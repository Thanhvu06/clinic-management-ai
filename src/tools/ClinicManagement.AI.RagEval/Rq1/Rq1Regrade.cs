using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record Rq1RegradeMapping(string Cl, string Stt);
public sealed record Rq1RegradeDifference(string Cl, string Stt, string Side, string Category,
    IReadOnlyList<string> Items, int? FirstScore, int? RepeatScore);
public sealed record Rq1RegradeComparison(int RequestedRatings, int MatchingScores, Rq1AgreementReport Agreement,
    IReadOnlyList<Rq1RegradeDifference> Differences, IReadOnlyList<string> UnavailableRatings);
public sealed record Rq1MechanicalIssue(string Rule, string Cl, string Side, string Item);
public sealed record Rq1RegradeReport(int MatchingContentRows, IReadOnlyList<Rq1MechanicalIssue> MechanicalIssues,
    Rq1RegradeComparison Stability, Rq1RegradeComparison Verification);
internal sealed class Rq1RegradeException(string message) : Exception(message);

internal static class Rq1Regrade
{
    internal static Rq1RegradeReport Analyze(XLWorkbook first, XLWorkbook repeat,
        IReadOnlyList<Rq1RegradeMapping> mapping, Func<XLWorkbook> openCodex)
    {
        var firstSheet = first.Worksheet(Rq1Workbook.Sheet);
        var repeatSheet = repeat.Worksheet(Rq1Workbook.Sheet);
        var firstRows = BlindAnswerWorkbook.SttRows(firstSheet);
        var repeatRows = BlindAnswerWorkbook.SttRows(repeatSheet);
        if (repeatRows.Count != mapping.Count || mapping.Select(item => item.Cl).Distinct(StringComparer.Ordinal).Count() != mapping.Count ||
            mapping.Select(item => item.Stt).Distinct(StringComparer.Ordinal).Count() != mapping.Count)
            throw new Rq1RegradeException("Repeat identifiers and mapping must be unique and have the same count.");

        foreach (var item in mapping)
        {
            if (!firstRows.TryGetValue(item.Stt, out var left) || !repeatRows.TryGetValue(item.Cl, out var right))
                throw new Rq1RegradeException($"Mapped row missing: {item.Cl}.");
            for (var column = 2; column <= 9; column++)
                if (!string.Equals(firstSheet.Cell(left, column).GetString(), repeatSheet.Cell(right, column).GetString(), StringComparison.Ordinal))
                    throw new Rq1RegradeException($"Content mismatch: {item.Cl}, column {column}.");
        }

        var issues = mapping.SelectMany(item => Rq1MechanicalAudit.Check(repeatSheet, repeatRows[item.Cl], item)).ToArray();
        var stability = Compare(firstSheet, repeatSheet, mapping, firstRows, repeatRows);
        using var codex = openCodex();
        var codexSheet = codex.Worksheet(Rq1Workbook.Sheet);
        var codexRows = BlindAnswerWorkbook.SttRows(codexSheet);
        if (firstRows.Count != 150 || codexRows.Count != 150 || !firstRows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(codexRows.Keys))
            throw new Rq1RegradeException("Verification requires the same 150 original identifiers in both locked workbooks.");
        var verificationMapping = firstRows.Keys.Select(stt => new Rq1RegradeMapping(stt, stt)).ToArray();
        var verification = Compare(firstSheet, codexSheet, verificationMapping, firstRows, codexRows);
        if (verification.Agreement.Overall.N != 300 || verification.MatchingScores != 299)
            throw new Rq1RegradeException($"Verification failed: got {verification.MatchingScores}/{verification.Agreement.Overall.N}, expected 299/300 matching scores. No report written.");
        return new(mapping.Count, issues, stability, verification);
    }

    internal static Rq1RegradeComparison Compare(IXLWorksheet first, IXLWorksheet second,
        IReadOnlyList<Rq1RegradeMapping> mapping, IReadOnlyDictionary<string, int> firstRows,
        IReadOnlyDictionary<string, int> secondRows)
    {
        var ratings = new List<Rq1RatingComparison>();
        var differences = new List<Rq1RegradeDifference>();
        var unavailable = new List<string>();
        var matching = 0;
        foreach (var item in mapping)
        {
            var left = firstRows[item.Stt];
            var right = secondRows[item.Cl];
            var category = QuestionCategories.Canonical(Rq1Workbook.Text(first.Cell(left, 2)));
            if (category != QuestionCategories.Canonical(Rq1Workbook.Text(second.Cell(right, 2))))
                throw new Rq1RegradeException($"Category mismatch for mapped identifier {item.Cl}.");
            var hasIdeas = Rq1Workbook.Text(first.Cell(left, Rq1Workbook.Ideas)).Length != 0 ||
                Rq1Workbook.Text(second.Cell(right, Rq1Workbook.Ideas)).Length != 0;
            foreach (var (side, start) in new[] { ("A", Rq1Workbook.ChecklistA), ("B", Rq1Workbook.ChecklistB) })
            {
                var firstValues = Rq1MechanicalAudit.Values(first, left, start);
                var secondValues = Rq1MechanicalAudit.Values(second, right, start);
                var firstChecklist = ReadChecklist(firstValues);
                var secondChecklist = ReadChecklist(secondValues);
                if (firstChecklist is not null && secondChecklist is not null)
                {
                    ratings.Add(new(category, hasIdeas, firstChecklist, secondChecklist,
                        Refused(firstValues[5]), Refused(secondValues[5])));
                    if (firstChecklist.Score == secondChecklist.Score) matching++;
                }
                else unavailable.Add($"{item.Cl}/{side}");

                var changed = Enumerable.Range(0, 6).Where(index =>
                {
                    if (index == 1 && firstChecklist is not null && secondChecklist is not null)
                        return Rq1Agreement.C2(firstChecklist, hasIdeas) != Rq1Agreement.C2(secondChecklist, hasIdeas);
                    return firstValues[index] != secondValues[index];
                }).Select(index => index == 5 ? "Từ chối" : $"C{index + 1}").ToArray();
                if (changed.Length != 0)
                    differences.Add(new(item.Cl, item.Stt, side, category, changed, firstChecklist?.Score, secondChecklist?.Score));
            }
        }
        return new(mapping.Count * 2, matching, Rq1Agreement.Compare(ratings), differences, unavailable);
    }

    internal static Rq1Checklist? ReadChecklist(IReadOnlyList<string> values) =>
        Enumerable.Range(0, 5).All(index => Rq1Checklist.Options[index].Contains(values[index]))
            ? new(values[0], values[1], values[2], values[3], values[4]) : null;

    private static bool? Refused(string value) => value switch
    {
        Rq1Checklist.Yes => true,
        Rq1Checklist.No => false,
        _ => null
    };
}
