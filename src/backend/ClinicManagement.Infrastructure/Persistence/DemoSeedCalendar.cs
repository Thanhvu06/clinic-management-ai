using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Services;

namespace ClinicManagement.Infrastructure.Persistence;

public static class DemoSeedCalendar
{
    public static bool IsClosed(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday;

    public static DateOnly NextOpen(DateOnly date) => IsClosed(date) ? date.AddDays(1) : date;

    public static DateOnly PreviousOpen(DateOnly date) => IsClosed(date) ? date.AddDays(-1) : date;

    public static IDateTimeProvider Anchor(IDateTimeProvider clock) =>
        IsClosed(clock.VietnamToday) ? new ShiftedClock(clock, TimeSpan.FromDays(1)) : clock;

    public static IServiceProvider WithAnchoredClock(IServiceProvider services) =>
        new AnchoredServices(services, Anchor(services.GetService(typeof(IDateTimeProvider)) as IDateTimeProvider ?? new DateTimeProvider()));

    public static string? ClosedDayNotice(IDateTimeProvider clock) =>
        IsClosed(clock.VietnamToday)
            ? $"Hôm nay Chủ nhật – phòng khám nghỉ. Dữ liệu \"hôm nay\" của demo được neo sang thứ Hai {NextOpen(clock.VietnamToday):dd/MM/yyyy}."
            : null;

    private sealed class AnchoredServices(IServiceProvider inner, IDateTimeProvider clock) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IDateTimeProvider) ? clock : inner.GetService(serviceType);
    }

    private sealed class ShiftedClock(IDateTimeProvider inner, TimeSpan offset) : IDateTimeProvider
    {
        public DateTime UtcNow => inner.UtcNow.Add(offset);
        public DateTime VietnamNow => inner.ConvertUtcToVietnam(UtcNow);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => inner.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime utcDateTime) => inner.ConvertUtcToVietnam(utcDateTime);
        public DateTime ConvertVietnamToUtc(DateTime vnDateTime) => inner.ConvertVietnamToUtc(vnDateTime);
    }
}
