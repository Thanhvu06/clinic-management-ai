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
    private static readonly Regex ExplicitDayPart = new(
        @"\b(?:(?:buoi|ban) (sang|chieu|toi|dem)|(sang|chieu|toi|dem)(?= (?:mai|nay|thu|chu nhat|ngay)\b))\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex Night = new(@"\bđêm\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly string[] Weekdays = ["hai", "ba", "tu", "nam", "sau", "bay"];

    internal static bool IsDatedQuestion(string text)
    {
        var plain = AiTextNormalizer.NormalizeForComparison(text);
        return ScheduleCue.IsMatch(plain) &&
            (Day.IsMatch(plain) || ParsePart(text, plain, AiBookingWizardPreference.Parse(text, new DateOnly(2000, 1, 1)).Part) != BookingDayPart.None);
    }

    internal static (DateOnly? Date, BookingDayPart Part, bool Invalid) Parse(string text, DateOnly today)
    {
        if (!IsDatedQuestion(text)) return (null, BookingDayPart.None, false);
        var plain = AiTextNormalizer.NormalizeForComparison(text);
        var (day, part) = AiBookingWizardPreference.Parse(text, today);
        part = ParsePart(text, plain, part);
        if (part == BookingDayPart.Night && Regex.IsMatch(plain, @"\bdem nay\b")) day = today.DayNumber;
        var date = Day.IsMatch(plain)
            ? AiBookingWizardService.ParseDay(WrittenWeekday.Replace(plain, match => $"thu {Array.IndexOf(Weekdays, match.Groups[1].Value) + 2}"),
                today, includeTodayForWeekday: true)
            : day > 0 ? DateOnly.FromDayNumber(day) : today;
        return (date, part, date is null);
    }

    private static BookingDayPart ParsePart(string text, string plain, BookingDayPart fallback)
    {
        var match = ExplicitDayPart.Match(plain);
        return (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) switch
        {
            "sang" => BookingDayPart.Morning,
            "chieu" => BookingDayPart.Afternoon,
            "toi" => BookingDayPart.Evening,
            "dem" => BookingDayPart.Night,
            _ => Night.IsMatch(text) ? BookingDayPart.Night : fallback
        };
    }

    internal static BookingDayPart PartOf(TimeOnly start) =>
        start < AiBookingWizardPreference.MorningStart || start >= AiBookingWizardPreference.EveningEnd ? BookingDayPart.Night
        : start < AiBookingWizardPreference.AfternoonStart ? BookingDayPart.Morning
        : start < AiBookingWizardPreference.EveningStart ? BookingDayPart.Afternoon
        : BookingDayPart.Evening;
}
