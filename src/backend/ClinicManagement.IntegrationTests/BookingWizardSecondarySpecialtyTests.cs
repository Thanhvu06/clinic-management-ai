using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class BookingWizardSecondarySpecialtyTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Doctor_hint_uses_active_secondary_specialty_when_primary_is_not_available_to_wizard(bool inactivePrimary)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var primary = await db.Specialties.SingleAsync(x => x.Id == SpecialtyEntityId);
        var secondary = await db.Specialties.SingleAsync(x => x.Id == CardiologySpecialtyId);
        var originalAiEnabled = primary.AiEnabled;
        var originalActive = primary.IsActive;
        var name = await db.Users.Where(x => x.Id == DoctorId).Select(x => x.FullName).SingleAsync();
        primary.AiEnabled = false;
        primary.IsActive = !inactivePrimary;
        await db.SaveChangesAsync();
        try
        {
            using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
            var result = await StepAsync(patient, new { sessionId = $"secondary_hint_{Guid.NewGuid():N}", step = "start", hint = name });
            Assert.Equal("day", result.Step);
            Assert.Equal(name, result.Summary?.DoctorName);
            Assert.Equal(secondary.Name, result.Summary?.SpecialtyName);
        }
        finally
        {
            primary.AiEnabled = originalAiEnabled;
            primary.IsActive = originalActive;
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cardiology_lists_secondary_doctor_finds_slots_and_books_selected_specialty(bool anyDoctor)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await db.Users.SingleAsync(x => x.Id == DoctorId);
        var oldName = doctor.FullName;
        doctor.FullName = "Nguyễn Minh Khải";
        var specialty = await db.Specialties.SingleAsync(x => x.Id == CardiologySpecialtyId);
        var otherName = await db.Users.Where(x => x.Id == Doctor2UserId).Select(x => x.FullName).SingleAsync();
        var facilityId = await db.StaffFacilityAssignments.Where(x => x.UserId == DoctorId && x.IsActive && x.Role == "Doctor")
            .Select(x => x.FacilityId).FirstAsync();
        var department = new Department
        {
            FacilityId = facilityId, SpecialtyId = specialty.Id, Code = $"secondary-{Guid.NewGuid():N}",
            Name = "Tim mạch kiểm thử", IsActive = true
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        try
        {
            Assert.True(await db.DoctorSpecialties.AnyAsync(x => x.DoctorId == DoctorEntityId && x.SpecialtyId == specialty.Id && !x.IsPrimary));
            Assert.False(await db.DoctorSpecialties.AnyAsync(x => x.DoctorId == Doctor2EntityId && x.SpecialtyId == specialty.Id));
            var startTime = new TimeOnly(11, anyDoctor ? 17 : 7);
            var slot = await CreateAvailableSlotAsync(DoctorEntityId, GetFutureWorkingDate(anyDoctor ? 4 : 3), startTime, startTime.AddMinutes(30));
            using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
            var sessionId = $"secondary_{Guid.NewGuid():N}";
            var start = await StepAsync(patient, new { sessionId, step = "start" });
            var choices = await StepAsync(patient, new { sessionId, step = "pick", optionToken = start.Options.Single(x => x.Label == specialty.Name).Token });
            Assert.Contains(choices.Options, x => x.Label == "Nguyễn Minh Khải");
            Assert.DoesNotContain(choices.Options, x => x.Label == otherName);
            var option = choices.Options.Single(x => anyDoctor ? x.Label.StartsWith("Bác sĩ nào cũng được") : x.Label == "Nguyễn Minh Khải");
            var days = await StepAsync(patient, new { sessionId, step = "pick", optionToken = option.Token });
            Assert.Equal("day", days.Step);
            Assert.Contains(days.Options, x => x.Label == slot.SlotDate.ToString("dd/MM/yyyy"));
            var times = await StepAsync(patient, new { sessionId, step = "pick", optionToken = days.Options.Single(x => x.Label == slot.SlotDate.ToString("dd/MM/yyyy")).Token });
            var reason = await StepAsync(patient, new { sessionId, step = "pick", optionToken = times.Options.Single(x => x.Label == $"{slot.StartTime:HH:mm} – {slot.EndTime:HH:mm}").Token });
            if (reason.Step == "facility") reason = await StepAsync(patient, new { sessionId, step = "pick", optionToken = reason.Options.First().Token });
            Assert.Equal("reason", reason.Step);
            Assert.Equal("Nguyễn Minh Khải", reason.Summary?.DoctorName);
            var review = await StepAsync(patient, new { sessionId, step = "reason", optionToken = reason.ReasonToken, reason = "Khám tim mạch định kỳ" });
            var payload = Assert.IsType<AiActionPayloadDto>(review.ReviewAction?.Payload);
            Assert.Equal(specialty.Id, payload.SpecialtyId);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/appointments")
            {
                Content = JsonContent.Create(new
                {
                    doctorId = payload.DoctorId, specialtyId = payload.SpecialtyId, appointmentSlotId = payload.SlotId,
                    reason = payload.Reason, facilityId = payload.FacilityId, confirmationId = payload.ConfirmationId,
                    contextSnapshotId = payload.ContextSnapshotId, sessionId, draftId = payload.DraftId, draftVersion = payload.DraftVersion
                })
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var booked = await patient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
            var appointment = await db.Appointments.SingleAsync(x => x.AppointmentSlotId == slot.Id);
            Assert.Equal(specialty.Id, appointment.SpecialtyId);
            Assert.Equal(DoctorEntityId, appointment.DoctorId);
        }
        finally
        {
            doctor.FullName = oldName;
            db.Departments.Remove(department);
            await db.SaveChangesAsync();
        }
    }

    private static async Task<AiBookingWizardResponseDto> StepAsync(HttpClient client, object request)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/ai/booking-wizard", request);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AiBookingWizardResponseDto>>();
        return Assert.IsType<AiBookingWizardResponseDto>(envelope?.Data);
    }
}
