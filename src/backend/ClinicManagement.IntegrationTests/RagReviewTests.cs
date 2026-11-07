using System.Net;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class RagReviewTests
{
    [Theory]
    [InlineData("cho tôi đặt lịch khám tim được không")]
    [InlineData("mai đặt lịch khám nội được không")]
    [InlineData("hủy lịch hẹn ngày mai được không")]
    [InlineData("đổi lịch hẹn ngày mai được không")]
    [InlineData("cho tôi đặt lịch khám tim cần chuẩn bị gì?")]
    public void Concrete_actions_do_not_enter_RAG(string message)
    {
        var intent = new VietnameseIntentClassifier().Classify(message, new IntentClassificationContext());
        Assert.False(KnowledgeQuestionPolicy.IsCandidate(message, intent.Intent, false, intent));
    }

    [Theory]
    [InlineData("đặt lịch khám như thế nào")]
    [InlineData("hướng dẫn đổi lịch hẹn")]
    [InlineData("Đi khám thường có bắt buộc nhịn ăn ko ạ?")]
    [InlineData("Lấy số rồi chờ khám ở đâu vậy?")]
    [InlineData("Đến trễ hơn 30 phút có bị vắng mặt không?")]
    [InlineData("mang ho so cu di kham duoc khong")]
    [InlineData("lay ket qua o dau vay")]
    public void General_questions_pass_the_candidate_gate(string message)
    {
        var intent = new VietnameseIntentClassifier().Classify(message, new IntentClassificationContext());
        Assert.True(KnowledgeQuestionPolicy.IsCandidate(message, intent.Intent, false, intent), intent.Intent);
    }

    [Theory]
    [InlineData("bị ho 3 ngày")]
    [InlineData("dau dau chong mat")]
    [InlineData("đau ngực khó thở")]
    public void Symptoms_stay_outside_RAG(string message)
    {
        var intent = new VietnameseIntentClassifier().Classify(message, new IntentClassificationContext());
        Assert.False(KnowledgeQuestionPolicy.IsCandidate(message, intent.Intent, false, intent));
        if (message == "đau ngực khó thở") Assert.True(new AiSafetyGuard().Inspect(message).IsEmergency);
    }

    [Theory]
    [MemberData(nameof(SymptomSpecialtyRulesTests.Examples), MemberType = typeof(SymptomSpecialtyRulesTests))]
    public void Existing_clinical_complaints_remain_symptoms_even_with_question_words(string message, string specialtyCode)
    {
        Assert.StartsWith("SP", specialtyCode);
        message += ", có cần chuẩn bị gì không?";
        var intent = new VietnameseIntentClassifier().Classify(message, new IntentClassificationContext());
        Assert.False(KnowledgeQuestionPolicy.IsCandidate(message, intent.Intent, false, intent));
    }

    [Fact]
    public void Guidance_with_an_extracted_selection_stays_in_the_action_flow()
    {
        var classification = new IntentClassificationResult { Intent = AiChatIntentTypes.StartBooking };
        Assert.True(KnowledgeQuestionPolicy.IsCandidate("đặt lịch khám như thế nào", classification.Intent, false, classification));
        foreach (var selected in new[]
        {
            new IntentClassificationResult { ExtractedDoctorName = "Bác sĩ mẫu" },
            new IntentClassificationResult { ExtractedDate = "2026-11-08" },
            new IntentClassificationResult { ExtractedTimePreference = "09:00" },
            new IntentClassificationResult { ExtractedRelativeDoctorIndex = 1 },
            new IntentClassificationResult { ExtractedRelativeSlotIndex = 2 }
        })
            Assert.False(KnowledgeQuestionPolicy.IsCandidate("đặt lịch khám như thế nào", classification.Intent, false, selected));
        Assert.False(KnowledgeQuestionPolicy.IsCandidate("cách đặt lịch khám tim mạch", classification.Intent, false, classification));
    }

    [Theory]
    [InlineData("hướng dẫn đổi lịch hẹn", true)]
    [InlineData("cách hủy lịch hẹn như thế nào", true)]
    [InlineData("cách hủy lịch hẹn của tôi", false)]
    [InlineData("hướng dẫn xem kết quả của em", false)]
    [InlineData("hướng dẫn xem kết quả của con", false)]
    [InlineData("cách xem đơn thuốc của mình", false)]
    [InlineData("cách xem đơn thuốc của chính mình", false)]
    [InlineData("hướng dẫn đổi lịch mã APT-001", false)]
    [InlineData("hướng dẫn đổi lịch số 123", false)]
    public void Early_RAG_is_limited_to_unowned_general_guidance(string message, bool expected)
    {
        var classification = new VietnameseIntentClassifier().Classify(message, new IntentClassificationContext());
        Assert.Equal(expected, KnowledgeQuestionPolicy.IsEarlyGuidance(message, classification));
    }

    [Fact]
    public async Task Chat_and_query_embeddings_receive_the_same_masked_real_question_without_history()
    {
        const string question = "tôi tên là Trần Bí Mật, email secret@example.test, số 0912345678, CCCD 123456789012, hồ sơ PAT-SECRET; nhịn ăn bao lâu trước khi lấy máu?";
        var masked = KnowledgePrivacyMask.Mask(question);
        var queries = new List<string>();
        string? chatQuestion = null;
        using var handler = new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get) return Json(new { models = Array.Empty<object>() });
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("30m", body.RootElement.GetProperty("keep_alive").GetString());
            if (request.RequestUri!.AbsolutePath == "/api/embed")
            {
                var inputs = body.RootElement.GetProperty("input").EnumerateArray().Select(value => value.GetString()!).ToArray();
                queries.AddRange(inputs);
                return Json(new { embeddings = inputs.Select(_ => new float[] { 1, 0 }) });
            }
            var messages = body.RootElement.GetProperty("messages");
            Assert.Equal(2, messages.GetArrayLength());
            var content = messages[1].GetProperty("content").GetString()!;
            chatQuestion = content.Split("\nTài liệu công khai:", 2)[0].Replace("Câu hỏi đã làm sạch: ", "");
            Assert.DoesNotContain("Lịch sử hội thoại:", content);
            return Json(new { message = new { content = "Hỏi nơi lấy mẫu về thời gian nhịn ăn." } });
        });
        var directory = TemporaryDirectory();
        try
        {
            using var http = new HttpClient(handler);
            var options = Options.Create(new OllamaOptions { IsEnabled = true });
            var client = new OllamaKnowledgeClient(http, options);
            using var retriever = new ClinicKnowledgeRetriever(new MarkdownKnowledgeCatalog(), client, options,
                new EnvironmentStub(directory), NullLogger<ClinicKnowledgeRetriever>.Instance);
            await retriever.BuildEmbeddingsAsync();
            queries.Clear();
            var answer = await new ClinicKnowledgeAnswerer(retriever, client).AnswerAsync(question);
            Assert.True(answer!.Generated);
            Assert.Equal(masked, Assert.Single(queries));
            Assert.Equal(masked, chatQuestion);
            Assert.Contains("nhịn ăn bao lâu trước khi lấy máu?", chatQuestion);
            foreach (var secret in new[] { "Trần Bí Mật", "secret@example.test", "0912345678", "123456789012", "PAT-SECRET" })
            {
                Assert.DoesNotContain(secret, chatQuestion);
                Assert.DoesNotContain(secret, queries[0]);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("tên tôi là Nguyễn Riêng Tư, cần nhịn ăn không?", "Nguyễn Riêng Tư")]
    [InlineData("con tôi tên Lê Giả Lập có cần nhịn ăn không?", "Lê Giả Lập")]
    [InlineData("toi ten la Nguyen Gia Lap, nhin an bao lau?", "Nguyen Gia Lap")]
    [InlineData("mã lịch hẹn APT-261107-0099, cần mang gì?", "APT-261107-0099")]
    [InlineData("CCCD 123 456 789 012, cần mang gì?", "123 456 789 012")]
    [InlineData("mã hồ sơ là XYZABC, cần mang gì?", "XYZABC")]
    [InlineData("ma lich hen QW123, can mang gi?", "QW123")]
    public void Privacy_mask_removes_identifiers_and_is_idempotent(string question, string secret)
    {
        var masked = KnowledgePrivacyMask.Mask(question);
        Assert.DoesNotContain(secret, masked);
        Assert.Contains("?", masked);
        Assert.Equal(masked, KnowledgePrivacyMask.Mask(masked));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Citations_use_the_60_percent_threshold_deduplicate_and_stop_at_two(bool threeStrong)
    {
        KnowledgeHit Hit(string id, string heading, double score) => new(new(id + heading, new(id, id, heading), "Tài liệu " + id), score);
        var hits = threeStrong ? new[] { Hit("a", "h", 1), Hit("b", "h", .9), Hit("c", "h", .8) }
            : new[] { Hit("a", "h", 1), Hit("a", "h", .9), Hit("b", "h", .6), Hit("c", "h", .59) };
        var retriever = new Mock<IClinicKnowledgeRetriever>();
        retriever.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeRetrieval(hits));
        using var handler = new Handler((_, _) => Task.FromResult(Json(new { message = new { content = "Hướng dẫn đã đối chiếu." } })));
        using var http = new HttpClient(handler);
        var client = new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true }));
        var answer = await new ClinicKnowledgeAnswerer(retriever.Object, client).AnswerAsync("Nhịn ăn bao lâu?");
        Assert.Equal(new[] { "a", "b" }, answer!.Sources.Select(source => source.DocId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Background_warms_the_chat_model_only_when_enabled(bool enabled)
    {
        var warmup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get) return Json(new { models = Array.Empty<object>() });
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("30m", body.RootElement.GetProperty("keep_alive").GetString());
            if (request.RequestUri!.AbsolutePath == "/api/chat")
            {
                Assert.Empty(body.RootElement.GetProperty("messages").EnumerateArray());
                warmup.SetResult();
                return Json(new { done = true });
            }
            var count = body.RootElement.GetProperty("input").GetArrayLength();
            return Json(new { embeddings = Enumerable.Range(0, count).Select(_ => new float[] { 1, 0 }) });
        });
        var directory = TemporaryDirectory();
        try
        {
            using var http = new HttpClient(handler);
            var options = Options.Create(new OllamaOptions { IsEnabled = enabled });
            var client = new OllamaKnowledgeClient(http, options);
            using var retriever = new ClinicKnowledgeRetriever(new MarkdownKnowledgeCatalog(), client, options,
                new EnvironmentStub(directory), NullLogger<ClinicKnowledgeRetriever>.Instance);
            await retriever.StartAsync(default);
            if (enabled) await warmup.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await retriever.StopAsync(default);
            Assert.Equal(enabled, warmup.Task.IsCompletedSuccessfully);
            if (!enabled) Assert.Equal(0, handler.Calls);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task End_to_end_BM25_hits_at_least_85_percent_of_all_in_scope_development_questions()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Offline evaluation must not call HTTP."));
        using var http = new HttpClient(handler);
        var options = Options.Create(new OllamaOptions());
        using var retriever = new ClinicKnowledgeRetriever(new MarkdownKnowledgeCatalog(), new OllamaKnowledgeClient(http, options), options,
            new EnvironmentStub(Path.GetTempPath()), NullLogger<ClinicKnowledgeRetriever>.Instance);
        var classifier = new VietnameseIntentClassifier();
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "AI", "Knowledge", "Eval", "rag-dev-questions.json")));
        var inScope = 0; var hits = 0;
        foreach (var row in json.RootElement.EnumerateArray())
        {
            if (row.GetProperty("expectedDocId").ValueKind == JsonValueKind.Null) continue;
            inScope++;
            var question = row.GetProperty("question").GetString()!;
            var classification = classifier.Classify(question, new IntentClassificationContext());
            var safety = new AiSafetyGuard().Inspect(question);
            if (safety.IsEmergency || safety.IsPromptInjection || AiMedicalScopeGuard.IsPrescriptionRequest(question) ||
                !KnowledgeQuestionPolicy.IsCandidate(question, classification.Intent, false, classification)) continue;
            var result = await retriever.RetrieveAsync(question);
            if (result.Hits.Select(hit => hit.Chunk.Source.DocId).Distinct().Take(3).Contains(row.GetProperty("expectedDocId").GetString())) hits++;
        }
        Assert.Equal(156, inScope);
        Assert.True((double)hits / inScope >= .85, $"End-to-end Hit@3={hits}/{inScope}");
        Assert.Equal(0, handler.Calls);
    }

    private static string TemporaryDirectory() { var path = Path.Combine(Path.GetTempPath(), "rag-review-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return respond(request, ct); }
    }
    private sealed class EnvironmentStub(string path) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "RagReviewTests";
        public string ContentRootPath { get; set; } = path;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
