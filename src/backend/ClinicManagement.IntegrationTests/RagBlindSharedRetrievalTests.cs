extern alias RagEval;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.Knowledge;
using BlindAnswerOptions = RagEval::ClinicManagement.AI.RagEval.BlindAnswerOptions;
using BlindAnswerRetrieval = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRetrieval;
using BlindAnswerRun = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRun;
using BlindAnswerRuntime = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRuntime;

namespace ClinicManagement.IntegrationTests;

public sealed class RagBlindSharedRetrievalTests
{
    private const string Question = "Có cần nhịn ăn trước xét nghiệm không?";

    [Theory]
    [InlineData("bm25")]
    [InlineData("hybrid")]
    public async Task Runtime_modes_share_one_retrieval_and_only_generated_answer_calls_chat(string mode)
    {
        using var handler = new FakeOllama();
        using var runtime = await BlindAnswerRuntime.CreateAsync(handler, retrieval: mode);
        var embeddingCallsBefore = handler.EmbeddingCalls;
        var generated = await runtime.AnswerAsync(Question, true);
        var extracted = await runtime.AnswerAsync(Question, false);

        Assert.Equal(mode, runtime.Retrieval);
        Assert.Equal("KnowledgeAnswer", generated.DialogueOutcome);
        Assert.Equal("KnowledgeExtractive", extracted.DialogueOutcome);
        Assert.Equal(generated.Sources[0], Assert.Single(extracted.Sources));
        Assert.Equal(1, handler.AnswerCalls);
        Assert.Equal(mode == "hybrid" ? 1 : 0, handler.EmbeddingCalls - embeddingCallsBefore);
        if (mode == "bm25") Assert.Equal(0, handler.EmbeddingCalls);
    }

    [Theory]
    [InlineData("bm25")]
    [InlineData("hybrid")]
    public async Task Changing_fake_retriever_cannot_change_context_between_answer_modes(string mode)
    {
        var retriever = new ChangingRetriever();
        using var handler = new FakeOllama();
        using var runtime = await BlindAnswerRuntime.CreateAsync(handler, retrieval: mode, retriever: retriever);
        var generated = await runtime.AnswerAsync(Question, true);
        var extracted = await runtime.AnswerAsync(Question, false);

        Assert.Equal(1, retriever.Calls);
        Assert.Equal(new[] { "synthetic-a-1", "synthetic-b-1" }, generated.Sources.Select(source => source.DocId));
        Assert.Equal(generated.Sources[0], Assert.Single(extracted.Sources));
        Assert.Equal(retriever.LastResult!.Hits[0].Chunk.Text, extracted.Message);
        foreach (var hit in retriever.LastResult.Hits)
            Assert.Contains(hit.Chunk.Text, handler.LastChatPrompt);
        Assert.Equal(1, handler.AnswerCalls);
    }

    [Fact]
    public async Task Retrieval_snapshot_is_identical_in_a_pair_and_refreshes_for_the_next_pair()
    {
        var inner = new ChangingRetriever();
        var shared = new BlindAnswerRetrieval(inner);
        shared.BeginAnswer(Question, true);
        var first = await shared.RetrieveAsync(Question);
        shared.BeginAnswer(Question, false);
        Assert.Same(first, await shared.RetrieveAsync(Question));
        Assert.Equal(1, inner.Calls);

        shared.BeginAnswer(Question, false);
        var next = await shared.RetrieveAsync(Question);
        shared.BeginAnswer(Question, true);
        Assert.Same(next, await shared.RetrieveAsync(Question));
        Assert.NotEqual(first.Hits[0].Chunk.Id, next.Hits[0].Chunk.Id);
        Assert.Equal(2, inner.Calls);

        shared.BeginAnswer("Câu giả khác", true);
        await shared.RetrieveAsync("Câu giả khác");
        Assert.Equal(3, inner.Calls);
    }

    [Theory]
    [InlineData("bm25")]
    [InlineData("hybrid")]
    public async Task Run_exports_metadata_with_mode_seed_count_time_model_and_no_question_or_answer(string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "rag-shared-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var csv = Path.Combine(directory, "synthetic.csv");
            await File.WriteAllTextAsync(csv, "STT,Người viết,Câu hỏi,Loại,Tài liệu đúng\n5,Giả," + Question + ",Trong phạm vi,synthetic-a-1\n");
            var options = BlindAnswerOptions.Parse(["--csv", csv, "--out-dir", Path.Combine(directory, "output"),
                "--retrieval", mode, "--seed", "42"]);
            var retriever = new ChangingRetriever();
            using var runtime = await BlindAnswerRuntime.CreateAsync(new FakeOllama(), retrieval: options.Retrieval, retriever: retriever);
            var console = new StringWriter();
            await BlindAnswerRun.RunAsync(options, runtime, console);

            Assert.Equal(3, Directory.GetFiles(options.OutputDirectory).Length);
            var metadata = await File.ReadAllTextAsync(Path.Combine(options.OutputDirectory, "chay-thu.json"));
            using var json = JsonDocument.Parse(metadata);
            var root = json.RootElement;
            Assert.Equal(new[] { "seed", "retrieval", "count", "measuredAtUtc", "model" }, root.EnumerateObject().Select(property => property.Name));
            Assert.Equal(42, root.GetProperty("seed").GetInt32());
            Assert.Equal(mode, root.GetProperty("retrieval").GetString());
            Assert.Equal(1, root.GetProperty("count").GetInt32());
            Assert.True(root.GetProperty("measuredAtUtc").TryGetDateTimeOffset(out _));
            Assert.Equal("qwen2.5:7b", root.GetProperty("model").GetString());
            foreach (var privateText in new[] { Question, FakeOllama.Answer }.Concat(retriever.LastResult!.Hits.Select(hit => hit.Chunk.Text)))
            {
                Assert.DoesNotContain(privateText, metadata);
                Assert.DoesNotContain(privateText, console.ToString());
            }
            Assert.Contains("retrieval: " + mode, console.ToString());
            Assert.Equal(1, retriever.Calls);
            await Assert.ThrowsAsync<IOException>(() => BlindAnswerRun.RunAsync(options, runtime, console));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Retrieval_option_defaults_to_hybrid_and_rejects_invalid_or_duplicate_values()
    {
        var output = Path.Combine(Path.GetTempPath(), "rag-shared-options-" + Guid.NewGuid().ToString("N"));
        string[] common = ["--csv", "synthetic.csv", "--out-dir", output];
        Assert.Equal("hybrid", BlindAnswerOptions.Parse(common).Retrieval);
        Assert.Throws<ArgumentException>(() => BlindAnswerOptions.Parse([.. common, "--retrieval", "other"]));
        Assert.Throws<ArgumentException>(() => BlindAnswerOptions.Parse([.. common, "--retrieval", "bm25", "--retrieval", "hybrid"]));
    }

    private sealed class ChangingRetriever : IClinicKnowledgeRetriever
    {
        public int Calls { get; private set; }
        public KnowledgeRetrieval? LastResult { get; private set; }

        public Task<KnowledgeRetrieval> RetrieveAsync(string question, CancellationToken ct = default)
        {
            Calls++;
            LastResult = new([
                new(new("chunk-a-" + Calls, new("synthetic-a-" + Calls, "Tài liệu giả A", "Mục giả A"), "Đoạn giả A lượt " + Calls + "."), 1),
                new(new("chunk-b-" + Calls, new("synthetic-b-" + Calls, "Tài liệu giả B", "Mục giả B"), "Đoạn giả B lượt " + Calls + "."), 0.8)
            ]);
            return Task.FromResult(LastResult);
        }
    }

    private sealed class FakeOllama : HttpMessageHandler
    {
        public const string Answer = "Hỏi nơi lấy mẫu để được hướng dẫn chuẩn bị.";
        public int EmbeddingCalls { get; private set; }
        public int AnswerCalls { get; private set; }
        public string LastChatPrompt { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { models = Array.Empty<object>() }) };
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (request.RequestUri.AbsolutePath == "/api/embed")
            {
                EmbeddingCalls++;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                    { embeddings = body.RootElement.GetProperty("input").EnumerateArray().Select(_ => new[] { 1.0, 0.0 }).ToArray() }) };
            }
            Assert.Equal("/api/chat", request.RequestUri.AbsolutePath);
            var messages = body.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                AnswerCalls++;
                LastChatPrompt = messages[1].GetProperty("content").GetString()!;
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { done = true, message = new { content = Answer } }) };
        }
    }
}
