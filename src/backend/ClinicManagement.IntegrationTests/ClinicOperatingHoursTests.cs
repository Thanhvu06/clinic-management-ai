using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.IntegrationTests;

public class ClinicOperatingHoursTests
{
    [Theory]
    [InlineData("Standard", 1, false)]
    [InlineData("Standard", 4, true)]
    [InlineData("Standard", 7, true)]
    [InlineData("Standard", 2, true)]
    [InlineData("TwentyFourSeven", 1, true)]
    [InlineData("TwentyFourSeven", 4, true)]
    public void Policy_filters_days_and_preserves_seed_shifts(string mode, int day, bool expected)
    {
        var policy = new ClinicOperatingHoursPolicy(Options.Create(new ClinicOperatingHoursOptions { Mode = mode }));
        Assert.Equal(expected, policy.IsOpen(new DateOnly(2026, 11, day)));
        Assert.Equal(mode == "TwentyFourSeven" ? 3 : day == 1 ? 0 : day == 7 ? 1 : 2,
            policy.GetSeedShifts(new DateOnly(2026, 11, day)).Count);
    }

    [Theory]
    [InlineData("2026-11-01T14:00:00Z", "TwentyFourSeven")]
    [InlineData("2026-11-03T20:00:00Z", "TwentyFourSeven")]
    [InlineData("2026-11-02T02:00:00Z", "TwentyFourSeven")]
    [InlineData("2026-11-02T02:00:00Z", "Standard")]
    public async Task Seed_obeys_clock_mode_expiry_and_checkin(string utc, string mode)
    {
        using var parent = new FixedSeedWebApplicationFactory(DateTime.Parse(utc).ToUniversalTime());
        var user = new OperatingHoursTestUser();
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<ClinicOperatingHoursOptions>(options => options.Mode = mode);
            services.AddSingleton<ICurrentUserService>(user);
        }));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var policy = services.GetRequiredService<IClinicOperatingHoursPolicy>();
        var earliest = clock.VietnamNow.AddMinutes(30);
        var confirmed = (await db.Appointments.Include(a => a.Histories).Include(a => a.Patient).ToListAsync())
            .Where(a => a.Status == AppointmentStatus.Confirmed && a.AppointmentDate.ToDateTime(a.StartTime) >= earliest &&
                a.AppointmentDate.ToDateTime(a.StartTime) < earliest.AddHours(24)).ToList();
        if (mode == "TwentyFourSeven")
        {
            Assert.True(confirmed.Count >= 24);
            Assert.All(Enumerable.Range(0, 24), hour => Assert.Contains(confirmed,
                a => (int)(a.AppointmentDate.ToDateTime(a.StartTime) - earliest).TotalHours == hour));
            var demoDoctorIds = await db.Doctors.Join(db.Users, d => d.UserId, u => u.Id, (d, u) => new { d.Id, u.Email })
                .Where(x => x.Email == "doctor@cliniccare.local" || x.Email == "bacsi.02@cliniccare.local" || x.Email == "bacsi.03@cliniccare.local")
                .Select(x => x.Id).ToListAsync();
            Assert.Equal(3, confirmed.Select(a => a.DoctorId).Intersect(demoDoctorIds).Distinct().Count());
            Assert.Contains(confirmed, a => a.Patient.Email == "patient@cliniccare.local");
            Assert.All(confirmed, a =>
            {
                Assert.NotNull(a.FacilityId);
                Assert.Contains(a.Histories, h => h.NewStatus == AppointmentStatus.Pending);
                Assert.Contains(a.Histories, h => h.OldStatus == AppointmentStatus.Pending && h.NewStatus == AppointmentStatus.Confirmed);
            });
            var mainDoctor = await db.Doctors.Join(db.Users, d => d.UserId, u => u.Id, (d, u) => new { d.Id, u.Email })
                .SingleAsync(x => x.Email == "doctor@cliniccare.local");
            Assert.True(await db.PatientVisits.AnyAsync(v => v.AssignedDoctorId == mainDoctor.Id && v.VisitDate == clock.VietnamToday && v.Status == VisitStatus.WaitingForDoctor));
            var schedules = await db.DoctorWorkSchedules.Where(s => s.DoctorId == mainDoctor.Id && s.WorkDate == clock.VietnamToday).OrderBy(s => s.StartTime).ToListAsync();
            Assert.Equal(new[] { new TimeOnly(0, 0), new(8, 0), new(16, 0) }, schedules.Select(s => s.StartTime));
            Assert.Equal(new[] { new TimeOnly(8, 0), new(16, 0), new(23, 30) }, schedules.Select(s => s.EndTime));
            Assert.Equal(47, await db.AppointmentSlots.CountAsync(s => s.DoctorId == mainDoctor.Id && s.SlotDate == clock.VietnamToday));
        }
        else
        {
            var todaySchedules = await db.DoctorWorkSchedules.Where(s => s.WorkDate == clock.VietnamToday).ToListAsync();
            Assert.Equal(20, todaySchedules.Count);
            Assert.All(todaySchedules, s => Assert.Contains((s.StartTime, s.EndTime), policy.GetSeedShifts(clock.VietnamToday)));
            Assert.False(await db.DoctorWorkSchedules.AnyAsync(s => s.WorkDate == new DateOnly(2026, 11, 8)));
        }
        Assert.All((await db.Appointments.ToListAsync()).GroupBy(a => (a.PatientId, a.AppointmentDate)), group => Assert.Single(group));
        var ids = confirmed.Select(a => a.Id).ToList();
        await services.GetRequiredService<ExpiryWorker>().RunOnceAsync(throwOnFailure: true);
        db.ChangeTracker.Clear();
        Assert.False(await db.Appointments.AnyAsync(a => ids.Contains(a.Id) && a.Status == AppointmentStatus.NoShow));
        user.UserId = (await db.Users.SingleAsync(u => u.Email == "reception@cliniccare.local")).Id;
        var appointment = confirmed.First(a => a.AppointmentDate == clock.VietnamToday);
        var ticket = await services.GetRequiredService<IPatientVisitService>().CheckInAppointmentAsync(new() { AppointmentId = appointment.Id });
        Assert.Equal("WaitingForDoctor", ticket.Status);
        var location = await db.ClinicLocations.SingleAsync(l => l.Code == "CS01");
        Assert.Equal(policy.DisplayText, location.OpeningHours);
        using var context = JsonDocument.Parse(await services.GetRequiredService<IClinicAiContextService>().GetClinicContextJsonAsync());
        Assert.Equal(policy.DisplayText, context.RootElement.GetProperty("ThongTinCoSo").GetProperty("GioLamViec").GetString());
    }
}

internal sealed class OperatingHoursTestUser : ICurrentUserService
{
    public Guid? UserId { get; set; }
}
