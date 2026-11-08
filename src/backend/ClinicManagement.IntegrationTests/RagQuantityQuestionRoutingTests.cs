using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class RagQuantityQuestionRoutingTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("mỡ máu nhịn ăn mấy tiếng", "10–12 giờ")]
    [InlineData("đường huyết nhịn ăn mấy giờ", "ít nhất 8 giờ")]
    [InlineData("siêu âm bụng uống bao nhiêu nước", "0,5–1 lít")]
    [InlineData("mo mau nhin an may tieng", "10–12 giờ")]
    public async Task Preparation_quantity_questions_return_real_document_sources(string message, string evidence)
    {
        using var scope = Factory.Services.CreateScope();
        using var http = new HttpClient(new OfflineHandler());
        using var retriever = CreateRetriever(http);
        var service = CreateService(scope.ServiceProvider, retriever, http);

        var response = await service.ChatAsync(new AiChatRequestDto { Message = message });

        Assert.Equal("KnowledgeExtractive", response.DialogueOutcome);
        Assert.Contains(response.Sources, source => source.DocId == "prepare-tests");
        Assert.Contains(evidence, response.Message);
        Assert.Empty(response.SpecialtySuggestions);
        Assert.False(response.ProviderWasCalled);
    }

    [Theory]
    [InlineData("máy đo huyết áp loại nào tốt", false)]
    [InlineData("tôi bị đau bụng mấy ngày nay", true)]
    public async Task Device_and_symptom_questions_do_not_return_knowledge_answers(string message, bool symptoms)
    {
        using var scope = Factory.Services.CreateScope();
        using var http = new HttpClient(new OfflineHandler());
        using var retriever = CreateRetriever(http);
        var service = CreateService(scope.ServiceProvider, retriever, http);

        var response = await service.ChatAsync(new AiChatRequestDto { Message = message });

        Assert.DoesNotContain("Knowledge", response.DialogueOutcome ?? "");
        Assert.Empty(response.Sources);
        Assert.False(response.ProviderWasCalled);
        if (symptoms)
        {
            Assert.False(KnowledgeQuestionPolicy.IsCandidate(message, AiChatIntentTypes.UnclearOrOutOfScope, false));
            Assert.NotEmpty(response.SpecialtySuggestions);
        }
    }

    [Fact]
    public async Task Chest_pain_and_breathlessness_keep_the_115_emergency_route()
    {
        using var scope = Factory.Services.CreateScope();
        using var http = new HttpClient(new OfflineHandler());
        using var retriever = CreateRetriever(http);
        var service = CreateService(scope.ServiceProvider, retriever, http);

        var response = await service.ChatAsync(new AiChatRequestDto { Message = "đau ngực khó thở" });

        Assert.Equal("SafetyEmergency", response.DialogueOutcome);
        Assert.Equal("EMERGENCY", response.Urgency);
        Assert.Contains("115", response.Message);
        Assert.Empty(response.Sources);
        Assert.False(response.ProviderWasCalled);
    }

    [Theory]
    [InlineData("mấy giờ")]
    [InlineData("mấy tiếng")]
    [InlineData("mấy ngày")]
    [InlineData("mấy phút")]
    [InlineData("mấy lần")]
    [InlineData("mấy lít")]
    [InlineData("bao nhiêu")]
    public void Quantity_phrases_are_questions_without_a_question_mark(string message)
    {
        Assert.True(KnowledgeQuestionPolicy.IsCandidate(message, AiChatIntentTypes.UnclearOrOutOfScope, false));
    }

    [Theory]
    [InlineData("máy")]
    [InlineData("máy đo huyết áp")]
    [InlineData("may tiengkhac")]
    public void Standalone_may_and_partial_phrases_do_not_open_the_question_gate(string message)
    {
        Assert.False(KnowledgeQuestionPolicy.IsCandidate(message, AiChatIntentTypes.UnclearOrOutOfScope, false));
    }

    [Theory]
    [InlineData(AiChatIntentTypes.StartBooking)]
    [InlineData(AiChatIntentTypes.ViewAppointments)]
    [InlineData(AiChatIntentTypes.DiagnosticLookup)]
    public void Quantity_phrases_do_not_override_action_intents(string intent)
    {
        Assert.False(KnowledgeQuestionPolicy.IsCandidate("mỡ máu nhịn ăn mấy tiếng", intent, false,
            new IntentClassificationResult { Intent = intent, IsClear = false }));
    }

    [Fact]
    public void Explicit_intent_entity_and_ownership_guards_are_preserved()
    {
        Assert.False(KnowledgeQuestionPolicy.IsCandidate("mỡ máu nhịn ăn mấy tiếng",
            AiChatIntentTypes.UnclearOrOutOfScope, true));
        var classification = new IntentClassificationResult
        {
            Intent = AiChatIntentTypes.StartBooking,
            IsClear = false,
            ExtractedDoctorName = "Nguyễn Minh Khải"
        };
        Assert.False(KnowledgeQuestionPolicy.IsCandidate("cách đặt lịch mấy ngày",
            classification.Intent, false, classification));
        Assert.False(KnowledgeQuestionPolicy.IsEarlyGuidance("cách xem kết quả của tôi mấy ngày",
            new IntentClassificationResult { Intent = AiChatIntentTypes.DiagnosticLookup, IsClear = false }));
    }

    private AiSpecialtyService CreateService(IServiceProvider services, ClinicKnowledgeRetriever retriever, HttpClient http)
    {
        Factory.MockAiProvider.Setup(provider => provider.ChatWithAiAsync(It.IsAny<string>(),
                It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult { IsSuccess = false, Status = "Disabled" });
        var client = new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = false }));
        IClinicKnowledgeAnswerer answerer = new ClinicKnowledgeAnswerer(retriever, client);
        return ActivatorUtilities.CreateInstance<AiSpecialtyService>(services, answerer);
    }

    private static ClinicKnowledgeRetriever CreateRetriever(HttpClient http)
    {
        var options = Options.Create(new OllamaOptions { IsEnabled = false });
        return new(new MarkdownKnowledgeCatalog(), new OllamaKnowledgeClient(http, options), options,
            new OfflineEnvironment(), NullLogger<ClinicKnowledgeRetriever>.Instance);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Offline routing tests must not call Ollama.");
    }

    private sealed class OfflineEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "RagQuantityQuestionRoutingTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
