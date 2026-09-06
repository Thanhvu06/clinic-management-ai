using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AppointmentConcurrencyTests : IntegrationTestBase
{
    public AppointmentConcurrencyTests(CustomWebApplicationFactory factory) : base(factory) { }

    private static DateOnly GetFutureWorkingDate(int daysFromNow)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).AddDays(daysFromNow));
        while (date.DayOfWeek == DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    [Fact]
    public async Task Given_SameSlot_When_BookedConcurrentlyOrSequentially_Then_OnlyFirstSucceeds()
    {
        // Setup a fresh slot in database for this specific test with valid doctor schedule
        var date = GetFutureWorkingDate(3);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(14, 0, 0), new TimeOnly(14, 30, 0));
        var testSlotId = slot.Id;

        // Booking request 1 by Patient 1
        await AuthenticateAsync("pat1@test.com");
        var request1 = new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = testSlotId,
            Reason = "Khám sức khỏe tổng quát - Patient 1"
        };
        var response1 = await Client.PostAsJsonAsync("/api/v1/appointments", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Booking request 2 by Patient 2 on the SAME slot
        await AuthenticateAsync("pat2@test.com");
        var request2 = new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = testSlotId,
            Reason = "Khám đau đầu - Patient 2"
        };
        var response2 = await Client.PostAsJsonAsync("/api/v1/appointments", request2);

        // A slot race is a resource conflict, not a generic validation error.
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);

        var errorBody = await response2.Content.ReadAsStringAsync();
        Assert.Contains("SLOT_ALREADY_BOOKED", errorBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Given_CheckedInAppointment_When_SamePatientBooksOverlappingSlot_Then_CanonicalPolicyBlocksIt()
    {
        var date = GetFutureWorkingDate(9);
        var existingSlot = await CreateAvailableSlotAsync(
            DoctorEntityId,
            date,
            new TimeOnly(16, 0),
            new TimeOnly(16, 30));
        var targetSlot = await CreateAvailableSlotAsync(
            Doctor2EntityId,
            date,
            new TimeOnly(16, 0),
            new TimeOnly(16, 30));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            existingSlot.IsBooked = true;
            db.AppointmentSlots.Update(existingSlot);
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = $"APT-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = existingSlot.Id,
                AppointmentDate = date,
                StartTime = existingSlot.StartTime,
                EndTime = existingSlot.EndTime,
                Reason = "Đang chờ bác sĩ tiếp nhận và thăm khám",
                Status = AppointmentStatus.CheckedIn
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = Doctor2EntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = targetSlot.Id,
            Reason = "Đặt lịch khác trùng với ca đang tiếp nhận"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "PATIENT_TIME_CONFLICT",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var targetSlotAfterFailure = await verifyDb.AppointmentSlots
            .AsNoTracking()
            .FirstAsync(slot => slot.Id == targetSlot.Id);
        Assert.False(targetSlotAfterFailure.IsBooked);
    }
}
