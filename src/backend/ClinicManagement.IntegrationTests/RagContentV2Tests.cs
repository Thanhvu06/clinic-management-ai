using System.Net;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class RagContentV2Tests
{
    [Fact]
    public void Concrete_guidance_sections_are_short_and_pass_patient_output_safety()
    {
        foreach (var chunk in new MarkdownKnowledgeCatalog().Chunks)
        {
            Assert.InRange(chunk.Text.Length, 1, 550);
            var block = AiProviderOutputGuard.Inspect(ClinicManagement.Application.AI.Tools.AiActorRole.Patient, chunk.Text);
            Assert.True(block is null, $"{chunk.Id}: {block?.Code}: {chunk.Text}");
        }
    }

    [Fact]
    public async Task V2_output_contains_a_separate_unique_question_set_with_valid_sources()
    {
        var questions = await LoadQuestions("rag-dev-questions-v2.json");
        var baseline = await LoadQuestions("rag-dev-questions.json");
        var docIds = new MarkdownKnowledgeCatalog().Chunks.Select(c => c.Source.DocId).ToHashSet();

        Assert.InRange(questions.Count(q => q.ExpectedDocId is not null), 30, 40);
        Assert.Equal(10, questions.Count(q => q.ExpectedDocId is null));
        Assert.Equal(216, baseline.Length);
        Assert.Equal(questions.Length, questions.Select(q => q.Text).Distinct().Count());
        Assert.DoesNotContain(questions, q => baseline.Any(b => b.Text == q.Text));
        Assert.All(questions.Where(q => q.ExpectedDocId is not null), q => Assert.Contains(q.ExpectedDocId!, docIds));
    }

    [Fact]
    public async Task V2_end_to_end_BM25_reaches_85_percent_hit_at_3_and_90_percent_rejection_without_HTTP()
    {
        var questions = await LoadQuestions("rag-dev-questions-v2.json");
        using var handler = new OfflineHandler();
        using var http = new HttpClient(handler);
        using var retriever = CreateRetriever(http);
        var classifier = new VietnameseIntentClassifier();
        var safetyGuard = new AiSafetyGuard();
        int inScope = 0, outside = 0, hit3 = 0, rejected = 0;
        var missed = new List<string>();

        foreach (var question in questions)
        {
            var classification = classifier.Classify(question.Text, new IntentClassificationContext());
            var safety = safetyGuard.Inspect(question.Text);
            var candidate = !safety.IsEmergency && !safety.IsPromptInjection
                && !AiMedicalScopeGuard.IsPrescriptionRequest(question.Text)
                && KnowledgeQuestionPolicy.IsCandidate(question.Text, classification.Intent, false, classification);
            var ids = candidate
                ? (await retriever.RetrieveAsync(question.Text)).Hits.Select(h => h.Chunk.Source.DocId).Distinct().Take(3).ToArray()
                : [];
            if (question.ExpectedDocId is null)
            {
                outside++;
                if (ids.Length == 0) rejected++;
            }
            else
            {
                inScope++;
                if (ids.Contains(question.ExpectedDocId)) hit3++;
                else missed.Add(question.Text);
            }
        }

        Assert.True((double)hit3 / inScope >= .85, $"V2 Hit@3={hit3}/{inScope}; misses: {string.Join("; ", missed)}");
        Assert.True((double)rejected / outside >= .90, $"V2 correct rejection={rejected}/{outside}");
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("Siêu âm bụng nhịn ăn mấy tiếng?", "prepare-tests", "6–8 giờ")]
    [InlineData("Đường huyết lúc đói cần nhịn ăn mấy giờ?", "prepare-tests", "ít nhất 8 giờ")]
    [InlineData("Siêu âm bàng quang cần uống bao nhiêu nước?", "prepare-tests", "0,5–1 lít")]
    [InlineData("Chụp MRI cần báo thiết bị cấy ghép và tháo gì?", "prepare-tests", "máy tạo nhịp")]
    [InlineData("Đi khám nên đến sớm bao nhiêu phút?", "prepare-visit", "15 phút")]
    [InlineData("Trẻ nhỏ đi khám có cần mang sổ tiêm chủng không?", "bring-documents", "sổ tiêm chủng")]
    public async Task Preparation_retrieval_contains_the_concrete_guidance(string question, string docId, string evidence)
    {
        using var handler = new OfflineHandler();
        using var http = new HttpClient(handler);
        using var retriever = CreateRetriever(http);

        var result = await retriever.RetrieveAsync(question);

        Assert.Contains(result.Hits, h => h.Chunk.Source.DocId == docId && h.Chunk.Text.Contains(evidence, StringComparison.Ordinal));
        Assert.Equal(0, handler.Calls);
    }

    private static async Task<Question[]> LoadQuestions(string file) =>
        JsonSerializer.Deserialize<Question[]>(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "AI", "Knowledge", "Eval", file)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static ClinicKnowledgeRetriever CreateRetriever(HttpClient http)
    {
        var options = Options.Create(new OllamaOptions { IsEnabled = false });
        return new(new MarkdownKnowledgeCatalog(), new OllamaKnowledgeClient(http, options), options,
            new OfflineEnvironment(), NullLogger<ClinicKnowledgeRetriever>.Instance);
    }

    private sealed record Question(
        [property: System.Text.Json.Serialization.JsonPropertyName("question")] string Text, string? ExpectedDocId);

    private sealed class OfflineHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class OfflineEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "RagContentV2Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
