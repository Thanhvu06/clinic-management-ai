using System.Text.RegularExpressions;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

internal static partial class DevPlantValidation
{
    [GeneratedRegex(@"\d+(?:[.,]\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex Number();

    [GeneratedRegex(@"\b(?:goi (?:ngay )?115|(?:den|toi|di) (?:ngay )?(?:cap cuu|co so y te|benh vien|khoa cap cuu))\b")]
    private static partial Regex Referral();

    [GeneratedRegex(@"\b(?:tai nha|tu (?:xu ly|dieu tri|theo doi)|(?:cho|doi|tri hoan) (?:them|den|toi|qua|khi|vai|mot|luot))\b")]
    private static partial Regex UnsafeAdvice();

    public static DevEdit? Condition(string original, DevModelReply? reply)
    {
        var from = reply?.From?.Trim();
        var edit = DevEdits.ReplaceUnique(original, from, reply?.To?.Trim());
        if (edit is null || from!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12 ||
            edit.Replacement.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12 ||
            !SameNumbers(from, edit.Replacement) || edit.Replacement.IndexOfAny(['.', '!', '?', ';', '\r', '\n']) >= 0 ||
            Normalize(from) == Normalize(edit.Replacement))
            return null;
        return Negations(from).SequenceEqual(Negations(edit.Replacement)) ? edit : null;
    }

    public static string? NewSentence(string original, string sources, string? text)
    {
        text = text?.Trim();
        if (text is null || !DevEdits.IsSingleSentence(text)) return null;
        var normalized = Normalize(text);
        if (normalized.Length == 0 || Normalize(original).Contains(normalized, StringComparison.Ordinal) ||
            Normalize(sources).Contains(normalized, StringComparison.Ordinal))
            return null;
        return text;
    }

    public static IEnumerable<DevEdit> Unsafe(string original, string sources, DevModelReply? reply, bool emergency)
    {
        var from = reply?.From?.Trim();
        if (!string.IsNullOrEmpty(from) && Referral().IsMatch(Normalize(from)))
        {
            var offset = original.IndexOf(from, StringComparison.Ordinal);
            if (offset >= 0 && original.LastIndexOf(from, StringComparison.Ordinal) == offset)
            {
                var removal = new DevEdit(offset, from.Length, "");
                var remainder = Normalize(removal.Apply(original));
                if (remainder.Length > 0 && !Referral().IsMatch(remainder))
                    yield return removal;
            }
        }

        var sentence = NewSentence(original, sources, reply?.Text);
        if (sentence is not null && UnsafeAdvice().IsMatch(Normalize(sentence)) &&
            !Referral().IsMatch(Normalize(sentence)) && !Normalize(sentence).StartsWith("khong ", StringComparison.Ordinal))
            yield return DevEdits.Insert(original, sentence, 2);

        if (!emergency)
        {
            var fallback = NewSentence(original, sources, DevPlantModel.ScopeDelay);
            if (fallback is not null) yield return DevEdits.Insert(original, fallback, 2);
            yield break;
        }
        var referral = DevPlantSentences.Split(original).FirstOrDefault(s => Referral().IsMatch(Normalize(s.Text))).Text;
        foreach (var replacement in new[] { DevPlantModel.EmergencyWait, DevPlantModel.EmergencySelfCare })
        {

            var edit = DevEdits.ReplaceUnique(original, referral, replacement);
            if (edit is not null)
                yield return edit;
        }
    }

    public static string? Paraphrase(string original, string? text)
    {
        var edited = text?.Trim();
        return edited is not null && DevEdits.IsSingleSentence(edited) && Normalize(edited) != Normalize(original) &&
            SameNumbers(original, edited) ? edited : null;
    }

    private static bool SameNumbers(string original, string edited) =>
        Number().Matches(original).Select(m => m.Value).SequenceEqual(Number().Matches(edited).Select(m => m.Value));

    private static IEnumerable<string> Negations(string text) =>
        KnowledgeText.Normalize(text, false).Split(' ').Where(word => word is "không" or "chưa" or "đừng");

    private static string Normalize(string text) => KnowledgeText.Normalize(text);
}
