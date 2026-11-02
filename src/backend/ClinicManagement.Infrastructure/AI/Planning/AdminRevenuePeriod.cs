using System.Globalization;
using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI.Planning;

internal static class AdminRevenuePeriod
{
    public static bool TryResolve(string period, DateOnly today, out DateOnly from, out DateOnly to)
    {
        from = to = today;
        var first = new DateOnly(today.Year, today.Month, 1);
        switch (period)
        {
            case "today": return true;
            case "yesterday": from = to = today.AddDays(-1); return true;
            case "this_week":
                from = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
                to = from.AddDays(6);
                return true;
            case "this_month": from = first; to = first.AddMonths(1).AddDays(-1); return true;
            case "last_month": from = first.AddMonths(-1); to = first.AddDays(-1); return true;
        }
        var days = Regex.Match(period, @"^last_(\d+)_days$");
        if (days.Success && int.TryParse(days.Groups[1].Value, out var count) && count is >= 1 and <= 366)
        {
            from = today.AddDays(1 - count);
            return true;
        }
        var range = period.Split(':');
        return range.Length == 3 && range[0] == "range" && TryDate(range[1], out from) && TryDate(range[2], out to) &&
               to >= from && to.DayNumber - from.DayNumber <= 365;
    }

    public static bool TryParseQuestion(string text, out string period)
    {
        period = "today";
        var dates = Regex.Matches(text, @"\b(?:\d{1,2}/\d{1,2}/\d{4}|\d{4}-\d{2}-\d{2})\b");
        if (dates.Count > 0)
        {
            if (dates.Count > 2 || !TryDate(dates[0].Value, out var from) || !TryDate(dates[^1].Value, out var to)) return false;
            period = $"range:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
            return to >= from && to.DayNumber - from.DayNumber <= 365;
        }
        var days = Regex.Match(text, @"\b(\d+)\s+ngay\b");
        if (days.Success)
        {
            if (!int.TryParse(days.Groups[1].Value, out var count) || count is < 1 or > 366) return false;
            period = $"last_{count}_days";
        }
        else if (text.Contains("hom qua")) period = "yesterday";
        else if (text.Contains("tuan nay")) period = "this_week";
        else if (text.Contains("thang truoc")) period = "last_month";
        else if (text.Contains("thang nay")) period = "this_month";
        return true;
    }

    public static string Label(DateOnly from, DateOnly to, string period) => from != to
        ? $"từ {from:dd/MM/yyyy} đến {to:dd/MM/yyyy}"
        : period switch
        {
            "today" => $"hôm nay ({from:dd/MM/yyyy})",
            "yesterday" => $"hôm qua ({from:dd/MM/yyyy})",
            _ => $"ngày {from:dd/MM/yyyy}"
        };

    private static bool TryDate(string value, out DateOnly date) => DateOnly.TryParseExact(value,
        ["d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
