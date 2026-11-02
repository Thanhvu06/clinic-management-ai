using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Services;

namespace ClinicManagement.Infrastructure.Persistence;

public static class DemoSeedCalendar
{
    public static bool IsClosed(DateOnly date, IClinicOperatingHoursPolicy? policy = null) => !(policy ?? Schedules.ClinicOperatingHoursPolicy.Standard).IsOpen(date);

    public static DateOnly NextOpen(DateOnly date, IClinicOperatingHoursPolicy? policy = null) => IsClosed(date, policy) ? date.AddDays(1) : date;

    public static DateOnly PreviousOpen(DateOnly date, IClinicOperatingHoursPolicy? policy = null) => IsClosed(date, policy) ? date.AddDays(-1) : date;

    public static IDateTimeProvider Anchor(IDateTimeProvider clock) =>
        clock;

    public static IServiceProvider WithAnchoredClock(IServiceProvider services) =>
        new AnchoredServices(services, Anchor(services.GetService(typeof(IDateTimeProvider)) as IDateTimeProvider ?? new DateTimeProvider()));

    public static string? ClosedDayNotice(IDateTimeProvider clock, IClinicOperatingHoursPolicy? policy = null) =>
        IsClosed(clock.VietnamToday, policy)
            ? $"Hôm nay Chủ nhật – phòng khám nghỉ. Lịch hẹn tiếp theo vào thứ Hai {NextOpen(clock.VietnamToday):dd/MM/yyyy}."
            : null;

    private sealed class AnchoredServices(IServiceProvider inner, IDateTimeProvider clock) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IDateTimeProvider) ? clock : inner.GetService(serviceType);
    }

}
