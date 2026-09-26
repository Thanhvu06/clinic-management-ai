using System.Globalization;
using System.Text;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

public sealed class AiSafetyGuard : IAiSafetyGuard
{
    private static readonly string[] EmergencyPhrases =
    {
        "đau ngực", "đau thắt ngực", "khó thở nặng", "khó thở", "không thở được", "ngất xỉu", "bất tỉnh",
        "dấu hiệu đột quỵ", "méo miệng", "yếu liệt nửa người", "co giật", "chảy máu không cầm",
        "chảy máu nhiều", "sốc phản vệ", "dị ứng nặng", "tự tử", "muốn tự sát", "cấp cứu thai", "thai kỳ khẩn cấp",
        "trẻ tím tái", "trẻ khó thở", "tím tái", "trẻ co giật"
    };

    private static readonly string[] InjectionPhrases =
    {
        "bỏ qua quy tắc", "bỏ qua hướng dẫn", "ignore previous", "ignore all instructions",
        "system prompt", "developer mode", "xuất toàn bộ dữ liệu", "đóng vai bác sĩ", "kê thuốc"
    };

    public AiSafetyGuardResult Inspect(string? message)
    {
        var normalized = Normalize(message);
        if (string.IsNullOrWhiteSpace(normalized)) return new AiSafetyGuardResult();

        foreach (var phrase in EmergencyPhrases)
        {
            var normalizedPhrase = Normalize(phrase);
            var searchFrom = 0;
            while (searchFrom < normalized.Length)
            {
                var index = normalized.IndexOf(normalizedPhrase, searchFrom, StringComparison.Ordinal);
                if (index < 0) break;
                if (!IsNegated(normalized, index))
                    return new AiSafetyGuardResult { IsEmergency = true, MatchedCategory = phrase };
                searchFrom = index + normalizedPhrase.Length;
            }
        }

        if (InjectionPhrases.Any(x => normalized.Contains(Normalize(x), StringComparison.Ordinal)))
            return new AiSafetyGuardResult { IsPromptInjection = true, MatchedCategory = "prompt_injection" };

        return new AiSafetyGuardResult();
    }

    private static bool IsNegated(string text, int phraseIndex)
    {
        var start = phraseIndex;
        var hardBoundary = new[] { '.', '!', '?', ';', ':' };
        while (start > 0 && !hardBoundary.Contains(text[start - 1]))
            start--;

        var prefix = text[start..phraseIndex].Trim();
        var conjunction = prefix.LastIndexOf(" nhung ", StringComparison.Ordinal);
        if (conjunction >= 0)
            prefix = prefix[(conjunction + " nhung ".Length)..].Trim();

        // The cue must govern the phrase in the current clause. A distant
        // occurrence of "không" in a previous clause must not suppress a new
        // positive emergency report.
        return System.Text.RegularExpressions.Regex.IsMatch(
            prefix,
            @"(?:^|\s)(?:khong|chua|khong bi|khong phai|khong nghi)(?:\s+[\p{L}]+){0,4}\s*$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
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
