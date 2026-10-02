using System;
using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    private static readonly TimeZoneInfo _vietnamTimeZone = ResolveVietnamTimeZone();

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Asia/Ho_Chi_Minh",
                    TimeSpan.FromHours(7),
                    "Vietnam Standard Time (UTC+7)",
                    "Vietnam Standard Time"
                );
            }
        }
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public TimeZoneInfo VietnamTimeZone => _vietnamTimeZone;

    public DateTime VietnamNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _vietnamTimeZone);

    public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);

    public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);

    public DateTime ConvertUtcToVietnam(DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, _vietnamTimeZone);
    }

    public DateTime ConvertVietnamToUtc(DateTime vnDateTime)
    {
        var unspecified = DateTime.SpecifyKind(vnDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, _vietnamTimeZone);
    }
}
