using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;

namespace ClinicManagement.Infrastructure.AI;

internal static class PatientChatDoctorSchedule
{
    private static readonly Regex Day = new(
        @"\b(?:thu (?:[2-7]|hai|ba|tu|nam|sau|bay)|chu nhat|hom nay|ngay kia|mai|ngay \d{1,2})\b|\b\d{1,2}[/\-]\d{1,2}\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex ScheduleCue = new(@"\b(?:lich|kham|truc|lam viec|ranh)\b", RegexOptions.CultureInvariant);
    private static readonly Regex WrittenWeekday = new(@"\bthu (hai|ba|tu|nam|sau|bay)\b", RegexOptions.CultureInvariant);
    private static readonly string[] Weekdays = ["hai", "ba", "tu", "nam", "sau", "bay"];

    internal static bool IsDatedQuestion(string text)
    {
        var plain = AiTextNormalizer.NormalizeForComparison(text);
        return ScheduleCue.IsMatch(plain) &&
            (Day.IsMatch(plain) || AiBookingWizardPreference.Parse(text, new DateOnly(2000, 1, 1)).Part != BookingDayPart.None ||
                Regex.IsMatch(plain, @"\bbuoi (?:sang|chieu|toi)\b"));
    }

    internal static (DateOnly? Date, BookingDayPart Part, bool Invalid) Parse(string text, DateOnly today)
    {
        if (!IsDatedQuestion(text)) return (null, BookingDayPart.None, false);
        var plain = AiTextNormalizer.NormalizeForComparison(text);
        var (day, part) = AiBookingWizardPreference.Parse(text, today);
        if (part == BookingDayPart.None)
        {
            part = Regex.Match(plain, @"\bbuoi (sang|chieu|toi)\b").Groups[1].Value switch
            {
                "sang" => BookingDayPart.Morning,
                "chieu" => BookingDayPart.Afternoon,
                "toi" => BookingDayPart.Evening,
                _ => BookingDayPart.None
            };
        }
        var date = Day.IsMatch(plain)
            ? AiBookingWizardService.ParseDay(WrittenWeekday.Replace(plain, match => $"thu {Array.IndexOf(Weekdays, match.Groups[1].Value) + 2}"),
                today, includeTodayForWeekday: true)
            : day > 0 ? DateOnly.FromDayNumber(day) : today;
        return (date, part, date is null);
    }
}
