using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record ExcerptMismatch(string Stt, int? StoredLength, int? ReplayedLength, int? FirstDifferentCharacter);
public sealed record ExcerptRouteCount(string Route, int Matched, int Mismatched);
public sealed record ExcerptVerificationResult(IReadOnlyList<ExcerptRouteCount> Counts, IReadOnlyList<ExcerptMismatch> Mismatches)
{
    public int Matched => Counts.Sum(row => row.Matched);
    public int Mismatched => Mismatches.Count;
    public bool IsComplete => Matched == ExcerptReplayFile.RequiredRows && Mismatched == 0;
}

public static partial class ExcerptReplayVerification
{
    public static ExcerptMismatch? Compare(string stt, string stored, string replayed)
    {
        var original = Characters(stored);
        var actual = Characters(replayed);
        var length = Math.Min(original.Length, actual.Length);
        var position = 0;
        while (position < length && original[position] == actual[position]) position++;
        return position == length && original.Length == actual.Length
            ? null
            : new(stt, original.Length, actual.Length, position + 1);
    }

    public static ExcerptVerificationResult Verify(IXLWorkbook master, IReadOnlyDictionary<string, ExcerptReplayRow> replay)
    {
        var grades = master.Worksheet("Phiếu chấm");
        var keys = master.Worksheet("Khóa");
        var keyRows = BlindAnswerWorkbook.SttRows(keys);
        var gradeRows = BlindAnswerWorkbook.SttRows(grades);
        var rows = gradeRows.Keys.Union(replay.Keys, StringComparer.Ordinal).Union(keyRows.Keys, StringComparer.Ordinal);
        var counts = new Dictionary<string, (int Matched, int Mismatched)>(StringComparer.Ordinal);
        var differences = new List<ExcerptMismatch>();
        foreach (var stt in rows)
        {
            replay.TryGetValue(stt, out var entry);
            var stored = ExtractiveAnswer(stt);
            var route = entry?.Route.ToString() ?? "MissingReplayRow";
            var mismatch = entry is null || stored is null
                ? new ExcerptMismatch(stt, stored is null ? null : Characters(stored).Length,
                    entry is null ? null : Characters(entry.Answer).Length, null)
                : Compare(stt, stored, entry.Answer);
            var count = counts.GetValueOrDefault(route);
            if (mismatch is null) count.Matched++;
            else { count.Mismatched++; differences.Add(mismatch); }
            counts[route] = count;
        }
        return new(counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ExcerptRouteCount(pair.Key, pair.Value.Matched, pair.Value.Mismatched)).ToArray(), differences);

        string? ExtractiveAnswer(string stt)
        {
            if (!keyRows.TryGetValue(stt, out var keyRow) || !gradeRows.TryGetValue(stt, out var gradeRow))
                return null;
            var column = keys.Cell(keyRow, 2).GetString().Trim().Normalize(NormalizationForm.FormC) switch
            {
                "LLM" => 5,
                "Trích đoạn" => 4,
                _ => 0
            };
            return column == 0 ? null : grades.Cell(gradeRow, column).GetString();
        }
    }

    private static Rune[] Characters(string text) =>
        Whitespace().Replace(text.Normalize(NormalizationForm.FormC).Trim(), " ").EnumerateRunes().ToArray();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
