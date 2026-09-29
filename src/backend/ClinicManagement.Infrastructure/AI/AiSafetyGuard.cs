using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

public sealed class AiSafetyGuard : IAiSafetyGuard
{
    private static readonly string[] EmergencyPhrases =
    {
        "đau ngực", "đau thắt ngực", "khó thở nặng", "khó thở", "không thở được", "không cầm máu", "ngất xỉu", "ngất", "bất tỉnh",
        "dấu hiệu đột quỵ", "méo miệng", "yếu liệt nửa người", "co giật", "chảy máu không cầm",
        "chảy máu nhiều", "sốc phản vệ", "dị ứng nặng", "tự tử", "muốn tự sát", "cấp cứu thai", "thai kỳ khẩn cấp",
        "trẻ tím tái", "trẻ khó thở", "tím tái", "trẻ co giật", "uống quá liều thuốc", "quá liều thuốc",
        "có ý định tự làm hại bản thân", "muốn tự làm hại bản thân"
    };

    private static readonly string[] InjectionPhrases =
    {
        "bỏ qua quy tắc", "bỏ qua hướng dẫn", "ignore previous", "ignore all instructions",
        "system prompt", "developer mode", "xuất toàn bộ dữ liệu", "đóng vai bác sĩ",
        "giả làm admin", "in hồ sơ bệnh nhân khác", "gọi execute_confirmed_action", "đổi role",
        "dùng facility khác", "thực thi không xác nhận"
    };

    private static readonly Regex InjectionSequence = new(
        @"(?:^|[^\p{L}\p{N}])(?:bỏ qua|vô hiệu hóa|phớt lờ|bất chấp|vượt qua)(?:\s+[\p{L}]+){0,3}\s+(?:kiểm soát|quy tắc|hướng dẫn|giới hạn|quyền|quyền truy cập|kiểm tra|xác nhận)(?![\p{L}\p{N}])|(?:^|[^\p{L}\p{N}])(?:gọi|chạy|thực thi)\s+(?:tool|công cụ)(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex InjectionSequenceWithoutDiacritics = new(
        @"(?:^|[^\p{L}\p{N}])(?:bo qua|vo hieu hoa|phot lo|bat chap|vuot qua)(?:\s+[\p{L}]+){0,3}\s+(?:kiem soat|quy tac|huong dan|gioi han|quyen|quyen truy cap|kiem tra|xac nhan)(?![\p{L}\p{N}])|(?:^|[^\p{L}\p{N}])(?:goi|chay|thuc thi)\s+(?:tool|cong cu)(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NegationAtScopeEnd = new(
        @"(?:^|\s)(?:không|chưa)(?:\s+(?:bị|có|hề|từng|phải)){0,2}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NegationAtScopeEndWithoutDiacritics = new(
        @"(?:^|\s)(?:khong|chua)(?:\s+(?:bi|co|he|tung|phai)){0,2}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public AiSafetyGuardResult Inspect(string? message)
    {
        var normalized = NormalizeForMatching(message, out var hasDiacritics);
        if (string.IsNullOrWhiteSpace(normalized)) return new AiSafetyGuardResult();

        foreach (var phrase in EmergencyPhrases)
        {
            var normalizedPhrase = hasDiacritics
                ? NormalizePreservingDiacritics(phrase)
                : StripDiacritics(NormalizePreservingDiacritics(phrase));
            var searchFrom = 0;
            while (searchFrom < normalized.Length)
            {
                var index = IndexOfWholePhrase(normalized, normalizedPhrase, searchFrom);
                if (index < 0) break;
                if (!IsNegated(normalized, index, hasDiacritics))
                    return new AiSafetyGuardResult { IsEmergency = true, MatchedCategory = phrase };
                searchFrom = index + normalizedPhrase.Length;
            }
        }

        if (InjectionPhrases.Any(x => ContainsUnnegated(
                normalized,
                hasDiacritics ? NormalizePreservingDiacritics(x) : StripDiacritics(NormalizePreservingDiacritics(x)),
                hasDiacritics)) ||
            (hasDiacritics ? InjectionSequence : InjectionSequenceWithoutDiacritics)
                .Matches(normalized)
                .Cast<Match>()
                .Any(match => !IsNegated(normalized, match.Index + LeadingBoundaryLength(normalized, match.Index), hasDiacritics)))
            return new AiSafetyGuardResult { IsPromptInjection = true, MatchedCategory = "prompt_injection" };

        return new AiSafetyGuardResult();
    }

    private static bool IsNegated(string text, int phraseIndex, bool hasDiacritics)
    {
        if (phraseIndex <= 0) return false;

        var start = phraseIndex;
        while (start > 0 && !IsClauseBoundary(text[start - 1]))
            start--;

        var prefix = text[start..phraseIndex].Trim();
        if (prefix.Length == 0) return false;

        return (hasDiacritics ? NegationAtScopeEnd : NegationAtScopeEndWithoutDiacritics).IsMatch(prefix);
    }

    private static bool ContainsUnnegated(string text, string phrase, bool hasDiacritics)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        var searchFrom = 0;
        while (searchFrom < text.Length)
        {
            var index = IndexOfWholePhrase(text, phrase, searchFrom);
            if (index < 0) return false;
            if (!IsNegated(text, index, hasDiacritics)) return true;
            searchFrom = index + phrase.Length;
        }

        return false;
    }

    private static int IndexOfWholePhrase(string text, string phrase, int startIndex)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return -1;
        var searchFrom = Math.Max(0, startIndex);
        while (searchFrom < text.Length)
        {
            var index = text.IndexOf(phrase, searchFrom, StringComparison.Ordinal);
            if (index < 0) return -1;
            var beforeIsWord = index > 0 && IsWordChar(text[index - 1]);
            var end = index + phrase.Length;
            var afterIsWord = end < text.Length && IsWordChar(text[end]);
            if (!beforeIsWord && !afterIsWord) return index;
            searchFrom = end;
        }

        return -1;
    }

    private static bool IsWordChar(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static bool IsClauseBoundary(char value) => value is '.' or '!' or '?' or ';' or ':' or ',' or '\n' or '\r';

    private static int LeadingBoundaryLength(string value, int matchIndex) =>
        matchIndex < value.Length && !IsWordChar(value[matchIndex]) ? 1 : 0;

    private static bool ContainsDiacritics(string value) => value.Normalize(NormalizationForm.FormD)
        .Any(ch => CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark);

    private static string NormalizeForMatching(string? value, out bool hasDiacritics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            hasDiacritics = false;
            return string.Empty;
        }

        var preserved = NormalizePreservingDiacritics(value);
        hasDiacritics = ContainsDiacritics(preserved);
        return hasDiacritics ? preserved : StripDiacritics(preserved);
    }

    private static string NormalizePreservingDiacritics(string value) =>
        value.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    private static string StripDiacritics(string value)
    {
        var formD = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Replace("đ", "d");
    }
}
