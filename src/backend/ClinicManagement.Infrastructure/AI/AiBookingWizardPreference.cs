using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;

namespace ClinicManagement.Infrastructure.AI;

public enum BookingDayPart : byte { None, Morning, Afternoon, Evening, Night }

/// <summary>Day and part-of-day a patient typed when starting the wizard ("chiều mai").</summary>
public static class AiBookingWizardPreference
{
    public static readonly TimeOnly MorningStart = new(6, 0);
    public static readonly TimeOnly AfternoonStart = new(12, 0);
    public static readonly TimeOnly EveningStart = new(18, 0);
    public static readonly TimeOnly EveningEnd = new(22, 0);

    // Accents are required for the bare words: unaccented "toi" is "tôi" (me) and "sang" is "sang" (go over).
    private static readonly Regex Morning = new(@"\bsáng\b|\bsang (?:mai|nay)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Afternoon = new(@"\bchiều\b|\bchieu (?:mai|nay)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Evening = new(@"\btối\b|\btoi (?:mai|nay)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // "Mai" is also a given name ("bác sĩ Mai"), so it is not a day after a person title.
    private static readonly Regex Tomorrow = new(@"(?<!\b(?:bac si|bac sy|bs|bsi|co|chi|anh|em|ba|ong|cho) )\bmai\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static (int Day, BookingDayPart Part) Parse(string? typed, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(typed)) return (0, BookingDayPart.None);
        var accented = AiTextNormalizer.Normalize(typed).ToLowerInvariant();
        var plain = AiTextNormalizer.NormalizeForComparison(typed);
        var parts = new[]
        {
            (Morning.IsMatch(accented) || Morning.IsMatch(plain), BookingDayPart.Morning),
            (Afternoon.IsMatch(accented) || Afternoon.IsMatch(plain), BookingDayPart.Afternoon),
            (Evening.IsMatch(accented) || Evening.IsMatch(plain), BookingDayPart.Evening)
        }.Where(part => part.Item1).Select(part => part.Item2).ToList();
        var part = parts.Count == 1 ? parts[0] : BookingDayPart.None;
        DateOnly? day = Regex.IsMatch(plain, @"\bngay kia\b") ? today.AddDays(2)
            : Regex.IsMatch(plain, @"\bhom nay\b") || Regex.IsMatch(plain, @"\b(?:sang|chieu|toi) nay\b") ? today
            : Tomorrow.IsMatch(plain) ? today.AddDays(1)
            : null;
        return (day?.DayNumber ?? 0, part);
    }

    public static bool Matches(BookingDayPart part, TimeOnly start) => part switch
    {
        BookingDayPart.Morning => start >= MorningStart && start < AfternoonStart,
        BookingDayPart.Afternoon => start >= AfternoonStart && start < EveningStart,
        BookingDayPart.Evening => start >= EveningStart && start < EveningEnd,
        BookingDayPart.Night => start >= EveningEnd || start < MorningStart,
        _ => true
    };

    public static string Label(BookingDayPart part) => part switch
    {
        BookingDayPart.Morning => "buổi sáng",
        BookingDayPart.Afternoon => "buổi chiều",
        BookingDayPart.Evening => "buổi tối",
        BookingDayPart.Night => "ban đêm",
        _ => ""
    };
}
