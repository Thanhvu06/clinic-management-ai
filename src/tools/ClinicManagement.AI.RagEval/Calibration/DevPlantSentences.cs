using System.Text.RegularExpressions;

namespace ClinicManagement.AI.RagEval;

internal sealed record DevControl(DevAnswer Answer, int Sentence, string Text, string SourceId);

internal static partial class DevPlantSentences
{
    [GeneratedRegex(@"[.!?]+(?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Boundary();

    public static IEnumerable<DevControl> Find(DevAnswer answer, IReadOnlyList<ReplayPassage> passages)
    {
        if (answer.Mode != "Trích đoạn" || !answer.IsExtractive) yield break;
        var sourceSentences = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var passage in passages)
            foreach (var (_, sentence) in Split(passage.Text))
                sourceSentences.TryAdd(sentence, passage.Id);

        foreach (var (index, sentence) in Split(answer.Text))
            if (sourceSentences.TryGetValue(sentence, out var id))
                yield return new(answer, index, sentence, id);
    }

    public static IEnumerable<(int Index, string Text)> Split(string text)
    {
        var start = 0;
        var index = 0;
        foreach (Match end in Boundary().Matches(text))
        {
            var next = end.Index + end.Length;
            var sentence = text[start..next].Trim();
            start = next;
            if (sentence.Any(char.IsLetter))
                yield return (++index, sentence);
        }
    }
}
