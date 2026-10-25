using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Expiry;

public static class NoShowRestrictionRule
{
    public static DateTime? CalculateBlockedUntil(IEnumerable<DateTime> noShows, DateTime now, ExpiryRulesOptions options)
    {
        var events = new Queue<DateTime>();
        DateTime? blockedUntil = null;
        foreach (var time in noShows.Where(t => t <= now).Order())
        {
            while (events.Count > 0 && events.Peek() < time.AddDays(-options.NoShowWindowDays)) events.Dequeue();
            events.Enqueue(time);
            if (events.Count >= options.NoShowThreshold && (!blockedUntil.HasValue || time >= blockedUntil.Value))
                blockedUntil = time.AddDays(options.OnlineBookingBlockDays);
        }
        return blockedUntil > now ? blockedUntil : null;
    }

    public static async Task<DateTime?> GetBlockedUntilAsync(AppDbContext db, IDateTimeProvider clock, ExpiryRulesOptions options, long patientId)
    {
        var records = await db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId && a.Status == AppointmentStatus.NoShow)
            .Select(a => new
            {
                a.AppointmentDate, a.StartTime,
                RecordedAt = a.Histories.Where(h => h.NewStatus == AppointmentStatus.NoShow).Select(h => (DateTime?)h.CreatedAt).Max()
            }).ToListAsync();
        var times = records.Select(a => a.RecordedAt.HasValue ? clock.ConvertUtcToVietnam(a.RecordedAt.Value)
            : a.AppointmentDate.ToDateTime(a.StartTime).AddMinutes(options.AppointmentGraceMinutes));
        return CalculateBlockedUntil(times, clock.VietnamNow, options);
    }

    public static async Task EnsureOnlineBookingAllowedAsync(AppDbContext db, IDateTimeProvider clock, ExpiryRulesOptions options, long patientId)
    {
        if (await GetBlockedUntilAsync(db, clock, options, patientId) is { } until)
            throw new BusinessException("ONLINE_BOOKING_BLOCKED", Message(until, options));
    }

    public static string Message(DateTime until, ExpiryRulesOptions options) =>
        $"Bạn đã vắng mặt ít nhất {options.NoShowThreshold} lần trong {options.NoShowWindowDays} ngày. Tạm khóa đặt lịch online đến {until:dd/MM/yyyy HH:mm}. Vui lòng liên hệ lễ tân để đặt lịch.";
}
