using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class DemoBlockersSeedTests
{
    [Theory]
    [InlineData("\"Normal\"")]
    [InlineData("1")]
    public void IntakePriority_AcceptsStringAndNumber(string priority)
    {
        var request = JsonSerializer.Deserialize<ReceptionIntakeRequest>($"{{\"Priority\":{priority}}}");
        Assert.Equal(VisitPriority.Normal, request!.Priority);
    }

    [Theory]
    [InlineData(27, 3, 0)]
    [InlineData(27, 7, 0)]
    [InlineData(27, 9, 30)]
    [InlineData(31, 4, 0)]
    public async Task Seed_BindsFacilities_AndKeepsFutureCheckInAppointments(int day, int hour, int minute)
    {
        using var factory = new FixedSeedWebApplicationFactory(new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 7, true);
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var appointments = await db.Appointments.Include(a => a.Doctor).ToListAsync();
        var assignments = await db.StaffFacilityAssignments.Where(a => a.IsActive && a.Role == "Doctor").ToListAsync();
        Assert.All(appointments, appointment =>
        {
            Assert.NotNull(appointment.FacilityId);
            Assert.Contains(assignments, a => a.UserId == appointment.Doctor.UserId && a.FacilityId == appointment.FacilityId);
        });
        Assert.All(appointments.GroupBy(a => (a.PatientId, a.AppointmentDate)), group => Assert.Single(group));
        var confirmed = appointments.Where(a => a.Status == AppointmentStatus.Confirmed).ToList();
        Assert.True(confirmed.Count >= 3);
        Assert.All(confirmed, appointment => Assert.True(appointment.AppointmentDate.ToDateTime(appointment.StartTime) >= clock.VietnamNow.AddMinutes(30)));
        Assert.DoesNotContain(confirmed, appointment => DemoSeedCalendar.IsClosed(appointment.AppointmentDate));
        var checkInDay = confirmed.Min(a => a.AppointmentDate);
        Assert.True(confirmed.Count(a => a.AppointmentDate == checkInDay) >= 3);
        if (day == 31) Assert.Equal(new DateOnly(2026, 11, 2), checkInDay);
        var visits = await db.PatientVisits.ToListAsync();
        Assert.DoesNotContain(visits, v => v.VisitDate < clock.VietnamToday && v.Status == VisitStatus.InConsultation);
        var invalidVisitIds = visits.Where(v => v.Status is VisitStatus.NoShow or VisitStatus.Cancelled).Select(v => v.Id).ToList();
        Assert.False(await db.Invoices.AnyAsync(i => i.PatientVisitId.HasValue && invalidVisitIds.Contains(i.PatientVisitId.Value) && i.Status == InvoiceStatus.Unpaid));
        var orders = await db.DiagnosticOrders.Include(o => o.PatientVisit).Where(o => o.PatientVisitId != null).ToListAsync();
        Assert.All(orders, order => Assert.Equal(order.PatientVisit!.VisitDate, DateOnly.FromDateTime(clock.ConvertUtcToVietnam(order.OrderedAtUtc))));
    }
}
