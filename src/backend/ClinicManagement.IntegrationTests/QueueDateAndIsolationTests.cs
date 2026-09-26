using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class QueueDateAndIsolationTests : IntegrationTestBase
{
    public QueueDateAndIsolationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_DoctorQueue_When_QueriedBySpecificDate_Then_OnlyReturnsAppointmentsForThatDate()
    {
        // 1. Create 2 appointments for Doctor 1 on two distinct dates: DateToday and DateTomorrow
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var tomorrow = today.AddDays(1);

        var slotToday = await CreateAvailableSlotAsync(DoctorEntityId, today, new TimeOnly(8, 0), new TimeOnly(8, 30));
        var slotTomorrow = await CreateAvailableSlotAsync(DoctorEntityId, tomorrow, new TimeOnly(8, 0), new TimeOnly(8, 30));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = $"APPT-DATE-1-{Guid.NewGuid():N}"[..18],
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = slotToday.Id,
                AppointmentDate = today,
                StartTime = slotToday.StartTime,
                EndTime = slotToday.EndTime,
                Reason = "Khám hôm nay",
                Status = AppointmentStatus.CheckedIn
            });

            db.Appointments.Add(new Appointment
            {
                AppointmentCode = $"APPT-DATE-2-{Guid.NewGuid():N}"[..18],
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = slotTomorrow.Id,
                AppointmentDate = tomorrow,
                StartTime = slotTomorrow.StartTime,
                EndTime = slotTomorrow.EndTime,
                Reason = "Khám ngày mai",
                Status = AppointmentStatus.CheckedIn
            });

            await db.SaveChangesAsync();
        }

        // 2. Doctor 1 queries queue specifically for 'today'
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync($"/api/v1/doctor/appointments/queue?date={today:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("data");

        // Verify all returned items match the requested date
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("appointmentDate", out var apptDateProp))
            {
                var itemDateStr = apptDateProp.GetString();
                Assert.Equal(today.ToString("yyyy-MM-dd"), itemDateStr);
            }
        }
    }

    [Fact]
    public async Task Given_TwoDoctors_When_Doctor2QueriesQueue_Then_CannotSeeDoctor1QueueItems()
    {
        // 1. Create appointment assigned to Doctor 1
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(12));
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(9, 0), new TimeOnly(9, 30));

        string doc1ApptCode = $"DOC1-PRIV-{Guid.NewGuid():N}"[..18];
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = doc1ApptCode,
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = slot.Id,
                AppointmentDate = date,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Reason = "Bệnh nhân của bác sĩ 1",
                Status = AppointmentStatus.CheckedIn
            });
            await db.SaveChangesAsync();
        }

        // 2. Doctor 2 queries their queue for the same date
        await AuthenticateAsync("doc2@test.com");
        var response = await Client.GetAsync($"/api/v1/doctor/appointments/queue?date={date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("data");

        // Verify Doctor 1's appointment code is NOT in Doctor 2's queue
        foreach (var item in items.EnumerateArray())
        {
            var code = item.GetProperty("appointmentCode").GetString();
            Assert.NotEqual(doc1ApptCode, code);
        }
    }
}
