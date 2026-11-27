using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class AiPatientCancelAppointmentCodeTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Chat_matches_the_generated_appointment_code_with_a_numeric_date_segment()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
        var firstSlot = await CreateAvailableSlotAsync(DoctorEntityId, today.AddDays(2), new TimeOnly(9, 0), new TimeOnly(9, 30));
        var otherSlot = await CreateAvailableSlotAsync(DoctorEntityId, today.AddDays(3), new TimeOnly(9, 0), new TimeOnly(9, 30));
        var selected = CreateAppointment(today, firstSlot);
        db.Appointments.AddRange(selected, CreateAppointment(today, otherSlot));
        await db.SaveChangesAsync();
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var response = await patient.PostAsJsonAsync("/api/v1/ai/chat", new
        {
            message = $"huy lich hen ma {selected.AppointmentCode.ToLowerInvariant()}",
            sessionId = $"sess_{Guid.NewGuid():N}",
            hasOpenBookingWizard = true
        });

        response.EnsureSuccessStatusCode();
        var chat = (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
        Assert.Equal("PatientReadResolved", chat.DialogueOutcome);
        Assert.Equal("NotCalled", chat.ProviderStatus);
        var action = Assert.Single(chat.Actions, item => item.Type == AiActionTypes.RequestCancellation);
        Assert.Equal(selected.Id, action.Payload.AppointmentId);
        Assert.Equal(selected.AppointmentCode, action.Payload.AppointmentCode);
    }

    private Appointment CreateAppointment(DateOnly today, AppointmentSlot slot) => new()
    {
        AppointmentCode = BusinessDisplay.RandomCode("APT", today, 8),
        PatientId = Patient1EntityId,
        DoctorId = DoctorEntityId,
        SpecialtyId = SpecialtyEntityId,
        AppointmentSlotId = slot.Id,
        AppointmentDate = slot.SlotDate,
        StartTime = slot.StartTime,
        EndTime = slot.EndTime,
        Status = AppointmentStatus.Pending,
        Reason = "Kiểm thử mã lịch hẹn"
    };
}
