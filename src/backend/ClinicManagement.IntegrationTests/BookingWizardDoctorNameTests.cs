using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class BookingWizardDoctorNameTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("bác sĩ khải á")]
    [InlineData("khải")]
    [InlineData("bác sĩ Nguyễn Minh Khải ơi giúp mình")]
    public async Task Specialty_text_accepts_unique_doctor_name(string text)
    {
        await SetDoctorNamesAsync("Trần Văn An");
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var sessionId = $"wizdoc_{Guid.NewGuid():N}";
        var start = await StepAsync(patient, new { sessionId, step = "start" });
        var day = await StepAsync(patient, new { sessionId, step = "text", optionToken = start.TextToken, text });

        Assert.Equal("specialty", start.Step);
        Assert.Equal("day", day.Step);
        Assert.Equal("Nguyễn Minh Khải", day.Summary?.DoctorName);
    }

    [Theory]
    [InlineData("bác sĩ khải á", "Trần Văn Khải")]
    [InlineData("alo", "Trần Văn An")]
    [InlineData("chào", "Trần Văn An")]
    [InlineData("xin gặp khải", "Trần Văn An")]
    public async Task Specialty_text_keeps_choices_for_ambiguous_or_unmatched_input(string text, string secondDoctor)
    {
        await SetDoctorNamesAsync(secondDoctor);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var sessionId = $"wizdoc_{Guid.NewGuid():N}";
        var start = await StepAsync(patient, new { sessionId, step = "start" });
        var result = await StepAsync(patient, new { sessionId, step = "text", optionToken = start.TextToken, text });

        Assert.Equal("specialty", result.Step);
        Assert.Contains("chưa xác định được lựa chọn", result.Message);
        Assert.Equal(start.Options.Select(option => option.Label), result.Options.Select(option => option.Label));
    }

    [Fact]
    public async Task Start_hint_accepts_doctor_name_with_trailing_filler()
    {
        await SetDoctorNamesAsync("Trần Văn An");
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var sessionId = $"wizdoc_{Guid.NewGuid():N}";
        var day = await StepAsync(patient, new { sessionId, step = "start", hint = "bác sĩ khải á" });

        Assert.Equal("day", day.Step);
        Assert.Equal("Nguyễn Minh Khải", day.Summary?.DoctorName);
    }

    private async Task SetDoctorNamesAsync(string secondDoctor)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await db.Users.SingleAsync(user => user.Id == DoctorId);
        var second = await db.Users.SingleAsync(user => user.Id == Doctor2UserId);
        first.FullName = "Nguyễn Minh Khải";
        second.FullName = secondDoctor;
        await db.SaveChangesAsync();
    }

    private static async Task<AiBookingWizardResponseDto> StepAsync(HttpClient patient, object request)
    {
        using var response = await patient.PostAsJsonAsync("/api/v1/ai/booking-wizard", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AiBookingWizardResponseDto>>();
        return Assert.IsType<AiBookingWizardResponseDto>(result?.Data);
    }
}
