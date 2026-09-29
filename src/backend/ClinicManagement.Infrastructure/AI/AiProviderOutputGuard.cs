using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>Checks untrusted free text only, never backend-grounded clinical records.</summary>
public static class AiProviderOutputGuard
{
    private static readonly Regex ConditionalWarning = new(
        @"\b(?:nếu|khi|trong\s+trường\s+hợp|trường\s+hợp|có\s+dấu\s+hiệu|những\s+dấu\s+hiệu|dấu\s+hiệu\s+cảnh\s+báo|hoặc\s+bất\s+kỳ|bất\s+kỳ|nên\s+đi\s+cấp\s+cứu\s+khi)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CurrentState = new(
        @"\b(?:(?:bạn|người\s+bệnh|bệnh\s+nhân)\s+(?:hiện\s+)?đang|hiện\s+tại|lúc\s+này)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public sealed record Block(string Message, string Code, bool Emergency);

    public static Block? Inspect(AiActorRole role, params string?[] texts)
    {
        var guard = new AiSafetyGuard();
        foreach (var text in texts.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            foreach (var clause in Regex.Split(text!, @"[.!?;,\r\n]+"))
            {
                var safety = guard.Inspect(clause);
                if (safety.IsEmergency && HasAssertedEmergency(clause, safety.MatchedCategory))
                    return new("Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi 115 hoặc đến cơ sở cấp cứu gần nhất. Không chờ phản hồi qua trò chuyện.", "PROVIDER_OUTPUT_EMERGENCY", true);
            }
        }
        if (texts.Any(guard.ContainsPromptInjection))
            return new("Nội dung trả lời không đáp ứng quy tắc an toàn. Vui lòng chọn thao tác có sẵn hoặc liên hệ cơ sở.", "PROVIDER_OUTPUT_UNSAFE", false);
        if (texts.Any(x => AiMedicalScopeGuard.IsUnsafeProviderAdvice(role, x)))
            return new("Tôi không thể kê đơn hoặc hướng dẫn liều dùng qua cuộc trò chuyện. Vui lòng trao đổi với bác sĩ hoặc xem toa thuốc đã được cơ sở xác nhận.", "MEDICAL_PRESCRIPTION_OUT_OF_SCOPE", false);
        return null;
    }

    private static bool HasAssertedEmergency(string text, string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return true;
        var normalized = AiTextNormalizer.Normalize(text).ToLowerInvariant();
        var phrase = AiTextNormalizer.Normalize(category).ToLowerInvariant();
        var searchFrom = 0;
        while (searchFrom < normalized.Length)
        {
            var phraseIndex = normalized.IndexOf(phrase, searchFrom, StringComparison.Ordinal);
            if (phraseIndex < 0) break;
            var sentenceStart = normalized.LastIndexOfAny(new[] { '.', '!', '?', ';', '\n', '\r' }, Math.Max(0, phraseIndex - 1));
            var prefix = normalized[(sentenceStart + 1)..phraseIndex];
            var conditions = ConditionalWarning.Matches(prefix);
            if (conditions.Count == 0)
                return true;
            var lastCondition = conditions[^1];
            var afterCondition = prefix[(lastCondition.Index + lastCondition.Length)..];
            if (CurrentState.IsMatch(afterCondition))
                return true;
            searchFrom = phraseIndex + phrase.Length;
        }
        return false;
    }
}
