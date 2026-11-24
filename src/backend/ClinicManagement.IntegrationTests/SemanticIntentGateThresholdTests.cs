using System.Net;
using System.Text;
using System.Text.Json;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class SemanticIntentGateThresholdTests
{
    [Theory]
    [InlineData(0.549, false)]
    [InlineData(0.551, true)]
    public async Task Only_semantic_evidence_above_the_named_threshold_can_reach_generation(double cosine, bool accepted)
    {
        using var fixture = new Fixture(cosine);
        await fixture.Retriever.BuildEmbeddingsAsync();
        var answer = await fixture.Answerer.AnswerThroughIntentGateAsync("Tôi xếp những thứ gì vào túi trước buổi hẹn khám?");

        Assert.Equal(accepted, answer is not null);
        Assert.Equal(accepted ? 1 : 0, fixture.Handler.ChatCalls);
        Assert.Equal(0.55, ClinicKnowledgeRetriever.SemanticIntentGateMinCosine);
        if (accepted) Assert.Single(answer!.Sources);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Missing_model_or_incompatible_vector_fails_closed_without_calling_the_LLM(bool enabled, bool invalidVector)
    {
        using var fixture = new Fixture(0.9, enabled, invalidVector);
        await fixture.Retriever.BuildEmbeddingsAsync();
        Assert.Null(await fixture.Answerer.AnswerThroughIntentGateAsync("Tôi xếp những thứ gì vào túi trước buổi hẹn khám?"));
        Assert.Equal(0, fixture.Handler.ChatCalls);
    }

    [Fact]
    public async Task A_stricter_deployment_cosine_minimum_is_not_weakened_by_the_intent_fallback()
    {
        using var fixture = new Fixture(0.7, minCosine: 0.8);
        await fixture.Retriever.BuildEmbeddingsAsync();
        Assert.Null(await fixture.Answerer.AnswerThroughIntentGateAsync("Tôi xếp những thứ gì vào túi trước buổi hẹn khám?"));
        Assert.Equal(0, fixture.Handler.ChatCalls);
    }

    [Fact]
    public async Task The_new_semantic_embedding_and_generation_paths_receive_only_masked_questions()
    {
        using var fixture = new Fixture(0.9);
        await fixture.Retriever.BuildEmbeddingsAsync();
        var answer = await fixture.Answerer.AnswerThroughIntentGateAsync(
            "Tôi tên Đặng Thành Nhật, mã PAT-FAKE-991, email rg@example.invalid, cần chuẩn bị giấy tờ nào tới buổi khám?");

        Assert.NotNull(answer);
        var payloads = string.Join('\n', fixture.Handler.Payloads);
        Assert.DoesNotContain("Đặng Thành Nhật", payloads);
        Assert.DoesNotContain("PAT-FAKE-991", payloads);
        Assert.DoesNotContain("rg@example.invalid", payloads);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "clinic-intent-gate-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _http;
        public EmbeddingHandler Handler { get; }
        public ClinicKnowledgeRetriever Retriever { get; }
        public ClinicKnowledgeAnswerer Answerer { get; }

        public Fixture(double cosine, bool enabled = true, bool invalidVector = false, double minCosine = 0.2)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "public.md"),
                "---\nid: public-guide\ntitle: Hướng dẫn công khai\ncategory: clinic\nupdated: 2026-11-24\n---\n## Chuẩn bị\nMang giấy tờ nhận diện tới quầy khi đi khám.");
            Handler = new EmbeddingHandler(cosine, invalidVector);
            _http = new HttpClient(Handler);
            var options = Options.Create(new OllamaOptions { IsEnabled = enabled, MinScore = 1, MinCosine = minCosine });
            var ollama = new OllamaKnowledgeClient(_http, options);
            Retriever = new ClinicKnowledgeRetriever(new MarkdownKnowledgeCatalog(_root), ollama, options,
                new Environment(_root), NullLogger<ClinicKnowledgeRetriever>.Instance);
            Answerer = new ClinicKnowledgeAnswerer(Retriever, ollama);
        }

        public void Dispose()
        {
            Retriever.Dispose();
            _http.Dispose();
            Directory.Delete(_root, true);
        }
    }

    private sealed class EmbeddingHandler(double cosine, bool invalidVector) : HttpMessageHandler
    {
        private bool _documentEmbedded;
        public int ChatCalls { get; private set; }
        public List<string> Payloads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK);
            var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            Payloads.Add(payload);
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/chat", StringComparison.Ordinal))
            {
                ChatCalls++;
                return Json(new { message = new { content = "Mang giấy tờ nhận diện tới quầy khi đi khám." } });
            }
            var vector = !_documentEmbedded ? new[] { 1d, 0d }
                : invalidVector ? [1d, 0d, 0d] : [cosine, Math.Sqrt(1 - cosine * cosine)];
            _documentEmbedded = true;
            return Json(new { embeddings = new[] { vector } });
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }

    private sealed class Environment(string root) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "SemanticIntentGateTests";
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
