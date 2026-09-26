using System.Globalization;
using System.Text;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

public sealed class AiSafetyGuard : IAiSafetyGuard
{
    private static readonly string[] EmergencyPhrases =
    {
        "đau ngực", "đau thắt ngực", "khó thở nặng", "không thở được", "ngất xỉu", "bất tỉnh",
        "dấu hiệu đột quỵ", "méo miệng", "yếu liệt nửa người", "co giật", "chảy máu không cầm",
        "chảy máu nhiều", "sốc phản vệ", "dị ứng nặng", "tự tử", "muốn tự sát", "cấp cứu thai", "thai kỳ khẩn cấp",
        "trẻ tím tái", "trẻ khó thở", "trẻ co giật"
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

        if (InjectionPhrases.Any(x => normalized.Contains(Normalize(x), StringComparison.Ordinal)))
            return new AiSafetyGuardResult { IsPromptInjection = true, MatchedCategory = "prompt_injection" };

        foreach (var phrase in EmergencyPhrases)
        {
            var index = normalized.IndexOf(Normalize(phrase), StringComparison.Ordinal);
            if (index < 0 || IsNegated(normalized, index)) continue;
            return new AiSafetyGuardResult { IsEmergency = true, MatchedCategory = phrase };
        }

        return new AiSafetyGuardResult();
    }

    private static bool IsNegated(string text, int phraseIndex)
    {
        var start = Math.Max(0, phraseIndex - 24);
        var prefix = text[start..phraseIndex];
        return prefix.Contains("khong", StringComparison.Ordinal) ||
               prefix.Contains("chua", StringComparison.Ordinal) ||
               prefix.Contains("khong bi", StringComparison.Ordinal);
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
