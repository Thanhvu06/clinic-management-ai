using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Common;

namespace ClinicManagement.Infrastructure.AI.Tools;

internal sealed record PatientReadFilters(
    DateOnly? FromDate, DateOnly? ToDate, bool Latest, string? Status, string? Service)
{
    private const string DatePattern = @"\d{1,2}/\d{1,2}/\d{4}|hom nay|hom qua|thang (?:nay|truoc|\d{1,2})";
    private static readonly string[] DateFormats = ["d/M/yyyy", "dd/MM/yyyy"];

    internal bool Active => FromDate.HasValue || ToDate.HasValue || Latest || Status != null || Service != null;
    internal int Limit(int pageSize) => Latest ? 1 : pageSize;
    internal int Offset(int page, int pageSize) => Latest ? 0 : (page - 1) * pageSize;

    internal static PatientReadFilters FromText(string text, string tool, DateOnly today, out bool invalidDate)
    {
        var range = Regex.Match(text, $@"\btu\s+(?:ngay\s+)?({DatePattern})\s+den\s+(?:ngay\s+)?({DatePattern})\b");
        var single = Regex.Match(text, $@"\b(?:{DatePattern})\b");
        var start = ParsePeriod(range.Success ? range.Groups[1].Value : single.Value, today);
        var end = range.Success ? ParsePeriod(range.Groups[2].Value, today) : start;
        invalidDate = single.Success && (!start.From.HasValue || !end.To.HasValue || start.From > end.To);
        var status = tool == "patient.get_my_bills"
            ? Regex.IsMatch(text, @"\b(?:chua|cho) thanh toan\b") ? "Unpaid"
                : Regex.IsMatch(text, @"\bda thanh toan\b") ? "Paid" : null
            : null;
        string? service = null;
        if (tool == "patient.get_my_diagnostic_results")
        {
            var codes = DiagnosticServiceAliases.Resolve(text);
            var category = Regex.Match(text, @"\b(?:sieu am|x[\s-]+quang)\b");
            var serviceText = Regex.Replace(text, $@"\b(?:(?:tu|den)\s+)?(?:ngay\s+)?(?:{DatePattern})\b", " ");
            serviceText = Regex.Replace(serviceText, @"\b(?:xem|tra cuu|ket qua|can lam sang|cua toi|cua minh|gan nhat|moi nhat|nhe|nha|a)\b", " ").Trim();
            serviceText = Regex.Replace(serviceText, @"\s+", " ").Trim(' ', '.', '?', '!', ',');
            const string testPrefix = "xet nghiem ";
            if (serviceText.StartsWith(testPrefix, StringComparison.Ordinal) &&
                DiagnosticServiceAliases.Resolve(serviceText[testPrefix.Length..]).SetEquals(codes))
                serviceText = serviceText[testPrefix.Length..];
            service = category.Success && serviceText == category.Value ? category.Value
                : serviceText is "" or "xet nghiem" ? null : serviceText;
        }
        return new(start.From, end.To, Regex.IsMatch(text, @"\b(?:gan nhat|moi nhat)\b"), status, service);
    }

    private static (DateOnly? From, DateOnly? To) ParsePeriod(string value, DateOnly today)
    {
        if (DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return (date, date);
        if (value == "hom nay") return (today, today);
        if (value == "hom qua") return (today.AddDays(-1), today.AddDays(-1));
        if (!value.StartsWith("thang ")) return default;
        var first = new DateOnly(today.Year, today.Month, 1);
        if (value == "thang truoc") first = first.AddMonths(-1);
        else if (value != "thang nay")
        {
            if (!int.TryParse(value[6..], out var month) || month is < 1 or > 12) return default;
            first = new DateOnly(today.Year, month, 1);
        }
        return (first, first.AddMonths(1).AddDays(-1));
    }

    internal string ArgumentsJson() => JsonSerializer.Serialize(new
    {
        fromDate = FromDate, toDate = ToDate, latest = Latest ? (bool?)true : null, status = Status, service = Service
    }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

    internal DateTime? UtcStart(IDateTimeProvider clock) => FromDate is { } date
        ? clock.ConvertVietnamToUtc(date.ToDateTime(TimeOnly.MinValue)) : null;
    internal DateTime? UtcEnd(IDateTimeProvider clock) => ToDate is { } date && date < DateOnly.MaxValue
        ? clock.ConvertVietnamToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue)) : null;

    internal string Summary(int count, string label)
    {
        var payment = Status == "Unpaid" ? " chưa thanh toán" : Status == "Paid" ? " đã thanh toán" : "";
        var service = Service == null ? "" : $" cho dịch vụ {ServiceDisplay(Service)}";
        var latest = Latest ? " gần nhất" : "";
        var dates = (FromDate, ToDate) switch
        {
            ({ } from, { } to) when from == to => $" ngày {from:dd/MM/yyyy}",
            ({ } from, { } to) => $" từ {from:dd/MM/yyyy} đến {to:dd/MM/yyyy}",
            ({ } from, null) => $" từ {from:dd/MM/yyyy}",
            (null, { } to) => $" đến {to:dd/MM/yyyy}",
            _ => ""
        };
        var prefix = count == 0 && dates.Length > 0 ? "Không có" : $"Có {count}";
        return $"{prefix} {label}{payment}{service}{dates}{latest}.";
    }

    private static string ServiceDisplay(string service) => service switch
    {
        "cong thuc mau" => "công thức máu",
        "xet nghiem mau tong quat" => "xét nghiệm máu tổng quát",
        "duong huyet" => "đường huyết",
        "chuc nang gan" => "chức năng gan",
        "men gan" => "men gan",
        "chuc nang than" => "chức năng thận",
        "dien tim" => "điện tim",
        "x quang phoi" or "x-quang phoi" => "X-quang phổi",
        "sieu am bung" => "siêu âm bụng",
        "sieu am" => "siêu âm",
        "x quang" or "x-quang" => "X-quang",
        _ => service
    };
}
