using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class CardiologyBookingAliasTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("tôi muốn đặt lịch khám tim")]
    [InlineData("dat lich kham tim")]
    [InlineData("khám tim")]
    [InlineData("khoa tim")]
    [InlineData("bác sĩ tim")]
    [InlineData("bệnh tim")]
    [InlineData("kham tim")]
    [InlineData("bac si tim")]
    public async Task Cardiology_alias_prefills_the_wizard_specialty(string message)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var response = await patient.PostAsJsonAsync("/api/v1/ai/booking-wizard", new
        {
            sessionId = $"cardiology_{Guid.NewGuid():N}", step = "start", hint = message
        });
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AiBookingWizardResponseDto>>())!.Data!;
        Assert.Equal("doctor", result.Step);
        Assert.Equal("Tim mạch", result.Summary?.SpecialtyName);
        Assert.True(SymptomSpecialtyRules.HasNamedSpecialty(message));
        Assert.False(result.ProviderWasCalled);
    }

    [Theory]
    [InlineData("tìm bác sĩ giỏi")]
    [InlineData("tim")]
    [InlineData("tìm")]
    public async Task Standalone_tim_and_find_doctor_do_not_select_cardiology(string message)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var response = await patient.PostAsJsonAsync("/api/v1/ai/booking-wizard", new
        {
            sessionId = $"not_cardiology_{Guid.NewGuid():N}", step = "start", hint = message
        });
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AiBookingWizardResponseDto>>())!.Data!;
        Assert.Equal("specialty", result.Step);
        Assert.Null(result.Summary?.SpecialtyName);
        Assert.False(SymptomSpecialtyRules.HasNamedSpecialty(message));
    }

    [Theory]
    [InlineData("đau tim")]
    [InlineData("đau ngực khó thở")]
    public async Task Emergency_symptoms_keep_the_115_chat_route(string message)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var response = await patient.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto { Message = message });
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
        Assert.Equal("SafetyEmergency", result.DialogueOutcome);
        Assert.Contains("115", result.Message);
        Assert.Empty(result.Sources);
        Assert.False(result.ProviderWasCalled);
    }
}
