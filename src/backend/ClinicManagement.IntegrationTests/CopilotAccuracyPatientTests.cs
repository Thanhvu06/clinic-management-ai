using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public class CopilotAccuracyPatientTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("tôi uống paracetamol 10 viên một lúc có sao không")]
    [InlineData("tôi uống quá liều")]
    [InlineData("uống cả vỉ")]
    [InlineData("uống nhầm thuốc")]
    [InlineData("vừa uống aspirin 8 viên cùng lúc")]
    [InlineData("toi uong 12 vien mot luc")]
    public async Task MedicationMisuse_IsEmergencyBeforeProviderOrSpecialtySuggestions(string message)
    {
        await AuthenticateAsync("pat1@test.com");
        Factory.MockAiProvider.Invocations.Clear();
        var response = await ChatAsync(message);
        Assert.Equal("EMERGENCY", response.Urgency);
        Assert.Equal("SafetyEmergency", response.DialogueOutcome);
        Assert.Equal("NotCalled", response.ProviderStatus);
        Assert.Contains("115", response.Message);
        Assert.Empty(response.SpecialtySuggestions);
        Assert.Equal(AiActionTypes.CallEmergency, Assert.Single(response.Actions).Type);
        Factory.MockAiProvider.Verify(provider => provider.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("không uống quá liều")]
    [InlineData("tôi không uống quá liều thuốc")]
    [InlineData("không uống nhầm thuốc")]
    [InlineData("chưa uống cả vỉ")]
    [InlineData("tôi không uống paracetamol 10 viên một lúc")]
    public void NegatedMedicationMisuse_DoesNotRaiseEmergency(string message) => Assert.False(new AiSafetyGuard().Inspect(message).IsEmergency);

    [Theory]
    [InlineData("Nội tổng quát", "SP01")]
    [InlineData("Nhi", "SP02")]
    [InlineData("Sản phụ khoa", "SP03")]
    [InlineData("Da liễu", "SP04")]
    [InlineData("Tai mũi họng", "SP05")]
    [InlineData("Tim mạch", "SP06")]
    [InlineData("Cơ xương khớp", "SP07")]
    [InlineData("Thần kinh", "SP08")]
    [InlineData("Nội tiết", "SP09")]
    [InlineData("Nhãn khoa", "SP10")]
    [InlineData("Mắt", "SP10")]
    [InlineData("Tiêu hóa", "SP11")]
    [InlineData("da lieu", "SP04")]
    [InlineData("tai mui hong", "SP05")]
    [InlineData("co xuong khop", "SP07")]
    public async Task NamedSpecialty_ResolvesWithoutProvider_AndLinksToThatSpecialty(string name, string code)
    {
        await AuthenticateAsync("pat1@test.com");
        var question = $"tôi muốn đặt lịch khám {name} ngày mai";
        foreach (var input in new[] { question, AiTextNormalizer.NormalizeForComparison(question) })
        {
            var response = await ChatAsync(input);
            var suggestion = Assert.Single(response.SpecialtySuggestions);
            Assert.Equal(code, suggestion.SpecialtyCode);
            var action = Assert.Single(response.Actions, a => a.Type == AiActionTypes.StartBooking);
            Assert.Equal(suggestion.SpecialtyId, action.Payload.SpecialtyId);
            Assert.Equal($"/patient/book?specialtyId={suggestion.SpecialtyId}", action.Payload.TargetUrl);
            Assert.Equal("NotCalled", response.ProviderStatus);
        }
    }

    [Theory]
    [InlineData("cho tôi xem hóa đơn của tôi", "patient.get_my_bills", AiActionTypes.ViewBills, "/patient/invoices")]
    [InlineData("đơn thuốc của tôi", "patient.get_my_prescriptions", AiActionTypes.ViewPrescriptions, "/patient/prescriptions")]
    [InlineData("lịch hẹn của tôi", "patient.get_my_appointments", AiActionTypes.ViewMyAppointments, "/patient/appointments")]
    [InlineData("cho mình xem kết quả xét nghiệm", "patient.get_my_diagnostic_results", AiActionTypes.ViewDiagnosticResults, "/patient/diagnostic-results")]
    public async Task OwnData_UsesPatientReadToolAndExistingNavigation(string message, string tool, string action, string route)
    {
        await AuthenticateAsync("pat1@test.com");
        var response = await ChatAsync(message);
        Assert.NotEqual(AiChatIntentTypes.UnclearOrOutOfScope, response.PrimaryIntent);
        Assert.Equal(tool, Assert.Single(response.ToolResults).ToolName);
        Assert.Equal("completed", response.ToolResults[0].Status);
        Assert.Contains(response.Actions, a => a.Type == action && a.Payload.TargetUrl == route && a.RequiresAuthentication && !a.RequiresConfirmation);
        Assert.Equal("NotCalled", response.ProviderStatus);
    }

    private async Task<AiChatResponseDto> ChatAsync(string message)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto { Message = message });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }
}
