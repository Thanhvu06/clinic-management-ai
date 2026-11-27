using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ClinicManagement.IntegrationTests;

public sealed class DevelopmentSlotCoverageTests
{
    [Fact]
    public async Task Coverage_only_extends_bookable_slots_without_running_other_seeds_or_changing_qa_records()
    {
        using var initial = new FixedSeedWebApplicationFactory(new DateTime(2026, 11, 27, 3, 0, 0, DateTimeKind.Utc));
        using var initialScope = initial.Services.CreateScope();
        var initialServices = initialScope.ServiceProvider;
        await DevelopmentDataSeeder.SeedMasterDataAsync(initialServices);
        var initialDb = initialServices.GetRequiredService<AppDbContext>();
        var clock = initialServices.GetRequiredService<IDateTimeProvider>();
        var today = clock.VietnamToday;
        await AddExistingActivityAsync(initialDb, today);
        var before = await ReadQaRecordsAsync(initialDb);

        using var api = new CoverageApiFactory("Development", true, initialDb.Database.GetConnectionString(), clock);
        using var scope = api.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var hosted = api.Services.GetServices<IHostedService>().ToList();
        var worker = Assert.Single(hosted.OfType<DevelopmentSlotCoverageWorker>());
        Assert.DoesNotContain(hosted, candidate => candidate is DevelopmentDemoTopUpWorker or ExpiryWorker);

        var availability = services.GetRequiredService<IAppointmentAvailabilityPolicy>();
        var horizon = today.AddDays(13);
        var specialties = await db.Specialties.Where(s => s.IsActive).ToListAsync();
        Assert.Equal(11, specialties.Count);
        foreach (var specialty in specialties)
        {
            var slots = await availability.GetAvailableSlotsAsync(new BatchSlotAvailabilityRequest
            {
                SpecialtyId = specialty.Id, FromDate = horizon, ToDate = horizon
            });
            var bookable = false;
            foreach (var doctorId in slots.Select(slot => slot.DoctorId).Distinct())
                bookable |= (await AppointmentFacilityResolver.GetEligibleAsync(db, doctorId, specialty.Id)).Count > 0;
            Assert.True(bookable, $"{specialty.Name} has no bookable slot on {horizon:yyyy-MM-dd}");
        }

        var afterStartup = (await db.AppointmentSlots.CountAsync(), await db.DoctorWorkSchedules.CountAsync());
        Assert.True(afterStartup.Item1 > 1);
        Assert.True(afterStartup.Item2 > 0);
        Assert.Equivalent(before, await ReadQaRecordsAsync(db), strict: true);
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(afterStartup, (await db.AppointmentSlots.CountAsync(), await db.DoctorWorkSchedules.CountAsync()));
        Assert.Equivalent(before, await ReadQaRecordsAsync(db), strict: true);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", true)]
    public async Task No_demo_seed_runs_when_both_flags_are_off_or_outside_development(string environment, bool coverageEnabled)
    {
        using var api = new CoverageApiFactory(environment, coverageEnabled);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(api.Services.GetServices<IHostedService>(),
            candidate => candidate is DevelopmentSlotCoverageWorker or DevelopmentDemoTopUpWorker);
        Assert.Empty(await db.Specialties.ToListAsync());
        Assert.Empty(await db.DoctorWorkSchedules.ToListAsync());
        Assert.Empty(await db.AppointmentSlots.ToListAsync());
        Assert.Empty(await db.Appointments.ToListAsync());
        Assert.Empty(await db.PatientVisits.ToListAsync());
        Assert.Empty(await db.Patients.ToListAsync());
    }

    [Theory]
    [InlineData(16, 59, 6)]
    [InlineData(17, 4, 1)]
    [InlineData(17, 5, 1440)]
    public void Daily_run_is_scheduled_at_0005_vietnam_time(int utcHour, int utcMinute, int expectedMinutes)
    {
        using var factory = new FixedSeedWebApplicationFactory(new DateTime(2026, 11, 27, utcHour, utcMinute, 0, DateTimeKind.Utc));
        var clock = factory.Services.GetRequiredService<IDateTimeProvider>();
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), DevelopmentSlotCoverageWorker.GetDelayUntilNextRun(clock));
    }

    private static async Task AddExistingActivityAsync(AppDbContext db, DateOnly today)
    {
        var assignment = await db.StaffFacilityAssignments.FirstAsync(a => a.Role == "Doctor" && a.DepartmentId != null);
        var doctor = await db.Doctors.FirstAsync(d => d.UserId == assignment.UserId);
        var otherSpecialty = await db.Specialties.FirstAsync(s => !s.DoctorSpecialties.Any(ds => ds.DoctorId == doctor.Id));
        var patient = await db.Patients.FirstAsync(p => p.UserId != null);
        var past = today.AddDays(-2);
        db.Appointments.Add(new Appointment
        {
            AppointmentCode = "COVERAGE-EXISTING", PatientId = patient.Id, DoctorId = doctor.Id,
            SpecialtyId = otherSpecialty.Id, FacilityId = assignment.FacilityId,
            AppointmentDate = past, StartTime = new(9, 0), EndTime = new(9, 30), Status = AppointmentStatus.Confirmed,
            AppointmentSlot = new AppointmentSlot
            {
                DoctorId = doctor.Id, SlotDate = past, StartTime = new(9, 0), EndTime = new(9, 30), IsBooked = true
            }
        });
        db.PatientVisits.Add(new PatientVisit
        {
            VisitCode = "COVERAGE-EXISTING", PatientId = patient.Id, FacilityId = assignment.FacilityId,
            DepartmentId = assignment.DepartmentId!.Value, VisitDate = past, CreatedByUserId = assignment.UserId
        });
        db.Patients.AddRange(
            new Patient { FullName = "Coverage patient A", MedicalRecordNumber = "COVERAGE-A", PhoneNumber = "0899000001", DateOfBirth = today.AddYears(-25) },
            new Patient { FullName = "Coverage patient B", MedicalRecordNumber = "COVERAGE-B", PhoneNumber = "0899000002", DateOfBirth = today.AddYears(-26) });
        await db.SaveChangesAsync();
    }

    private static async Task<QaRecords> ReadQaRecordsAsync(AppDbContext db)
    {
        var appointments = await db.Appointments.OrderBy(a => a.Id).Select(a => new { a.Id, a.Status, a.SpecialtyId }).ToListAsync();
        var visits = await db.PatientVisits.OrderBy(v => v.Id).Select(v => new { v.Id, v.Status }).ToListAsync();
        var patients = await db.Patients.OrderBy(p => p.Id).Select(p => new { p.Id, p.DateOfBirth }).ToListAsync();
        return new QaRecords(appointments.Select(a => (a.Id, a.Status, a.SpecialtyId)).ToArray(),
            visits.Select(v => (v.Id, v.Status)).ToArray(), patients.Select(p => (p.Id, p.DateOfBirth)).ToArray(),
            await db.Users.CountAsync(), await db.Specialties.CountAsync());
    }

    private sealed record QaRecords(
        (long Id, AppointmentStatus Status, long SpecialtyId)[] Appointments,
        (long Id, VisitStatus Status)[] Visits, (long Id, DateOnly? DateOfBirth)[] Patients, int Users, int Specialties);

    private sealed class CoverageApiFactory(string environment, bool coverageEnabled,
        string? existingDatabase = null, IDateTimeProvider? clock = null) : CustomWebApplicationFactory
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Minimal hosting reads seed flags before ConfigureAppConfiguration runs.
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoSeed:Enabled"] = "false",
                ["DemoSeed:SlotCoverageOnly"] = coverageEnabled.ToString(),
                ["ClinicOperatingHours:Mode"] = "TwentyFourSeven",
                ["Rag:IsEnabled"] = "false"
            }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            if (existingDatabase is null || clock is null) return;

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddSingleton(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(existingDatabase).Options);
                services.RemoveAll<IDateTimeProvider>();
                services.AddSingleton(clock);
            });
        }
    }
}
