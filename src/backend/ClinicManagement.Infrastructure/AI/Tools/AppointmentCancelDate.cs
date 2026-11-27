using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI.Tools;

/// <summary>
/// Reads the day a patient names in a cancel command ("ngày mai", "27/11", "27/11/2026"),
/// from text already normalized by AiTextNormalizer.NormalizeForComparison.
/// </summary>
public static class AppointmentCancelDate
{
    private static readonly Regex Numeric = new(@"(?<!\d)(\d{1,2})[/-](\d{1,2})(?:[/-](\d{4}))?(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Tomorrow = new(@"\b(?:ngay|sang|trua|chieu|toi) mai\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DayAfterTomorrow = new(@"\bngay (?:kia|mot)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Today = new(@"\bhom nay\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DateOnly? Parse(string normalizedText, DateOnly today, out bool invalid)
    {
        invalid = false;
        var numeric = Numeric.Match(normalizedText);
        if (numeric.Success)
        {
            var day = int.Parse(numeric.Groups[1].Value);
            var month = int.Parse(numeric.Groups[2].Value);
            var hasYear = numeric.Groups[3].Success;
            var year = hasYear ? int.Parse(numeric.Groups[3].Value) : today.Year;
            if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                invalid = true;
                return null;
            }
            var date = new DateOnly(year, month, day);
            // A day without a year means the next occurrence, since only upcoming appointments can be cancelled.
            if (!hasYear && date < today && day <= DateTime.DaysInMonth(year + 1, month))
                date = new DateOnly(year + 1, month, day);
            return date;
        }
        if (DayAfterTomorrow.IsMatch(normalizedText)) return today.AddDays(2);
        if (Tomorrow.IsMatch(normalizedText)) return today.AddDays(1);
        if (Today.IsMatch(normalizedText)) return today;
        return null;
    }
}
