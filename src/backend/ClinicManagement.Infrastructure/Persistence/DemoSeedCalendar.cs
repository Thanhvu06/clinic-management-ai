using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Services;

namespace ClinicManagement.Infrastructure.Persistence;

public static class DemoSeedCalendar
{
    public static bool IsClosed(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday;

    public static DateOnly NextOpen(DateOnly date) => IsClosed(date) ? date.AddDays(1) : date;

    public static DateOnly PreviousOpen(DateOnly date) => IsClosed(date) ? date.AddDays(-1) : date;

    public static IDateTimeProvider Anchor(IDateTimeProvider clock) =>
        clock;

    public static IServiceProvider WithAnchoredClock(IServiceProvider services) =>
        new AnchoredServices(services, Anchor(services.GetService(typeof(IDateTimeProvider)) as IDateTimeProvider ?? new DateTimeProvider()));

    public static string? ClosedDayNotice(IDateTimeProvider clock) =>
        IsClosed(clock.VietnamToday)
            ? $"Hôm nay Chủ nhật – phòng khám nghỉ. Lịch hẹn tiếp theo vào thứ Hai {NextOpen(clock.VietnamToday):dd/MM/yyyy}."
            : null;

    private sealed class AnchoredServices(IServiceProvider inner, IDateTimeProvider clock) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IDateTimeProvider) ? clock : inner.GetService(serviceType);
    }

}
