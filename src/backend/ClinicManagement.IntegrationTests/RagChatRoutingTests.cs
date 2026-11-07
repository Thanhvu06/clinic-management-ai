using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class RagChatRoutingTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Knowledge_answer_returns_sources_and_preserves_booking_draft()
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await Send(patient, new AiChatRequestDto { Message = "Có cần nhịn ăn trước xét nghiệm không?",
            PendingSpecialtyId = SpecialtyEntityId, PendingDoctorId = DoctorEntityId, Reason = "Tái khám", DraftVersion = 7 });
        Assert.Equal("KnowledgeExtractive", response.DialogueOutcome);
        Assert.False(response.ProviderWasCalled);
        Assert.Equal("DeterministicFallback", response.ExecutionMode);
        Assert.Equal("prepare-tests", response.Sources[0].DocId);
        Assert.Equal("knowledge_sources", Assert.Single(response.ToolResults).ResultType);
        Assert.NotNull(response.BookingDraft);
        Assert.Equal(SpecialtyEntityId, response.BookingDraft.SpecialtyId);
        Assert.Equal(DoctorEntityId, response.BookingDraft.DoctorId);
        Assert.Equal("Tái khám", response.BookingDraft.Reason);
        Assert.Equal(7, response.BookingDraft.Version);
    }

    [Theory]
    [InlineData("đau ngực khó thở", "SafetyEmergency")]
    [InlineData("Khám nội tổng quát giá bao nhiêu?", "PricingInquiryResolved")]
    [InlineData("Phòng khám có giờ làm việc thế nào?", "FacilityInquiryResolved")]
    public async Task Emergency_and_database_handlers_take_precedence(string message, string outcome)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await Send(patient, new AiChatRequestDto { Message = message });
        Assert.Equal(outcome, response.DialogueOutcome);
        Assert.Empty(response.Sources);
        Assert.False(response.ProviderWasCalled);
        if (outcome == "SafetyEmergency") Assert.Contains("115", response.Message);
    }

    [Fact]
    public async Task Symptoms_keep_specialty_recommendation_and_no_knowledge_sources()
    {
        Factory.MockAiProvider.Setup(provider => provider.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(),
            It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult { IsSuccess = false, Status = "Disabled" });
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await Send(patient, new AiChatRequestDto { Message = "Tôi bị đau đầu chóng mặt" });
        Assert.NotEmpty(response.SpecialtySuggestions);
        Assert.Empty(response.Sources);
        Assert.DoesNotContain("Knowledge", response.DialogueOutcome ?? "");
    }

    [Theory]
    [InlineData("Bạn đang đau ngực khó thở.", "PROVIDER_OUTPUT_EMERGENCY", "EMERGENCY")]
    [InlineData("Uống paracetamol 500mg mỗi ngày.", "MEDICAL_PRESCRIPTION_OUT_OF_SCOPE", "ROUTINE")]
    public async Task Generated_answer_uses_existing_output_block_and_emergency_action(string generated, string code, string urgency)
    {
        using var scope = Factory.Services.CreateScope();
        var answerer = new Mock<IClinicKnowledgeAnswerer>();
        answerer.Setup(a => a.AnswerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeAnswer(generated, [new("prepare-tests", "Chuẩn bị", "Xét nghiệm")], true, true));
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, answerer.Object);
        var response = await service.ChatAsync(new AiChatRequestDto { Message = "Có cần nhịn ăn trước xét nghiệm không?" });
        Assert.Equal(code, response.DialogueOutcome);
        Assert.Equal(urgency, response.Urgency);
        Assert.Equal("SafetyBlocked", response.ProviderState);
        Assert.Equal("ManualHandoff", response.ExecutionMode);
        Assert.True(response.ProviderWasCalled);
        Assert.Empty(response.Sources);
        if (urgency == "EMERGENCY") Assert.Equal("tel:115", Assert.Single(response.Actions).Payload.TargetUrl);
    }

    [Fact]
    public async Task Generated_answer_reports_provider_assistance_and_citations()
    {
        using var scope = Factory.Services.CreateScope();
        var answerer = new Mock<IClinicKnowledgeAnswerer>();
        answerer.Setup(a => a.AnswerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeAnswer("Hỏi nơi lấy mẫu để biết hướng dẫn chuẩn bị.", [new("prepare-tests", "Chuẩn bị", "Xét nghiệm")], true, true));
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, answerer.Object);
        var response = await service.ChatAsync(new AiChatRequestDto { Message = "Có cần nhịn ăn trước xét nghiệm không?" });
        Assert.Equal("KnowledgeAnswer", response.DialogueOutcome);
        Assert.Equal("ProviderAssisted", response.ExecutionMode);
        Assert.True(response.ProviderWasCalled);
        Assert.False(response.FallbackActive);
        Assert.Single(response.Sources);
    }

    [Fact]
    public async Task Existing_guest_chat_authorization_remains_enforced()
    {
        using var guest = Factory.CreateClient();
        var response = await guest.PostAsJsonAsync("/api/v1/ai/chat", new { message = "Có cần nhịn ăn trước xét nghiệm không?" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<AiChatResponseDto> Send(HttpClient client, AiChatRequestDto request)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/ai/chat", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }
}
