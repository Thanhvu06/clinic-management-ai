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

    private static readonly Regex Word = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Negation is intentionally narrow: at most two filler words can follow
    // "không/chưa". Conjunctions and punctuation are not in this allowlist, so
    // "không sốt và đau ngực" remains an emergency. Ambiguous wording fails safe.
    private static readonly HashSet<string> NegationFillers = new(StringComparer.Ordinal)
    {
        "bi", "co", "he", "tung", "phai", "thay", "cam", "con", "gap", "thuong", "bao", "gio"
    };

    private static readonly HashSet<char> ClauseBoundaries = new(".!?;:,\n\r".ToCharArray());

    public AiSafetyGuardResult Inspect(string? message)
    {
        var normalized = NormalizePreservingDiacritics(message);
        if (string.IsNullOrWhiteSpace(normalized)) return new AiSafetyGuardResult();

        var tokens = Tokenize(normalized);
        foreach (var phrase in EmergencyPhrases)
        {
            foreach (var match in FindPhraseMatches(tokens, phrase))
            {
                if (!IsNegated(normalized, tokens, match.StartTokenIndex))
                    return new AiSafetyGuardResult { IsEmergency = true, MatchedCategory = phrase };
            }
        }

        foreach (var phrase in InjectionPhrases)
        {
            foreach (var match in FindPhraseMatches(tokens, phrase))
            {
                if (!IsNegated(normalized, tokens, match.StartTokenIndex))
                    return new AiSafetyGuardResult { IsPromptInjection = true, MatchedCategory = "prompt_injection" };
            }
        }

        if (HasUnnegatedInjectionSequence(normalized))
            return new AiSafetyGuardResult { IsPromptInjection = true, MatchedCategory = "prompt_injection" };

        return new AiSafetyGuardResult();
    }

    private static bool HasUnnegatedInjectionSequence(string normalized)
    {
        // Equal texts still need both grammars: ASCII input cannot match the accented grammar.
        foreach (var (variant, pattern) in new[]
        {
            (normalized, InjectionSequence),
            (StripDiacritics(normalized), InjectionSequenceWithoutDiacritics)
        })
        {
            var tokens = Tokenize(variant);
            var matches = pattern
                .Matches(variant)
                .Cast<Match>();
            foreach (var match in matches)
            {
                var tokenIndex = Array.FindIndex(tokens, token => token.StartIndex >= match.Index);
                if (tokenIndex >= 0 && !IsNegated(variant, tokens, tokenIndex))
                    return true;
            }
        }

        return false;
    }

    private static bool IsNegated(string text, IReadOnlyList<TextToken> tokens, int phraseStartTokenIndex)
    {
        if (phraseStartTokenIndex <= 0 || phraseStartTokenIndex > tokens.Count)
            return false;

        var currentIndex = phraseStartTokenIndex - 1;
        var fillerCount = 0;
        while (currentIndex >= 0 && fillerCount < 2)
        {
            if (HasClauseBoundaryBetween(text, tokens[currentIndex].EndIndex, tokens[phraseStartTokenIndex].StartIndex))
                return false;

            var token = tokens[currentIndex].Comparison;
            if (token is "khong" or "chua")
                return true;

            if (!NegationFillers.Contains(token))
                return false;

            fillerCount++;
            currentIndex--;
        }

        return currentIndex >= 0 && tokens[currentIndex].Comparison is "khong" or "chua" &&
               !HasClauseBoundaryBetween(text, tokens[currentIndex].EndIndex, tokens[phraseStartTokenIndex].StartIndex);
    }

    private static bool HasClauseBoundaryBetween(string text, int fromExclusive, int toExclusive)
    {
        if (fromExclusive >= toExclusive || fromExclusive < 0 || toExclusive > text.Length)
            return false;

        for (var index = fromExclusive; index < toExclusive; index++)
        {
            if (ClauseBoundaries.Contains(text[index]))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<PhraseMatch> FindPhraseMatches(IReadOnlyList<TextToken> textTokens, string phrase)
    {
        var phraseTokens = Tokenize(NormalizePreservingDiacritics(phrase));
        if (phraseTokens.Length == 0 || textTokens.Count < phraseTokens.Length)
            return Array.Empty<PhraseMatch>();

        var matches = new List<PhraseMatch>();
        for (var start = 0; start <= textTokens.Count - phraseTokens.Length; start++)
        {
            var match = true;
            for (var offset = 0; offset < phraseTokens.Length; offset++)
            {
                if (!MatchesSyllable(textTokens[start + offset], phraseTokens[offset]))
                {
                    match = false;
                    break;
                }
            }

            if (match)
                matches.Add(new PhraseMatch(start));
        }

        return matches;
    }

    private static bool MatchesSyllable(TextToken actual, TextToken expected)
    {
        // A mixed-diacritic sentence is matched syllable by syllable. An accented
        // syllable must match exactly; an unaccented syllable accepts its folded
        // form. This preserves ngạt/ngắt/ngất distinctions whenever the user typed
        // the Vietnamese mark and intentionally treats fully unaccented "ngat"
        // as ambiguous and therefore fail-safe.
        return ContainsDiacritics(actual.Value)
            ? string.Equals(actual.Value, expected.Value, StringComparison.Ordinal)
            : string.Equals(actual.Comparison, expected.Comparison, StringComparison.Ordinal);
    }

    private static TextToken[] Tokenize(string value) => Word.Matches(value)
        .Cast<Match>()
        .Select(match =>
        {
            var token = match.Value.ToLowerInvariant();
            return new TextToken(token, StripDiacritics(token), match.Index, match.Index + match.Length);
        })
        .ToArray();

    private static bool ContainsDiacritics(string value) => value.Normalize(NormalizationForm.FormD)
        .Any(ch => CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark);

    private static string NormalizePreservingDiacritics(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Normalize(NormalizationForm.FormC).ToLowerInvariant();

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

    private sealed record TextToken(string Value, string Comparison, int StartIndex, int EndIndex);

    private sealed record PhraseMatch(int StartTokenIndex);
}
