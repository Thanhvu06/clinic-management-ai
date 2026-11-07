using System.Net;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class RagOutOfScopeTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly MarkdownKnowledgeCatalog Catalog = new();
    private static readonly IHostEnvironment DevEnvironment = new FakeHostEnvironment("Development");

    [Theory]
    [InlineData("Bitcoin tăng hay giảm?")]
    [InlineData("thời tiết mai thế nào?")]
    [InlineData("viết code python sắp xếp mảng được k")]
    [InlineData("đội nào vô địch world cup?")]
    public async Task Out_of_scope_questions_do_not_enter_RAG_do_not_call_Ollama_and_have_no_sources(string question)
    {
        var ollamaCalls = 0;
        using var scope = Factory.Services.CreateScope();
        using var retriever = CreateRetriever(new OllamaOptions { IsEnabled = true }, _ =>
        {
            Interlocked.Increment(ref ollamaCalls);
            return JsonResponse("""{"message":{"content":"Không được gọi"}}""");
        });
        var answerer = CreateAnswerer(retriever, new OllamaOptions { IsEnabled = true }, _ =>
        {
            Interlocked.Increment(ref ollamaCalls);
            return JsonResponse("""{"message":{"content":"Không được gọi"}}""");
        });
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, (IClinicKnowledgeAnswerer)answerer);

        var response = await service.ChatAsync(new AiChatRequestDto { Message = question });

        Assert.DoesNotContain("Knowledge", response.DialogueOutcome ?? "");
        Assert.Equal("UnclearInput", response.DialogueOutcome);
        Assert.Equal(0, ollamaCalls);
        Assert.Empty(response.Sources);
    }

    [Theory]
    [InlineData("nhịn ăn bao lâu trước khi lấy máu", "prepare-tests")]
    [InlineData("trễ hẹn 20p có sao k", "appointments")]
    [InlineData("mang ho so cu di kham duoc khong", "bring-documents")]
    [InlineData("hướng dẫn đổi lịch hẹn", "appointments")]
    public async Task In_scope_questions_still_enter_RAG_and_return_expected_sources(string question, string expectedDocId)
    {
        var chatCalls = 0;
        using var scope = Factory.Services.CreateScope();
        using var retriever = CreateRetriever(new OllamaOptions { IsEnabled = true }, _ =>
            JsonResponse("""{"message":{"content":"Hướng dẫn từ tài liệu [1]."}}"""));
        var answerer = CreateAnswerer(retriever, new OllamaOptions { IsEnabled = true }, _ =>
        {
            Interlocked.Increment(ref chatCalls);
            return JsonResponse("""{"message":{"content":"Hướng dẫn từ tài liệu [1]."}}""");
        });
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, (IClinicKnowledgeAnswerer)answerer);

        var response = await service.ChatAsync(new AiChatRequestDto { Message = question });

        Assert.Equal("KnowledgeAnswer", response.DialogueOutcome);
        Assert.Equal(1, chatCalls);
        Assert.Contains(response.Sources, s => s.DocId == expectedDocId);
    }

    [Theory]
    [InlineData("Tài liệu chưa có thông tin về nội dung này.")]
    [InlineData("Chưa có thông tin trong tài liệu hướng dẫn hiện tại.")]
    [InlineData("Tài liệu không đề cập đến nội dung bạn hỏi.")]
    public async Task Ollama_no_information_answer_returns_null_and_falls_back_to_unclear_input_without_sources(string llmText)
    {
        var chatCalls = 0;
        using var scope = Factory.Services.CreateScope();
        using var retriever = CreateRetriever(new OllamaOptions { IsEnabled = true }, _ =>
            JsonResponse(JsonSerializer.Serialize(new { message = new { content = llmText } })));
        var answerer = CreateAnswerer(retriever, new OllamaOptions { IsEnabled = true }, _ =>
        {
            Interlocked.Increment(ref chatCalls);
            return JsonResponse(JsonSerializer.Serialize(new { message = new { content = llmText } }));
        });

        var directAnswer = await answerer.AnswerAsync("nhịn ăn bao lâu trước khi lấy máu");
        Assert.Null(directAnswer);

        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, (IClinicKnowledgeAnswerer)answerer);
        var response = await service.ChatAsync(new AiChatRequestDto { Message = "nhịn ăn bao lâu trước khi lấy máu" });

        Assert.Equal(2, chatCalls);
        Assert.DoesNotContain("Knowledge", response.DialogueOutcome ?? "");
        Assert.Equal("UnclearInput", response.DialogueOutcome);
        Assert.Empty(response.Sources);
    }

    [Fact]
    public async Task Cosine_below_threshold_rejects_hit_even_when_bm25_passes()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clinic-rag-cosine-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new OllamaOptions { IsEnabled = true, MinCosine = 0.55 };
            using var retriever = new ClinicKnowledgeRetriever(
                Catalog,
                new OllamaKnowledgeClient(new HttpClient(new DelegateHandler(request =>
                {
                    if (request.RequestUri!.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal))
                        return JsonResponse("""{"models":[]}""");
                    var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(body);
                    var input = doc.RootElement.GetProperty("input");
                    if (input.ValueKind == JsonValueKind.Array && input.GetArrayLength() > 1)
                    {
                        var vectors = Enumerable.Range(0, input.GetArrayLength()).Select(_ => new[] { 1f, 0f }).ToArray();
                        return JsonResponse(JsonSerializer.Serialize(new { embeddings = vectors }));
                    }
                    return JsonResponse("""{"embeddings":[[0.3,0.9539]]}""");
                })), Options.Create(options)),
                Options.Create(options),
                new FakeHostEnvironment("Development") { ContentRootPath = tempRoot },
                NullLogger<ClinicKnowledgeRetriever>.Instance);

            await retriever.BuildEmbeddingsAsync();
            Assert.True(retriever.EmbeddingsReady);

            var result = await retriever.RetrieveAsync("nhịn ăn bao lâu trước khi lấy máu");
            Assert.Empty(result.Hits);
            Assert.True(result.EmbeddingWasCalled);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task End_to_end_BM25_dev_set_reaches_at_least_90_percent_rejection_and_85_percent_hit_at_3()
    {
        var questions = LoadQuestions();
        var classifier = new VietnameseIntentClassifier();
        var safetyGuard = new AiSafetyGuard();
        using var retriever = CreateRetriever(new OllamaOptions { IsEnabled = false }, _ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var inScope = 0;
        var outside = 0;
        var hit3 = 0;
        var rejected = 0;

        foreach (var question in questions)
        {
            var classification = classifier.Classify(question.Text, new IntentClassificationContext());
            var safety = safetyGuard.Inspect(question.Text);
            var candidate = !safety.IsEmergency
                && !safety.IsPromptInjection
                && !AiMedicalScopeGuard.IsPrescriptionRequest(question.Text)
                && KnowledgeQuestionPolicy.IsCandidate(question.Text, classification.Intent, false, classification);

            var retrieval = candidate ? await retriever.RetrieveAsync(question.Text) : new KnowledgeRetrieval([]);
            var docIds = retrieval.Hits.Select(h => h.Chunk.Source.DocId).Distinct().ToArray();

            if (question.ExpectedDocId is null)
            {
                outside++;
                if (docIds.Length == 0) rejected++;
            }
            else
            {
                inScope++;
                var rank = Array.IndexOf(docIds, question.ExpectedDocId);
                if (rank is >= 0 and < 3) hit3++;
            }
        }

        Assert.Equal(156, inScope);
        Assert.Equal(60, outside);
        Assert.True((double)rejected / outside >= 0.90, $"End-to-end correct rejection was {(double)rejected / outside:P2}");
        Assert.True((double)hit3 / inScope >= 0.85, $"End-to-end Hit@3 was {(double)hit3 / inScope:P2}");
    }

    private static ClinicKnowledgeRetriever CreateRetriever(OllamaOptions options, Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(Catalog, new OllamaKnowledgeClient(new HttpClient(new DelegateHandler(responder)), Options.Create(options)),
            Options.Create(options), DevEnvironment, NullLogger<ClinicKnowledgeRetriever>.Instance);

    private static ClinicKnowledgeAnswerer CreateAnswerer(
        ClinicKnowledgeRetriever retriever,
        OllamaOptions options,
        Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(retriever, new OllamaKnowledgeClient(new HttpClient(new DelegateHandler(responder)), Options.Create(options)));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Question[] LoadQuestions() =>
        JsonSerializer.Deserialize<Question[]>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AI", "Knowledge", "Eval", "rag-dev-questions.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private sealed record Question([property: System.Text.Json.Serialization.JsonPropertyName("question")] string Text, string? ExpectedDocId);

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "ClinicManagement.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
