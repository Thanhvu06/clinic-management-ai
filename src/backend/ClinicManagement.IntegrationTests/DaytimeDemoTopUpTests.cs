using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class DaytimeDemoTopUpTests
{
    [Theory]
    [InlineData("2026-11-09T16:45:00Z")]
    [InlineData("2026-12-31T16:45:00Z")]
    public async Task Main_demo_uses_daytime_while_other_demo_patients_keep_night_slots(string utc)
    {
        using var fixture = await Fixture.CreateAsync(utc);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await fixture.RunAsync(new DemoTopUpOptions { Enabled = true });

        var rows = await db.Appointments.Include(row => row.Patient).ToListAsync();
        var main = Assert.Single(rows, row => row.Patient.Email == "patient@cliniccare.local");
        Assert.InRange(main.StartTime, new TimeOnly(7, 0), new TimeOnly(19, 59));
        Assert.True(main.AppointmentDate.ToDateTime(main.StartTime) >= fixture.Clock.VietnamNow.AddMinutes(30));
        Assert.Contains(rows, row => row.Patient.Email != "patient@cliniccare.local" &&
            (row.StartTime < new TimeOnly(7, 0) || row.StartTime >= new TimeOnly(20, 0)));
        Assert.All(rows.Where(row => row.Patient.Email != "patient@cliniccare.local"), row =>
            Assert.True(row.AppointmentDate.ToDateTime(row.StartTime) < fixture.Clock.VietnamNow.AddHours(24)));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public async Task Existing_future_daytime_slot_is_found_within_seven_days_and_not_duplicated(int days)
    {
        using var fixture = await Fixture.CreateAsync("2026-11-09T16:45:00Z");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await BlockFirstDayAsync(db, fixture.Clock);
        var slot = await FutureSlotAsync(db, fixture.Clock.VietnamToday.AddDays(days));
        var options = new DemoTopUpOptions { Enabled = true, MinUpcomingConfirmed = 0, MinPendingConfirmations = 0 };

        await fixture.RunAsync(options);
        await fixture.RunAsync(options);

        var main = await db.Patients.SingleAsync(row => row.Email == "patient@cliniccare.local");
        var appointment = Assert.Single(await db.Appointments.Where(row => row.PatientId == main.Id).ToListAsync());
        Assert.Equal(slot.Id, appointment.AppointmentSlotId);
        Assert.True(appointment.AppointmentDate.ToDateTime(appointment.StartTime) < fixture.Clock.VietnamNow.AddDays(7));
    }

    [Fact]
    public async Task Slot_after_seven_days_is_not_booked_and_warning_is_logged()
    {
        using var fixture = await Fixture.CreateAsync("2026-11-09T16:45:00Z");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await BlockFirstDayAsync(db, fixture.Clock);
        var slot = await FutureSlotAsync(db, fixture.Clock.VietnamToday.AddDays(8));

        await fixture.RunAsync(new DemoTopUpOptions { Enabled = true, MinUpcomingConfirmed = 0, MinPendingConfirmations = 0 });

        Assert.False(await db.Appointments.AnyAsync());
        Assert.False((await db.AppointmentSlots.AsNoTracking().SingleAsync(row => row.Id == slot.Id)).IsBooked);
        fixture.Logger.Verify(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Configured_daytime_hours_are_respected()
    {
        using var fixture = await Fixture.CreateAsync("2026-11-09T16:45:00Z");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await fixture.RunAsync(new DemoTopUpOptions
        {
            Enabled = true, MinUpcomingConfirmed = 0, MinPendingConfirmations = 0,
            DemoPatientDayStartHour = 10, DemoPatientDayEndHour = 11
        });

        var main = await db.Patients.SingleAsync(row => row.Email == "patient@cliniccare.local");
        var appointment = Assert.Single(await db.Appointments.Where(row => row.PatientId == main.Id).ToListAsync());
        Assert.InRange(appointment.StartTime, new TimeOnly(10, 0), new TimeOnly(10, 59));
    }

    private static async Task BlockFirstDayAsync(AppDbContext db, IDateTimeProvider clock)
    {
        var doctorIds = await db.Doctors.Join(db.Users, doctor => doctor.UserId, user => user.Id,
                (doctor, user) => new { doctor.Id, user.Email })
            .Where(row => row.Email == "doctor@cliniccare.local" || row.Email == "bacsi.02@cliniccare.local" || row.Email == "bacsi.03@cliniccare.local")
            .Select(row => row.Id).ToListAsync();
        foreach (var doctorId in doctorIds)
        foreach (var day in new[] { clock.VietnamToday, clock.VietnamToday.AddDays(1) })
            db.DoctorWorkSchedules.Add(new DoctorWorkSchedule
            {
                DoctorId = doctorId, WorkDate = day, StartTime = new(0, 0), EndTime = new(23, 30), IsActive = false
            });
        await db.SaveChangesAsync();
    }

    private static async Task<AppointmentSlot> FutureSlotAsync(AppDbContext db, DateOnly day)
    {
        var doctor = await db.Doctors.SingleAsync(row => db.Users.Any(user => user.Id == row.UserId && user.Email == "doctor@cliniccare.local"));
        db.DoctorWorkSchedules.Add(new DoctorWorkSchedule
        {
            DoctorId = doctor.Id, WorkDate = day, StartTime = new(7, 0), EndTime = new(8, 0), IsActive = true
        });
        var slot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = day, StartTime = new(7, 0), EndTime = new(7, 30) };
        db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();
        return slot;
    }

    private sealed class Fixture(WebApplicationFactory<Program> factory, CustomWebApplicationFactory parent) : IDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public IDateTimeProvider Clock => Factory.Services.GetRequiredService<IDateTimeProvider>();
        public Mock<ILogger<DevelopmentDemoTopUpWorker>> Logger { get; } = new();

        public async Task RunAsync(DemoTopUpOptions options)
        {
            using var worker = new DevelopmentDemoTopUpWorker(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
                new EnvironmentStub(), Options.Create(options), Logger.Object);
            await worker.RunOnceAsync();
        }

        public static async Task<Fixture> CreateAsync(string utc)
        {
            var parent = new FixedSeedWebApplicationFactory(DateTime.Parse(utc).ToUniversalTime());
            var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.Configure<ClinicOperatingHoursOptions>(options => options.Mode = "TwentyFourSeven")));
            using var scope = factory.Services.CreateScope();
            await DevelopmentDataSeeder.SeedMasterDataAsync(scope.ServiceProvider);
            return new Fixture(factory, parent);
        }

        public void Dispose() { Factory.Dispose(); parent.Dispose(); }
    }

    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "DaytimeDemoTopUpTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
