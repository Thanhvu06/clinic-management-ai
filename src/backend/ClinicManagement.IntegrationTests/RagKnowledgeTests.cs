using System.Net;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class RagKnowledgeTests
{
    [Theory]
    [InlineData("huy lich", "UnclearOrOutOfScope", false)]
    [InlineData("Tôi muốn đặt lịch mới rồi xem lịch đã đặt.", "UnclearOrOutOfScope", false)]
    [InlineData("Tôi bị đau đầu cần chuẩn bị gì", "SpecialtyRecommendation", false)]
    [InlineData("Cần chuẩn bị gì trước khi khám", "PricingInquiry", false)]
    [InlineData("Cần chuẩn bị gì trước khi khám", "FacilityInquiry", false)]
    [InlineData("Cần chuẩn bị gì trước khi khám", "ConfirmBooking", false)]
    [InlineData("Cần chuẩn bị gì trước khi khám", "UnclearOrOutOfScope", true)]
    [InlineData("nhin an truoc xet nghiem co can khong", "UnclearOrOutOfScope", false)]
    public void Candidate_policy_preserves_operational_and_symptom_routes(string message, string intent, bool explicitIntent)
    {
        var expected = message == "nhin an truoc xet nghiem co can khong";
        Assert.Equal(expected, KnowledgeQuestionPolicy.IsCandidate(message, intent, explicitIntent));
    }

    [Theory]
    [InlineData("Đi khám cần mang hồ sơ gì?", true)]
    [InlineData("Người già đi khám cần chuẩn bị gì?", true)]
    [InlineData("Tôi bị ho cần chuẩn bị gì?", false)]
    [InlineData("Đi khám lần đầu cần chuẩn bị gì?", true)]
    [InlineData("di kham lan dau chuan bi sao", true)]
    public void Guidance_distinguishes_Vietnamese_document_and_elder_terms_from_symptoms_and_prices(string message, bool expected) =>
        Assert.Equal(expected, KnowledgeQuestionPolicy.IsCandidate(message, "UnclearOrOutOfScope", false));

    [Fact]
    public void Public_document_extracts_pass_the_existing_output_guard()
    {
        var blocked = new MarkdownKnowledgeCatalog().Chunks.Where(chunk =>
            ClinicManagement.Infrastructure.AI.AiProviderOutputGuard.Inspect(
                ClinicManagement.Application.AI.Tools.AiActorRole.Patient, chunk.Text) is not null).Select(chunk => chunk.Id);
        Assert.Empty(blocked);
    }

    [Fact]
    public void Markdown_chunks_keep_metadata_headings_bounds_and_overlap()
    {
        var text = string.Join(' ', Enumerable.Range(0, 260).Select(i => "từ" + i));
        var chunks = MarkdownKnowledgeCatalog.Parse("---\nid: sample\ntitle: Tài liệu\ncategory: test\nupdated: 2026-11-07\n---\n## Chuẩn bị\n" + text);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => { Assert.Equal("sample", c.Source.DocId); Assert.Equal("Chuẩn bị", c.Source.Heading); Assert.InRange(c.Text.Length, 1, 550); });
        var endWords = chunks[0].Text.Split(' ').TakeLast(4);
        Assert.Contains(string.Join(' ', endWords), chunks[1].Text);
        Assert.Equal(chunks.Count, chunks.Select(c => c.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("nhịn ăn trước xét nghiệm")]
    [InlineData("nhin an truoc xet nghiem")]
    public void Bm25_recognizes_accented_and_unaccented_questions(string question)
    {
        var index = new Bm25KnowledgeIndex(new MarkdownKnowledgeCatalog().Chunks);
        Assert.Equal("prepare-tests", index.Search(question)[0].Chunk.Source.DocId);
    }

    [Fact]
    public void Rrf_combines_independent_ranks_and_cosine_checks_dimensions()
    {
        var a = new KnowledgeHit(new KnowledgeChunk("a", new("a", "A", "H"), "A"), 0.9);
        var b = new KnowledgeHit(new KnowledgeChunk("b", new("b", "B", "H"), "B"), 0.8);
        var c = new KnowledgeHit(new KnowledgeChunk("c", new("c", "C", "H"), "C"), 0.7);
        var hits = Bm25KnowledgeIndex.Fuse([a, b], [b, c]);
        Assert.Equal("b", hits[0].Chunk.Id);
        Assert.Equal(1.0 / 62 + 1.0 / 61, hits[0].Score, 10);
        Assert.Equal(1, Bm25KnowledgeIndex.Cosine([1, 0], [1, 0]));
        Assert.Equal(0, Bm25KnowledgeIndex.Cosine([1], [1, 0]));
    }

    [Fact]
    public async Task Below_threshold_and_outside_scope_do_not_call_Ollama()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected"));
        using var retriever = Retriever(handler, new OllamaOptions { IsEnabled = true, MinScore = 1 });
        Assert.Empty((await retriever.RetrieveAsync("nhịn ăn trước xét nghiệm")).Hits);
        Assert.Empty((await retriever.RetrieveAsync("đội bóng nào vô địch")).Hits);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_error_or_timeout_returns_extractive_answer_and_sources(bool timeout)
    {
        using var handler = new Handler(async (_, ct) =>
        {
            if (timeout) { await Task.Delay(Timeout.Infinite, ct); }
            return new(HttpStatusCode.ServiceUnavailable);
        });
        var options = Options.Create(new OllamaOptions { IsEnabled = true, TimeoutSeconds = 1 });
        using var http = new HttpClient(handler);
        var answerer = new ClinicKnowledgeAnswerer(Hits(), new OllamaKnowledgeClient(http, options));
        var answer = await answerer.AnswerAsync("nhịn ăn trước xét nghiệm");
        Assert.NotNull(answer);
        Assert.False(answer.Generated);
        Assert.True(answer.ProviderWasCalled);
        Assert.Equal("Tài liệu công khai về chuẩn bị xét nghiệm.", answer.Text);
        Assert.Equal("prepare-tests", Assert.Single(answer.Sources).DocId);
    }

    [Fact]
    public async Task Disabled_Ollama_extracts_without_http_and_no_match_returns_null()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected"));
        using var http = new HttpClient(handler);
        var client = new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions()));
        var answer = await new ClinicKnowledgeAnswerer(Hits(), client).AnswerAsync("nhịn ăn trước xét nghiệm");
        Assert.False(answer!.Generated);
        Assert.False(answer.ProviderWasCalled);
        var empty = new Mock<IClinicKnowledgeRetriever>();
        empty.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeRetrieval([]));
        Assert.Null(await new ClinicKnowledgeAnswerer(empty.Object, client).AnswerAsync("nhịn ăn trước xét nghiệm"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Circuit_opens_then_allows_one_probe_after_cooldown()
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var http = new HttpClient(handler);
        var time = new TestTime();
        var client = new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true }), time);
        Assert.True((await client.ChatAsync("Câu hỏi công khai", "Tài liệu", default)).WasCalled);
        Assert.False((await client.ChatAsync("Câu hỏi công khai", "Tài liệu", default)).WasCalled);
        Assert.False(await client.IsAvailableAsync(default));
        Assert.Equal(1, handler.Calls);
        time.Advance();
        Assert.True((await client.ChatAsync("Câu hỏi công khai", "Tài liệu", default)).WasCalled);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Generation_request_contains_only_server_owned_question_and_public_documents()
    {
        string? body = null;
        using var handler = new Handler(async (request, ct) =>
        {
            body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"message\":{\"content\":\"Hỏi nơi lấy mẫu trước xét nghiệm.\"}}", Encoding.UTF8, "application/json") };
        });
        using var http = new HttpClient(handler);
        var answerer = new ClinicKnowledgeAnswerer(Hits(), new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true })));
        var answer = await answerer.AnswerAsync("Tôi tên Nguyễn Riêng Tư, 56 tuổi, hồ sơ PAT-SECRET, nhịn ăn trước xét nghiệm được không?");
        Assert.True(answer!.Generated);
        using var json = JsonDocument.Parse(body!);
        var payload = json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Contains("Tài liệu công khai", payload);
        Assert.DoesNotContain("Nguyễn", payload);
        Assert.DoesNotContain("PAT-SECRET", payload);
        Assert.DoesNotContain("56", payload);
        Assert.False(json.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(0.1, json.RootElement.GetProperty("options").GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task Embedding_index_cache_reuses_content_hash_and_rebuilds_corruption()
    {
        var embedded = new List<string>();
        using var handler = new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var count = body.RootElement.GetProperty("input").GetArrayLength();
            embedded.AddRange(body.RootElement.GetProperty("input").EnumerateArray().Select(value => value.GetString()!));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { embeddings = Enumerable.Range(0, count).Select(_ => new float[] { 1, 0 }) })) };
        });
        var directory = Path.Combine(Path.GetTempPath(), "clinic-rag-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = Retriever(handler, new OllamaOptions { IsEnabled = true }, directory);
            await first.BuildEmbeddingsAsync();
            Assert.True(first.EmbeddingsReady);
            var calls = handler.Calls;
            using var second = Retriever(handler, new OllamaOptions { IsEnabled = true }, directory);
            await second.BuildEmbeddingsAsync();
            Assert.True(second.EmbeddingsReady);
            Assert.Equal(calls, handler.Calls);
            var cache = Assert.Single(Directory.GetFiles(Path.Combine(directory, "App_Data", "rag"), "*.json"));
            await File.WriteAllTextAsync(cache, "invalid json");
            using var third = Retriever(handler, new OllamaOptions { IsEnabled = true }, directory);
            await third.BuildEmbeddingsAsync();
            Assert.True(third.EmbeddingsReady);
            Assert.True(handler.Calls > calls);
            var result = await third.RetrieveAsync("Tên tôi Nguyễn Riêng Tư, mã PAT-SECRET, có cần nhịn ăn trước xét nghiệm không?");
            Assert.True(result.HybridUsed);
            Assert.True(result.EmbeddingWasCalled);
            Assert.NotEmpty(result.Hits);
            Assert.DoesNotContain(embedded, input => input.Contains("PAT-SECRET") || input.Contains("Nguyễn Riêng Tư"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Development_questions_have_BM25_hit_at_3_at_least_85_percent()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Offline test"));
        using var retriever = Retriever(handler, new OllamaOptions());
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "AI", "Knowledge", "Eval", "rag-dev-questions.json")));
        int count = 0, hit = 0;
        foreach (var row in json.RootElement.EnumerateArray())
        {
            if (row.GetProperty("expectedDocId").ValueKind == JsonValueKind.Null) continue;
            count++;
            var result = await retriever.RetrieveAsync(row.GetProperty("question").GetString()!);
            if (result.Hits.Select(h => h.Chunk.Source.DocId).Distinct().Take(3).Contains(row.GetProperty("expectedDocId").GetString())) hit++;
        }
        Assert.InRange(count, 150, 200);
        Assert.True((double)hit / count >= 0.85, $"Hit@3={hit}/{count}");
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("https://external.example")]
    [InlineData("http://external.example")]
    public void Model_endpoint_cannot_leave_the_local_machine(string url) =>
        Assert.Throws<ArgumentException>(() => new OllamaOptions { BaseUrl = url }.LocalEndpoint("api/chat"));

    [Fact]
    public async Task Background_initialization_does_not_block_startup_and_health_has_a_short_timeout()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); });
        using var http = new HttpClient(handler);
        var options = Options.Create(new OllamaOptions { IsEnabled = true });
        var client = new OllamaKnowledgeClient(http, options);
        using var retriever = Retriever(handler, options.Value);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await retriever.StartAsync(default);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
        await retriever.StopAsync(default);
        watch.Restart();
        Assert.False(await client.IsAvailableAsync(default));
        Assert.InRange(watch.Elapsed.TotalSeconds, 1.8, 3.0);
        Assert.Equal("127.0.0.1", options.Value.LocalEndpoint("api/chat").Host);
    }

    private static IClinicKnowledgeRetriever Hits()
    {
        var mock = new Mock<IClinicKnowledgeRetriever>();
        mock.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeRetrieval(
            [new KnowledgeHit(new("test:0", new("prepare-tests", "Chuẩn bị", "Xét nghiệm"), "Tài liệu công khai về chuẩn bị xét nghiệm."), 0.9)]));
        return mock.Object;
    }

    private static ClinicKnowledgeRetriever Retriever(Handler handler, OllamaOptions options, string? directory = null) =>
        new(new MarkdownKnowledgeCatalog(), new OllamaKnowledgeClient(new HttpClient(handler, false), Options.Create(options)),
            Options.Create(options), new EnvironmentStub(directory ?? Path.GetTempPath()), NullLogger<ClinicKnowledgeRetriever>.Instance);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return respond(request, ct); }
    }
    private sealed class TestTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now = _now.AddSeconds(31);
    }
    private sealed class EnvironmentStub(string path) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "RagTests";
        public string ContentRootPath { get; set; } = path;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
