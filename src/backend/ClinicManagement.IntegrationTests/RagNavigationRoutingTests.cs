using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class RagNavigationRoutingTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("xem kết quả xét nghiệm ở mục nào trên web", "Kết quả cận lâm sàng")]
    [InlineData("xem đơn thuốc ở trang nào", "Đơn thuốc của tôi")]
    [InlineData("hóa đơn xem ở đâu trên website", "Hóa đơn của tôi")]
    public async Task Interface_navigation_uses_RAG_with_real_document_sources(string message, string menuLabel)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");

        var response = await Send(patient, message);

        Assert.Equal("KnowledgeExtractive", response.DialogueOutcome);
        Assert.NotEmpty(response.Sources);
        Assert.Contains(menuLabel, response.Message);
        Assert.False(response.ProviderWasCalled);
        Assert.DoesNotContain("Có 0 chỉ định", response.Message);
    }

    [Theory]
    [InlineData("xem kết quả xét nghiệm của tôi")]
    [InlineData("đơn thuốc của tôi")]
    [InlineData("xem kết quả xét nghiệm của tôi trên web")]
    [InlineData("đơn thuốc của tôi ở trang nào")]
    public async Task Personal_reads_keep_the_existing_data_handler(string message)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");

        var response = await Send(patient, message);

        Assert.Equal("PatientReadResolved", response.DialogueOutcome);
        Assert.Empty(response.Sources);
        Assert.False(response.ProviderWasCalled);
    }

    [Theory]
    [InlineData("xem kết quả ở mục nào")]
    [InlineData("mục nào xem kết quả")]
    [InlineData("xem kết quả ở đâu trên web")]
    [InlineData("xem kết quả trên web")]
    [InlineData("xem kết quả trên website")]
    [InlineData("menu xem kết quả")]
    [InlineData("xem kết quả ở trang nào")]
    [InlineData("xem kết quả bấm vào đâu")]
    [InlineData("vào đâu để xem kết quả")]
    public void Navigation_phrases_pass_early_guidance_and_candidate_gates(string message)
    {
        var classification = new IntentClassificationResult { Intent = AiChatIntentTypes.DiagnosticLookup, IsClear = true };

        Assert.True(KnowledgeQuestionPolicy.IsEarlyGuidance(message, classification));
        Assert.True(KnowledgeQuestionPolicy.IsCandidate(message, classification.Intent, false, classification));
    }

    [Theory]
    [InlineData("xem kết quả của tôi trên web")]
    [InlineData("xem kết quả của chính tôi trên website")]
    [InlineData("xem kết quả của em ở mục nào")]
    [InlineData("xem kết quả của con ở mục nào")]
    [InlineData("xem kết quả của mình ở trang nào")]
    [InlineData("xem kết quả của anh qua menu")]
    [InlineData("xem kết quả của chị trên website")]
    [InlineData("xem kết quả của bạn trên web")]
    [InlineData("xem kết quả mã APT-001 trên web")]
    [InlineData("xem kết quả số 123 trên website")]
    [InlineData("xem kết quả chuyên khoa tim mạch ở mục nào")]
    public void Ownership_identifiers_and_named_specialty_keep_early_RAG_disabled(string message) =>
        Assert.False(KnowledgeQuestionPolicy.IsEarlyGuidance(message, new IntentClassificationResult()));

    [Fact]
    public void Extracted_selections_keep_interface_navigation_out_of_early_RAG()
    {
        IntentClassificationResult[] selections =
        [
            new() { ExtractedDoctorName = "Bác sĩ đã chọn" },
            new() { ExtractedDate = "2026-11-09" },
            new() { ExtractedTimePreference = "sáng" },
            new() { ExtractedRelativeDoctorIndex = 1 },
            new() { ExtractedRelativeSlotIndex = 1 }
        ];

        Assert.All(selections, classification =>
            Assert.False(KnowledgeQuestionPolicy.IsEarlyGuidance("xem kết quả ở mục nào trên web", classification)));
    }

    private static async Task<AiChatResponseDto> Send(HttpClient client, string message)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto { Message = message });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }
}
