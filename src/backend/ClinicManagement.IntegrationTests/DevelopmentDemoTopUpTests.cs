using System.Text.Json;
using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class DevelopmentDemoTopUpTests
{
    [Theory]
    [InlineData("2026-11-08T02:00:00Z")]
    [InlineData("2026-11-08T16:45:00Z")]
    [InlineData("2026-12-31T16:45:00Z")]
    public async Task Expired_demo_is_replenished_and_second_run_does_not_change_any_rows(string utc)
    {
        using var fixture = await Fixture.CreateAsync(utc);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await MainDoctorAsync(db);
        var patient = await db.Patients.SingleAsync(x => x.Email == "patient@cliniccare.local");
        var past = fixture.Clock.VietnamToday.AddDays(-1);
        var oldSlot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = past, StartTime = new(9, 0), EndTime = new(9, 30) };
        db.AppointmentSlots.Add(oldSlot);
        await db.SaveChangesAsync();
        var expired = new Appointment
        {
            AppointmentCode = "OLD-NOSHOW", DoctorId = doctor.Id, PatientId = patient.Id,
            SpecialtyId = doctor.DoctorSpecialties.Single(x => x.IsPrimary).SpecialtyId,
            AppointmentSlotId = oldSlot.Id, AppointmentDate = past, StartTime = oldSlot.StartTime, EndTime = oldSlot.EndTime,
            Status = AppointmentStatus.NoShow, Reason = "Giữ nguyên lịch đã hết hạn"
        };
        db.Appointments.Add(expired);
        await db.SaveChangesAsync();
        var before = EntitySnapshot(db, expired);

        await fixture.Worker.RunOnceAsync();

        await AssertTargetsAsync(db, fixture.Clock, 12, 3);
        db.ChangeTracker.Clear();
        Assert.Equal(before, EntitySnapshot(db, (await db.Appointments.FindAsync(expired.Id))!));
        var snapshot = await SnapshotAsync(db);
        await fixture.Worker.RunOnceAsync();
        Assert.Equal(snapshot, await SnapshotAsync(db));
    }

    [Theory]
    [InlineData("2026-11-08T16:45:00Z")]
    [InlineData("2026-12-31T16:45:00Z")]
    public async Task Advancing_a_day_preserves_expired_rows_and_restores_targets_with_new_dates(string utc)
    {
        using var fixture = await Fixture.CreateAsync(utc);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await fixture.Worker.RunOnceAsync();
        var oldIds = await db.Appointments.Select(x => x.Id).ToListAsync();
        fixture.Clock.Utc = fixture.Clock.Utc.AddDays(1);
        var expiryOptions = Options.Create(new ExpiryRulesOptions());
        await new PendingAppointmentExpiryRule(db, fixture.Clock, expiryOptions).RunAsync(default);
        await new ConfirmedAppointmentExpiryRule(db, fixture.Clock, expiryOptions).RunAsync(default);
        await new WaitingVisitExpiryRule(db, fixture.Clock, expiryOptions).RunAsync(default);
        db.ChangeTracker.Clear();
        var expired = JsonSerializer.Serialize(await db.Appointments.AsNoTracking().Where(x => oldIds.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.AppointmentSlotId, x.PatientId }).ToListAsync());

        await fixture.Worker.RunOnceAsync();

        await AssertTargetsAsync(db, fixture.Clock, 12, 3);
        Assert.Equal(expired, JsonSerializer.Serialize(await db.Appointments.AsNoTracking().Where(x => oldIds.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.AppointmentSlotId, x.PatientId }).ToListAsync()));
        var snapshot = await SnapshotAsync(db);
        await fixture.Worker.RunOnceAsync();
        Assert.Equal(snapshot, await SnapshotAsync(db));
    }

    [Fact]
    public async Task Inactive_shifts_and_leave_are_not_overridden_when_capacity_is_missing()
    {
        using var fixture = await Fixture.CreateAsync("2026-11-08T02:00:00Z");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctorIds = await db.Doctors.Join(db.Users, d => d.UserId, u => u.Id, (d, u) => new { d.Id, u.Email })
            .Where(x => x.Email == "doctor@cliniccare.local" || x.Email == "bacsi.02@cliniccare.local" || x.Email == "bacsi.03@cliniccare.local").Select(x => x.Id).ToListAsync();
        foreach (var doctorId in doctorIds)
        foreach (var day in new[] { fixture.Clock.VietnamToday, fixture.Clock.VietnamToday.AddDays(1) })
            db.DoctorWorkSchedules.Add(new DoctorWorkSchedule { DoctorId = doctorId, WorkDate = day, StartTime = new(0, 0), EndTime = new(23, 30), IsActive = false });
        await db.SaveChangesAsync();
        await fixture.Worker.RunOnceAsync();
        Assert.Empty(await db.Appointments.ToListAsync());
        Assert.All(await db.DoctorWorkSchedules.ToListAsync(), row => Assert.False(row.IsActive));
        db.DoctorWorkSchedules.RemoveRange(await db.DoctorWorkSchedules.ToListAsync());
        foreach (var doctorId in doctorIds)
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest
            {
                DoctorId = doctorId, StartDateTime = fixture.Clock.VietnamNow, EndDateTime = fixture.Clock.VietnamNow.AddHours(24),
                Status = DoctorLeaveRequestStatus.Pending, Reason = "Nghỉ đã có, không ghi đè"
            });
        await db.SaveChangesAsync();
        await fixture.Worker.RunOnceAsync();
        Assert.Empty(await db.Appointments.ToListAsync());
        Assert.All(await db.DoctorLeaveRequests.ToListAsync(), row => Assert.Equal(DoctorLeaveRequestStatus.Pending, row.Status));
    }

    [Fact]
    public async Task Occupied_slots_existing_patient_days_and_real_patients_are_preserved()
    {
        using var fixture = await Fixture.CreateAsync("2026-11-08T02:00:00Z");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await MainDoctorAsync(db);
        var mainPatient = await db.Patients.SingleAsync(x => x.Email == "patient@cliniccare.local");
        var other = await db.Patients.SingleAsync(x => x.Email == "patient.01@cliniccare.local");
        var realPatient = new Patient { FullName = "Bệnh nhân ngoài demo", Email = "real@example.test", PhoneNumber = "0911111222", DateOfBirth = new(1980, 1, 1), Gender = Gender.Male, MedicalRecordNumber = "REAL-PRESERVE" };
        db.Patients.Add(realPatient);
        var taken = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = fixture.Clock.VietnamToday, StartTime = new(10, 0), EndTime = new(10, 30), IsBooked = true };
        var held = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = fixture.Clock.VietnamToday, StartTime = new(11, 0), EndTime = new(11, 30), IsBooked = false };
        db.AppointmentSlots.AddRange(taken, held);
        await db.SaveChangesAsync();
        var existing = new Appointment
        {
            AppointmentCode = "PRESERVE-EXISTING", DoctorId = doctor.Id, PatientId = other.Id,
            SpecialtyId = doctor.DoctorSpecialties.Single(x => x.IsPrimary).SpecialtyId, AppointmentSlotId = held.Id,
            AppointmentDate = held.SlotDate, StartTime = held.StartTime, EndTime = held.EndTime, Status = AppointmentStatus.Confirmed, Reason = "Không thay đổi"
        };
        db.Appointments.Add(existing);
        var cancelledSlot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = fixture.Clock.VietnamToday, StartTime = new(8, 0), EndTime = new(8, 30) };
        db.AppointmentSlots.Add(cancelledSlot);
        await db.SaveChangesAsync();
        db.Appointments.Add(new Appointment
        {
            AppointmentCode = "PRESERVE-CANCELLED", DoctorId = doctor.Id, PatientId = mainPatient.Id,
            SpecialtyId = existing.SpecialtyId, AppointmentSlotId = cancelledSlot.Id,
            AppointmentDate = cancelledSlot.SlotDate, StartTime = cancelledSlot.StartTime, EndTime = cancelledSlot.EndTime, Status = AppointmentStatus.Cancelled
        });
        await db.SaveChangesAsync();
        var snapshot = EntitySnapshot(db, existing);

        await fixture.Worker.RunOnceAsync();

        db.ChangeTracker.Clear();
        Assert.Equal(snapshot, EntitySnapshot(db, (await db.Appointments.FindAsync(existing.Id))!));
        Assert.False(await db.Appointments.AnyAsync(x => x.AppointmentSlotId == taken.Id));
        Assert.Single(await db.Appointments.Where(x => x.AppointmentSlotId == held.Id).ToListAsync());
        Assert.False((await db.AppointmentSlots.FindAsync(held.Id))!.IsBooked);
        Assert.True((await db.AppointmentSlots.FindAsync(taken.Id))!.IsBooked);
        Assert.False(await db.Appointments.AnyAsync(x => x.PatientId == realPatient.Id));
        await AssertTargetsAsync(db, fixture.Clock, 12, 3);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", true)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public async Task Disabled_or_non_development_worker_never_resolves_a_database_scope(string environment, bool enabled)
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        using var worker = new DevelopmentDemoTopUpWorker(scopes.Object, new EnvironmentStub(environment), Options.Create(new DemoTopUpOptions { Enabled = enabled }), NullLogger<DevelopmentDemoTopUpWorker>.Instance);
        await worker.RunOnceAsync();
        await worker.StartAsync(default);
        await worker.StopAsync(default);
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Invalid_top_up_options_do_not_fault_the_host_or_resolve_a_database()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var options = new Mock<IOptions<DemoTopUpOptions>>();
        options.SetupGet(x => x.Value).Throws(new InvalidOperationException("Synthetic binding failure"));
        using var worker = new DevelopmentDemoTopUpWorker(scopes.Object, new EnvironmentStub("Development"), options.Object, NullLogger<DevelopmentDemoTopUpWorker>.Instance);
        await worker.RunOnceAsync();
        await worker.StartAsync(default);
        await worker.StopAsync(default);
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Custom_targets_and_periodic_retry_survive_a_failed_cycle()
    {
        using var fixture = await Fixture.CreateAsync("2026-11-08T02:00:00Z");
        var realScopes = fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>();
        var scopes = new Mock<IServiceScopeFactory>();
        var attempts = 0;
        scopes.Setup(x => x.CreateScope()).Returns(() =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("Synthetic top-up failure");
            return realScopes.CreateScope();
        });
        var logger = new Mock<ILogger<DevelopmentDemoTopUpWorker>>();
        var timer = new ManualTime();
        using var worker = new DevelopmentDemoTopUpWorker(scopes.Object, new EnvironmentStub("Development"),
            Options.Create(new DemoTopUpOptions { Enabled = true, TopUpIntervalMinutes = 7, MinUpcomingConfirmed = 6, MinPendingConfirmations = 2 }), logger.Object, timer);
        await worker.StartAsync(default);
        await UntilAsync(() => timer.TimerCount == 1);
        Assert.Equal(TimeSpan.FromMinutes(7), timer.DueTime);
        timer.Fire();
        await UntilAsync(() => Volatile.Read(ref attempts) == 1 && timer.TimerCount == 2);
        timer.Fire();
        await UntilAsync(() => timer.TimerCount == 3);
        await worker.StopAsync(default);
        using var scope = fixture.Factory.Services.CreateScope();
        await AssertTargetsAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), fixture.Clock, 6, 2);
        Assert.Equal(2, attempts);
        logger.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((_, _) => true), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static readonly JsonSerializerOptions SnapshotOptions = new() { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
    private static string EntitySnapshot(AppDbContext db, object entity) => JsonSerializer.Serialize(db.Entry(entity).CurrentValues.Properties.ToDictionary(x => x.Name, x => db.Entry(entity).Property(x.Name).CurrentValue));
    private static Task<Doctor> MainDoctorAsync(AppDbContext db) => db.Doctors.Include(x => x.DoctorSpecialties).ThenInclude(x => x.Specialty).SingleAsync(x => db.Users.Any(u => u.Id == x.UserId && u.Email == "doctor@cliniccare.local"));

    private static async Task AssertTargetsAsync(AppDbContext db, MutableClock clock, int confirmed, int pending)
    {
        var rows = await db.Appointments.AsNoTracking().Include(x => x.Patient).Include(x => x.Specialty).Include(x => x.AppointmentSlot).ToListAsync();
        var upcoming = rows.Where(x => x.AppointmentDate.ToDateTime(x.StartTime) >= clock.VietnamNow && x.AppointmentDate.ToDateTime(x.StartTime) < clock.VietnamNow.AddHours(24)).ToList();
        Assert.Equal(confirmed, upcoming.Count(x => x.Status == AppointmentStatus.Confirmed));
        Assert.Equal(pending, upcoming.Count(x => x.Status == AppointmentStatus.Pending));
        Assert.Contains(upcoming, x => x.Patient.Email == "patient@cliniccare.local" && x.Status == AppointmentStatus.Confirmed);
        Assert.Equal(3, upcoming.Select(x => x.DoctorId).Distinct().Count());
        Assert.All(rows.GroupBy(x => (x.PatientId, x.AppointmentDate)), group => Assert.Single(group));
        Assert.All(rows.Where(x => x.AppointmentCode.StartsWith("APT-")), row =>
        {
            if (row.Status is not (AppointmentStatus.Confirmed or AppointmentStatus.Pending)) return;
            Assert.True(row.AppointmentDate.ToDateTime(row.StartTime) >= clock.VietnamNow.AddMinutes(30));
            Assert.True(row.AppointmentSlot.IsBooked);
            Assert.True(DemoPatientDevelopmentSeed.Allows(row.Patient, row.Specialty.SpecialtyCode, row.AppointmentDate));
        });
        var main = await MainDoctorAsync(db);
        Assert.True(await db.PatientVisits.AnyAsync(x => x.VisitDate == clock.VietnamToday && x.AssignedDoctorId == main.Id && x.Status == VisitStatus.WaitingForDoctor));
    }

    private static async Task<string> SnapshotAsync(AppDbContext db)
    {
        db.ChangeTracker.Clear();
        return JsonSerializer.Serialize(new
        {
            Appointments = await db.Appointments.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Histories = await db.AppointmentHistories.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Visits = await db.PatientVisits.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Slots = await db.AppointmentSlots.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            Schedules = await db.DoctorWorkSchedules.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            PatientCount = await db.Patients.CountAsync()
        }, SnapshotOptions);
    }

    private static async Task UntilAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!ready()) await Task.Delay(10, timeout.Token);
    }

    private sealed class Fixture(WebApplicationFactory<Program> factory, MutableClock clock, CustomWebApplicationFactory parent) : IDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public MutableClock Clock { get; } = clock;
        public DevelopmentDemoTopUpWorker Worker { get; } = new(factory.Services.GetRequiredService<IServiceScopeFactory>(), new EnvironmentStub("Development"), Options.Create(new DemoTopUpOptions { Enabled = true }), NullLogger<DevelopmentDemoTopUpWorker>.Instance);
        public static async Task<Fixture> CreateAsync(string utc)
        {
            var clock = new MutableClock(DateTime.Parse(utc).ToUniversalTime());
            var parent = new FixedSeedWebApplicationFactory();
            var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDateTimeProvider>(clock);
                services.Configure<ClinicOperatingHoursOptions>(x => x.Mode = "TwentyFourSeven");
            }));
            using var scope = factory.Services.CreateScope();
            await DevelopmentDataSeeder.SeedMasterDataAsync(scope.ServiceProvider);
            Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), x => x is DevelopmentDemoTopUpWorker);
            return new Fixture(factory, clock, parent);
        }
        public void Dispose() { Worker.Dispose(); Factory.Dispose(); parent.Dispose(); }
    }

    private sealed class MutableClock(DateTime utc) : IDateTimeProvider
    {
        private readonly DateTimeProvider converter = new();
        public DateTime Utc { get; set; } = utc;
        public DateTime UtcNow => Utc;
        public DateTime VietnamNow => ConvertUtcToVietnam(Utc);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => converter.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime value) => converter.ConvertUtcToVietnam(value);
        public DateTime ConvertVietnamToUtc(DateTime value) => converter.ConvertVietnamToUtc(value);
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DemoTopUpTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ManualTime : TimeProvider
    {
        private Action? callback;
        public int TimerCount;
        public TimeSpan DueTime { get; private set; }
        public override ITimer CreateTimer(TimerCallback timerCallback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            callback = () => timerCallback(state);
            DueTime = dueTime;
            Interlocked.Increment(ref TimerCount);
            return new TimerStub();
        }
        public void Fire() => callback!();
        private sealed class TimerStub : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
