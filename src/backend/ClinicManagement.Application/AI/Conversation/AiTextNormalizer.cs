using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClinicManagement.Application.AI.Conversation;

/// <summary>
/// Server-owned text normalization shared by classifiers, entity extraction and
/// booking validation. UI normalization is only cosmetic and is never trusted.
/// </summary>
public static class AiTextNormalizer
{
    private static readonly Regex ControlCharacters = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex TrailingEscape = new(@"(?:\\\s*)+$", RegexOptions.Compiled);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormC);
        normalized = ControlCharacters.Replace(normalized, " ");
        normalized = TrailingEscape.Replace(normalized, string.Empty);
        normalized = Whitespace.Replace(normalized, " ").Trim();
        return normalized;
    }

    public static string NormalizeForComparison(string? value)
    {
        var normalized = Normalize(value).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd');
    }

    public static string NormalizeClinicalReason(string? value)
    {
        var reason = Normalize(value);
        if (reason.Length == 0)
            return string.Empty;

        reason = Regex.Replace(reason, @"^(?:tôi|em|mình|cháu)\s+(?:đang\s+|bị\s+|có\s+)?", string.Empty, RegexOptions.IgnoreCase);
        reason = Regex.Replace(reason, @"\s+(?:thì\s+)?(?:nên|muốn|cho\s+tôi|hãy)\s+(?:chọn|tìm|xem)\s+(?:bác\s+sĩ|bs\.?|khoa|chuyên\s+khoa).*$", string.Empty, RegexOptions.IgnoreCase);
        reason = Regex.Replace(reason, @"\s+(?:bác\s+sĩ|bs\.?)\s+(?:nào|ai|gì|ở\s+đâu)\s*(?:không)?\s*$", string.Empty, RegexOptions.IgnoreCase);
        reason = Regex.Replace(reason, @"[\s,;:]+$", string.Empty).Trim();

        if (reason.Length == 0)
            return string.Empty;

        return char.ToUpperInvariant(reason[0]) + reason[1..];
    }
}
