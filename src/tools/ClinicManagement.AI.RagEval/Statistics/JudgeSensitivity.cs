using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record JudgeHumanComparison(int Pairs, int AiHigher, int Equal, int AiLower,
    double? MeanAi, double? MeanHuman, KappaResult Agreement);

public sealed record JudgeSensitivity(JudgeHumanComparison AllScored, JudgeHumanComparison VerifiedOnly)
{
    public string Pairing => "Each scored AI answer is paired separately with each available human score for the same question and side.";
    private const string SheetName = "Điểm AI";
    private sealed record Pair(int Ai, int Human, bool? Unverified);

    internal static bool CopyAiGrades(IXLWorksheet source, XLWorkbook master, IReadOnlyDictionary<string, int> masterRows)
    {
        if (!Enumerable.Range(0, 2).All(side =>
            source.Cell(2, JudgeRun.CitationFlagColumn + side).GetString() == JudgeRun.CitationFlagHeader(side))) return false;
        var target = master.Worksheets.TryGetWorksheet(SheetName, out var existing) ? existing : master.AddWorksheet(SheetName);
        target.Clear(XLClearOptions.Contents);
        string[] headers = ["STT", "AI điểm A", "AI điểm B", JudgeRun.CitationFlagHeader(0), JudgeRun.CitationFlagHeader(1)];
        for (var i = 0; i < headers.Length; i++) target.Cell(2, i + 1).Value = headers[i];
        foreach (var (stt, row) in BlindAnswerWorkbook.SttRows(source))
        {
            if (!masterRows.TryGetValue(stt, out var destinationRow)) continue;
            target.Cell(destinationRow, 1).Value = stt;
            for (var side = 0; side < 2; side++)
            {
                target.Cell(destinationRow, 2 + side).Value = source.Cell(row, 6 + side).Value;
                target.Cell(destinationRow, 4 + side).Value = source.Cell(row, JudgeRun.CitationFlagColumn + side).Value;
            }
        }
        return true;
    }

    internal static JudgeSensitivity? Read(XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(SheetName, out var ai)) return null;
        var human = workbook.Worksheet("Phiếu chấm");
        var humanRows = BlindAnswerWorkbook.SttRows(human);
        var pairs = new List<Pair>();
        foreach (var (stt, row) in BlindAnswerWorkbook.SttRows(ai))
        {
            if (!humanRows.TryGetValue(stt, out var humanRow)) continue;
            for (var side = 0; side < 2; side++)
            {
                var aiScore = ReadScore(ai.Cell(row, 2 + side));
                if (aiScore is null) continue;
                var flag = ai.Cell(row, 4 + side);
                bool? unverified = flag.DataType == XLDataType.Boolean ? flag.GetBoolean() : null;
                foreach (var start in new[] { 6, 11 })
                {
                    var humanScore = ReadScore(human.Cell(humanRow, start + side));
                    if (humanScore.HasValue) pairs.Add(new(aiScore.Value, humanScore.Value, unverified));
                }
            }
        }
        return new(Summarize(pairs), Summarize(pairs.Where(pair => pair.Unverified == false).ToArray()));
    }

    private static int? ReadScore(IXLCell cell) => cell.IsEmpty() ? null : GradeCells.Score(cell);

    private static JudgeHumanComparison Summarize(IReadOnlyList<Pair> pairs)
    {
        var ai = pairs.Select(pair => pair.Ai).ToArray();
        var human = pairs.Select(pair => pair.Human).ToArray();
        return new(pairs.Count, pairs.Count(pair => pair.Ai > pair.Human), pairs.Count(pair => pair.Ai == pair.Human),
            pairs.Count(pair => pair.Ai < pair.Human), pairs.Count == 0 ? null : ai.Average(), pairs.Count == 0 ? null : human.Average(),
            StatisticalTests.Kappa(ai, human));
    }

    internal async Task WriteAsync(TextWriter console)
    {
        await Write("Mọi ô có điểm", AllScored);
        await Write("Chỉ citation_unverified=false", VerifiedOnly);
        async Task Write(string label, JudgeHumanComparison group)
        {
            await console.WriteLineAsync($"AI–người ({label}) | N={group.Pairs} | cao/bằng/thấp={group.AiHigher}/{group.Equal}/{group.AiLower} | " +
                $"mean AI/người={StatisticsReport.Number(group.MeanAi)}/{StatisticsReport.Number(group.MeanHuman)} | " +
                $"kappa={StatisticsReport.Number(group.Agreement.Kappa)} | linear kappa={StatisticsReport.Number(group.Agreement.LinearWeightedKappa)}");
        }
    }
}
