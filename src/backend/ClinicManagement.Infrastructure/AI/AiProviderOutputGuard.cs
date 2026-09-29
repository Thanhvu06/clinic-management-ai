using System.Text;
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
    // A condition alone ("Khi bạn nhập viện, bạn bị đau ngực") still asserts the symptom; guidance needs an action.
    private static readonly Regex ActionPhrase = new(
        @"\b(?:hãy|nên|cần|phải|thì|liên\s+hệ|gọi\s+(?:115|cấp\s+cứu|xe)|(?:đến|tới|đi)\s+(?:cấp\s+cứu|bệnh\s+viện|cơ\s+sở))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ListItem = new(
        @"^(?:[-*•]|\d+[.)])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Block(string Message, string Code, bool Emergency);

    public static Block? Inspect(AiActorRole role, params string?[] texts)
    {
        var guard = new AiSafetyGuard();
        foreach (var text in texts.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            foreach (var sentence in SplitSentences(text!))
            {
                var offset = 0;
                foreach (var clause in Regex.Split(sentence, @"(?<=,)"))
                {
                    var safety = guard.Inspect(clause);
                    // A condition scopes over the whole sentence, so a clause is judged with everything before it,
                    // and the action that makes it guidance may come anywhere in the sentence.
                    if (safety.IsEmergency && HasAssertedEmergency(sentence, offset, clause, safety.MatchedCategory))
                        return new("Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi 115 hoặc đến cơ sở cấp cứu gần nhất. Không chờ phản hồi qua trò chuyện.", "PROVIDER_OUTPUT_EMERGENCY", true);
                    offset += clause.Length;
                }
            }
        }
        if (texts.Any(guard.ContainsPromptInjection))
            return new("Nội dung trả lời không đáp ứng quy tắc an toàn. Vui lòng chọn thao tác có sẵn hoặc liên hệ cơ sở.", "PROVIDER_OUTPUT_UNSAFE", false);
        if (texts.Any(x => AiMedicalScopeGuard.IsUnsafeProviderAdvice(role, x)))
            return new("Tôi không thể kê đơn hoặc hướng dẫn liều dùng qua cuộc trò chuyện. Vui lòng trao đổi với bác sĩ hoặc xem toa thuốc đã được cơ sở xác nhận.", "MEDICAL_PRESCRIPTION_OUT_OF_SCOPE", false);
        return null;
    }

    /// <summary>
    /// Splits on . ! ? ; and line breaks, except that list items under a header line ending in ":"
    /// that carries a condition stay attached to it, so the condition covers the whole list.
    /// </summary>
    private static IEnumerable<string> SplitSentences(string text)
    {
        var block = new StringBuilder();
        foreach (var rawLine in Regex.Split(text, @"\r?\n"))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                foreach (var done in Flush(block)) yield return done;
                continue;
            }
            if (block.Length > 0 && ListItem.IsMatch(line))
            {
                block.Append(' ').Append(ListItem.Replace(line, string.Empty));
                continue;
            }
            foreach (var done in Flush(block)) yield return done;
            if (line.EndsWith(':') && ConditionalWarning.IsMatch(AiTextNormalizer.Normalize(line).ToLowerInvariant()))
            {
                block.Append(line);
                continue;
            }
            foreach (var part in Regex.Split(ListItem.Replace(line, string.Empty), @"[.!?;]+"))
                if (!string.IsNullOrWhiteSpace(part)) yield return part;
        }
        foreach (var done in Flush(block)) yield return done;
    }

    private static IEnumerable<string> Flush(StringBuilder block)
    {
        if (block.Length == 0) yield break;
        var text = block.ToString();
        block.Clear();
        yield return text;
    }

    private static bool HasAssertedEmergency(string sentence, int clauseStart, string text, string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return true;
        if (!ActionPhrase.IsMatch(AiTextNormalizer.Normalize(sentence).ToLowerInvariant())) return true;
        var context = AiTextNormalizer.Normalize(sentence[..clauseStart]).ToLowerInvariant();
        var normalized = AiTextNormalizer.Normalize(text).ToLowerInvariant();
        var phrase = AiTextNormalizer.Normalize(category).ToLowerInvariant();
        var searchFrom = 0;
        while (searchFrom < normalized.Length)
        {
            var phraseIndex = normalized.IndexOf(phrase, searchFrom, StringComparison.Ordinal);
            if (phraseIndex < 0) break;
            var prefix = context + " " + normalized[..phraseIndex];
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
