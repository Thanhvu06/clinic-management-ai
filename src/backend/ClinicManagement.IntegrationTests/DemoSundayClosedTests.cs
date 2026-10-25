using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicManagement.IntegrationTests;

public class DemoSundayClosedTests
{
    [Theory]
    [InlineData("2026-10-25T02:00:00Z", true)]
    [InlineData("2026-10-25T02:00:00Z", false)]
    [InlineData("2026-10-29T02:00:00Z", false)]
    [InlineData("2026-10-30T02:00:00Z", true)]
    [InlineData("2026-10-27T02:00:00Z", false)]
    public async Task DemoSeedHasNoSundayShiftsSlotsAppointmentsOrPayments(string utc, bool resetDemo)
    {
        var clock = new FixedClock(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        using var factory = new CustomWebApplicationFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
        }));
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(resetDemo ? DemoSeedCalendar.WithAnchoredClock(services) : services, 30, true);
        var db = services.GetRequiredService<AppDbContext>();

        static bool Sunday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday;
        Assert.DoesNotContain(await db.DoctorWorkSchedules.ToListAsync(), s => Sunday(s.WorkDate));
        Assert.DoesNotContain(await db.AppointmentSlots.ToListAsync(), s => Sunday(s.SlotDate));
        var appointments = await db.Appointments.ToListAsync();
        Assert.NotEmpty(appointments);
        Assert.DoesNotContain(appointments, a => Sunday(a.AppointmentDate));
        var payments = await db.Payments.ToListAsync();
        Assert.NotEmpty(payments);
        Assert.DoesNotContain(payments, p => Sunday(DateOnly.FromDateTime(clock.ConvertUtcToVietnam(p.ReceivedAtUtc))));
        Assert.DoesNotContain(await db.HealthPackageRegistrations.ToListAsync(), r => Sunday(r.PreferredDate));
        Assert.DoesNotContain(await db.DoctorLeaveRequests.ToListAsync(), l => Sunday(DateOnly.FromDateTime(l.StartDateTime)));

        var seedToday = resetDemo ? DemoSeedCalendar.NextOpen(clock.VietnamToday) : DemoSeedCalendar.PreviousOpen(clock.VietnamToday);
        if (!resetDemo && DemoSeedCalendar.IsClosed(clock.VietnamToday))
        {
            Assert.DoesNotContain(appointments, a => a.AppointmentDate == clock.VietnamToday);
            return;
        }
        Assert.Contains(appointments, a => a.AppointmentDate == seedToday);
        Assert.Contains(payments, p => DateOnly.FromDateTime(clock.ConvertUtcToVietnam(p.ReceivedAtUtc)) == seedToday);
    }

    [Fact]
    public void SundayResetAnchorsTodayToMondayWithWarning()
    {
        var sunday = new FixedClock(new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc));
        var anchored = DemoSeedCalendar.Anchor(sunday);

        Assert.Equal(new DateOnly(2026, 10, 26), anchored.VietnamToday);
        Assert.Equal(DayOfWeek.Monday, anchored.VietnamToday.DayOfWeek);
        Assert.Equal(sunday.VietnamTime, anchored.VietnamTime);
        var notice = DemoSeedCalendar.ClosedDayNotice(sunday);
        Assert.NotNull(notice);
        Assert.StartsWith("Hôm nay Chủ nhật – phòng khám nghỉ", notice);
        Assert.Contains("26/10/2026", notice);
    }

    [Fact]
    public void WeekdayResetKeepsTodayWithoutWarning()
    {
        var saturday = new FixedClock(new DateTime(2026, 10, 24, 2, 0, 0, DateTimeKind.Utc));

        Assert.Same(saturday, DemoSeedCalendar.Anchor(saturday));
        Assert.Null(DemoSeedCalendar.ClosedDayNotice(saturday));
    }

    private sealed class FixedClock(DateTime utc) : IDateTimeProvider
    {
        private readonly DateTimeProvider converter = new();
        public DateTime UtcNow => utc;
        public DateTime VietnamNow => ConvertUtcToVietnam(utc);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => converter.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime value) => converter.ConvertUtcToVietnam(value);
        public DateTime ConvertVietnamToUtc(DateTime value) => converter.ConvertVietnamToUtc(value);
    }
}
