using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>
/// A sentence typed while a wizard step is open that is itself a full booking request
/// ("tôi muốn đặt lịch khám nhi chiều mai") starts a new wizard instead of being read as the
/// step's text input, so its specialty, day and part of day are not lost.
/// </summary>
public static class AiBookingWizardRestart
{
    private static readonly Regex BookingVerb = new(
        @"\b(?:dat lich(?: hen| kham)?|dat kham|dat hen|dang ky kham|muon kham|hen kham|book lich)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TimePhrase = new(
        @"\b(?:(?:sang|chieu|toi) (?:mai|nay)|ngay mai|hom nay|ngay kia|tuan (?:sau|toi|nay)|cuoi tuan|som nhat|gan nhat|buoi (?:sang|chieu|toi)|(?<!\b(?:bac si|bac sy|bs|bsi|co|chi|anh|em|ba|ong|cho) )mai|sang|chieu)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Filler = new(
        @"\b(?:giup|cho|toi|minh|em|voi|nhe|nha|a|di|duoc khong|duoc|khong|vao|luc)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Typed dates and times ("ngày 22/10/2026", "thứ 3", "14h") answer a day or slot step; they name no specialty.
    private static readonly Regex DatePhrase = new(
        @"\b(?:ngay \d{1,2}(?: \d{1,2}(?: \d{4})?)?|\d{1,2} \d{1,2}(?: \d{4})?|thu [2-7]|chu nhat|\d{1,2} ?(?:h|gio)(?: ?\d{1,2})?|ngay)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A specialty or doctor always makes a new request. A sentence that only names a day or part of day
    /// restarts only on the specialty step, where nothing has been chosen yet; on later steps it answers the step.
    /// </summary>
    public static bool IsNewRequest(string? typed, DateOnly today, BookingWizardStage stage)
    {
        if (string.IsNullOrWhiteSpace(typed) || typed.Length > 200) return false;
        var plain = AiTextNormalizer.NormalizeForComparison(typed);
        if (!BookingVerb.IsMatch(plain)) return false;
        return Hint(typed) is not null ||
            stage == BookingWizardStage.Specialty && AiBookingWizardPreference.Parse(typed, today) != (0, BookingDayPart.None);
    }

    /// <summary>The words after the booking verb with day and time words removed ("khám nhi"), or null.</summary>
    public static string? Hint(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var plain = Regex.Replace(AiTextNormalizer.NormalizeForComparison(typed), @"[^a-z0-9\s]", " ");
        var verb = BookingVerb.Match(plain);
        if (!verb.Success) return null;
        var rest = Filler.Replace(DatePhrase.Replace(TimePhrase.Replace(plain[(verb.Index + verb.Length)..], " "), " "), " ");
        rest = Regex.Replace(Regex.Replace(rest, @"[^a-z0-9\s]", " "), @"\s+", " ").Trim();
        return Regex.Replace(rest, @"^(?:kham|lich)\b\s*", string.Empty).Length == 0 ? null : rest;
    }
}
