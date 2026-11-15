using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class RagLowIdfFallbackTests
{
    private const string LowIdf = "frequent familiar";

    [Fact]
    public async Task Default_rejects_low_idf_and_keeps_normal_hybrid_hits()
    {
        using var baseline = new Fixture(new OllamaOptions(), secondCosine: 0.8f);
        using var enabled = new Fixture(new OllamaOptions { LowIdfFallbackCosine = 1 }, secondCosine: 0.8f);
        await baseline.BuildAsync();
        await enabled.BuildAsync();

        var rejected = await baseline.Retriever.RetrieveAsync(LowIdf);

        Assert.Empty(rejected.Hits);
        Assert.False(rejected.EmbeddingWasCalled);
        Assert.False(rejected.HybridUsed);
        Assert.Equal(0, baseline.Transport.QueryCalls);

        var normal = await baseline.Retriever.RetrieveAsync("frequent familiar unique");
        var withFallback = await enabled.Retriever.RetrieveAsync("frequent familiar unique");
        Assert.True(normal.HybridUsed);
        Assert.Contains(normal.Hits, hit => hit.Chunk.Id == "first:0");
        Assert.Equal(normal.HybridUsed, withFallback.HybridUsed);
        Assert.Equal(normal.Hits, withFallback.Hits);
    }

    [Fact]
    public async Task Threshold_can_be_met_by_a_lexical_candidate_other_than_the_first()
    {
        using var fixture = new Fixture(new OllamaOptions { LowIdfFallbackCosine = 1 });
        await fixture.BuildAsync();

        var result = await fixture.Retriever.RetrieveAsync(LowIdf);

        Assert.True(result.HybridUsed);
        Assert.True(result.EmbeddingWasCalled);
        Assert.Equal(["second:0", "first:0"], result.Hits.Select(hit => hit.Chunk.Id));
        Assert.Equal(1, fixture.Transport.QueryCalls);
    }

    [Fact]
    public async Task A_high_cosine_outside_the_lexical_candidates_does_not_rescue_low_idf()
    {
        using var fixture = new Fixture(new OllamaOptions { LowIdfFallbackCosine = 0.65 }, secondCosine: 0.6f);
        await fixture.BuildAsync();

        var result = await fixture.Retriever.RetrieveAsync(LowIdf);

        Assert.Empty(result.Hits);
        Assert.True(result.EmbeddingWasCalled);
        Assert.Equal(1, fixture.Transport.QueryCalls);
    }

    [Theory]
    [InlineData(LowIdf, 1)]
    [InlineData("frequent familiar blue green orange", 0.15)]
    public async Task MinScore_and_query_coverage_are_still_required(string question, double minScore)
    {
        using var fixture = new Fixture(new OllamaOptions { LowIdfFallbackCosine = 0.5 }, minScore: minScore);
        await fixture.BuildAsync();

        var result = await fixture.Retriever.RetrieveAsync(question);

        Assert.Empty(result.Hits);
        Assert.False(result.EmbeddingWasCalled);
        Assert.Equal(0, fixture.Transport.QueryCalls);
    }

    [Fact]
    public async Task Low_idf_is_rejected_while_document_embeddings_are_unavailable()
    {
        using var fixture = new Fixture(new OllamaOptions { LowIdfFallbackCosine = 0.5 });

        var result = await fixture.Retriever.RetrieveAsync(LowIdf);

        Assert.False(fixture.Retriever.EmbeddingsReady);
        Assert.Empty(result.Hits);
        Assert.False(result.EmbeddingWasCalled);
        Assert.Equal(0, fixture.Transport.QueryCalls);
    }

    [Fact]
    public async Task Low_idf_is_rejected_if_query_embedding_fails()
    {
        using var fixture = new Fixture(new OllamaOptions { LowIdfFallbackCosine = 0.5 });
        await fixture.BuildAsync();
        fixture.Transport.FailQuery = true;

        var result = await fixture.Retriever.RetrieveAsync(LowIdf);

        Assert.Empty(result.Hits);
        Assert.True(result.EmbeddingWasCalled);
        Assert.False(result.HybridUsed);
    }

    [Fact]
    public async Task Normal_hybrid_cosine_gate_still_applies_after_the_fallback()
    {
        using var fixture = new Fixture(new OllamaOptions { LowIdfFallbackCosine = 0.5, MinCosine = 0.5 });
        await fixture.BuildAsync();

        var result = await fixture.Retriever.RetrieveAsync(LowIdf);

        Assert.Empty(result.Hits);
        Assert.True(result.HybridUsed);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rag-low-idf-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _http;
        public EmbeddingTransport Transport { get; }
        public ClinicKnowledgeRetriever Retriever { get; }

        public Fixture(OllamaOptions settings, float secondCosine = 1, double minScore = 0.15)
        {
            Directory.CreateDirectory(_root);
            WriteDoc("first", "frequent familiar", "frequent familiar unique");
            WriteDoc("second", "secondary", "frequent familiar");
            WriteDoc("unrelated", "other", "ambiguous placebo");
            settings.IsEnabled = true;
            settings.MinTermIdf = 0.75;
            settings.MinScore = minScore;
            var options = Options.Create(settings);
            Transport = new EmbeddingTransport(secondCosine);
            _http = new HttpClient(Transport);
            var environment = Mock.Of<IHostEnvironment>(env => env.ContentRootPath == _root);
            Retriever = new ClinicKnowledgeRetriever(new MarkdownKnowledgeCatalog(_root), new OllamaKnowledgeClient(_http, options),
                options, environment, NullLogger<ClinicKnowledgeRetriever>.Instance);
        }

        private void WriteDoc(string id, string title, string text) => File.WriteAllText(Path.Combine(_root, id + ".md"),
            $"---\nid: {id}\ntitle: {title}\ncategory: test\nupdated: 2026-11-01\n---\n\n{text}\n");

        public async Task BuildAsync()
        {
            await Retriever.BuildEmbeddingsAsync();
            Assert.True(Retriever.EmbeddingsReady);
        }

        public void Dispose()
        {
            Retriever.Dispose();
            _http.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class EmbeddingTransport(float secondCosine) : HttpMessageHandler
    {
        public int QueryCalls { get; private set; }
        public bool FailQuery { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK);
            Assert.Equal("/api/embed", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var inputs = body.RootElement.GetProperty("input").EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (!inputs[0].Contains(" — ", StringComparison.Ordinal))
            {
                QueryCalls++;
                return FailQuery ? new(HttpStatusCode.ServiceUnavailable) : Response([[1, 0]]);
            }
            return Response(inputs.Select<string, float[]>(text =>
                text.StartsWith("frequent familiar —", StringComparison.Ordinal) ? [0.4f, MathF.Sqrt(0.84f)]
                    : text.StartsWith("secondary —", StringComparison.Ordinal) ? [secondCosine, MathF.Sqrt(1 - secondCosine * secondCosine)]
                    : [1, 0]).ToArray());
        }

        private static HttpResponseMessage Response(float[][] embeddings) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(new { embeddings }) };
    }
}
