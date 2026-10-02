using System;

namespace ClinicManagement.Application.Common.Interfaces;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTime VietnamNow { get; }
    DateOnly VietnamToday { get; }
    TimeOnly VietnamTime { get; }
    TimeZoneInfo VietnamTimeZone { get; }
    DateTime ConvertUtcToVietnam(DateTime utcDateTime);
    DateTime ConvertVietnamToUtc(DateTime vnDateTime);
}
