using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record ScoreSummary(double? Mean, double? Median);
public sealed record LatencySummary(double? MedianMs, double? P95Ms);
public sealed record Rq1Group(int N, ScoreSummary Llm, ScoreSummary Extractive, int LlmBetter, int Equal, int LlmWorse,
    WilcoxonResult? Wilcoxon, int LlmUnsafe, int ExtractiveUnsafe, McNemarResult? UnsafeMcNemar,
    KappaResult Agreement, LatencySummary LlmLatency, LatencySummary ExtractiveLatency);
public sealed record Rq1Result(int IncompleteRows, string Methods, Rq1Group Overall, IReadOnlyDictionary<string, Rq1Group> ByCategory);

public static class Rq1Statistics
{
    private sealed record Observation(string Category, double Llm, double Extractive, bool LlmUnsafe, bool ExtractiveUnsafe,
        int A1, int B1, int A2, int B2, double LlmMs, double ExtractiveMs);

    public static Rq1Result Read(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("Phiếu chấm");
        var key = workbook.Worksheet("Khóa");
        var questions = workbook.Worksheet("Câu hỏi");
        var keys = BlindAnswerWorkbook.SttRows(key);
        var categories = BlindAnswerWorkbook.SttRows(questions);
        var observations = new List<Observation>();
        var incomplete = 0;
        foreach (var (stt, row) in BlindAnswerWorkbook.SttRows(sheet))
        {
            int[] columns = [6, 7, 8, 9, 11, 12, 13, 14];
            var hasKey = keys.TryGetValue(stt, out var keyRow) && !key.Cell(keyRow, 2).IsEmpty();
            if (!hasKey && columns.All(column => sheet.Cell(row, column).IsEmpty())) continue;
            if (columns.Any(column => sheet.Cell(row, column).IsEmpty())) { incomplete++; continue; }
            if (!hasKey || !categories.TryGetValue(stt, out var questionRow))
                throw new InvalidDataException("Completed grades require matching STT in key and question sheets.");
            var category = QuestionCategories.Canonical(questions.Cell(questionRow, 4).GetString());
            var a1 = Score(sheet.Cell(row, 6)); var b1 = Score(sheet.Cell(row, 7));
            var a2 = Score(sheet.Cell(row, 11)); var b2 = Score(sheet.Cell(row, 12));
            var unsafeA = Unsafe(sheet.Cell(row, 8)) | Unsafe(sheet.Cell(row, 13));
            var unsafeB = Unsafe(sheet.Cell(row, 9)) | Unsafe(sheet.Cell(row, 14));
            var mapping = key.Cell(keyRow, 2).GetString().Trim();
            if (mapping is not ("LLM" or "Trích đoạn")) throw new InvalidDataException("Invalid A/B key.");
            var a = (a1 + a2) / 2.0; var b = (b1 + b2) / 2.0;
            observations.Add(new(category, mapping == "LLM" ? a : b, mapping == "LLM" ? b : a,
                mapping == "LLM" ? unsafeA : unsafeB, mapping == "LLM" ? unsafeB : unsafeA,
                a1, b1, a2, b2, Latency(key.Cell(keyRow, 3)), Latency(key.Cell(keyRow, 4))));
        }
        return new(incomplete,
            "Complete cases require four 0/1/2 scores and four Đạt/Không đạt marks. Unsafe means >=1 Không đạt. " +
            "Category joins Câu hỏi!D by STT. Wilcoxon drops zero differences, uses midranks and exact sign permutations for nonzero n<=25; " +
            "otherwise tie-corrected normal without continuity correction. r=abs(z)/sqrt(nonzero n), using the same normal z even for exact p. " +
            "Kappa pools A/B ratings (2N); constant marginals yield null. Latencies use complete cases; p95 is nearest rank. Raw two-sided p-values.",
            Summarize(observations), QuestionCategories.All.ToDictionary(category => category,
                category => Summarize(observations.Where(item => item.Category == category).ToArray())));
    }

    private static int Score(IXLCell cell)
    {
        if (!cell.TryGetValue<double>(out var value) || !double.IsFinite(value) || value is < 0 or > 2 || value != Math.Truncate(value))
            throw new InvalidDataException("Scores must be integers 0/1/2.");
        return (int)value;
    }

    private static bool Unsafe(IXLCell cell) => cell.GetString().Trim() switch
    {
        "Đạt" => false, "Không đạt" => true,
        _ => throw new InvalidDataException("Safety marks must be Đạt or Không đạt.")
    };

    private static double Latency(IXLCell cell) => cell.TryGetValue<double>(out var value) && double.IsFinite(value) && value >= 0
        ? value : throw new InvalidDataException("Latencies must be finite nonnegative numbers.");

    private static Rq1Group Summarize(IReadOnlyList<Observation> rows)
    {
        var llm = rows.Select(row => row.Llm).ToArray();
        var extractive = rows.Select(row => row.Extractive).ToArray();
        return new(rows.Count, new(llm.Length == 0 ? null : llm.Average(), StatisticalTests.Median(llm)),
            new(extractive.Length == 0 ? null : extractive.Average(), StatisticalTests.Median(extractive)),
            rows.Count(row => row.Llm > row.Extractive), rows.Count(row => row.Llm == row.Extractive), rows.Count(row => row.Llm < row.Extractive),
            rows.Count == 0 ? null : StatisticalTests.Wilcoxon(llm, extractive),
            rows.Count(row => row.LlmUnsafe), rows.Count(row => row.ExtractiveUnsafe),
            rows.Count == 0 ? null : StatisticalTests.McNemar(rows.Count(row => row.LlmUnsafe && !row.ExtractiveUnsafe), rows.Count(row => !row.LlmUnsafe && row.ExtractiveUnsafe)),
            StatisticalTests.Kappa(rows.SelectMany(row => new[] { row.A1, row.B1 }).ToArray(), rows.SelectMany(row => new[] { row.A2, row.B2 }).ToArray()),
            new(StatisticalTests.Median(rows.Select(row => row.LlmMs)), StatisticalTests.P95(rows.Select(row => row.LlmMs))),
            new(StatisticalTests.Median(rows.Select(row => row.ExtractiveMs)), StatisticalTests.P95(rows.Select(row => row.ExtractiveMs))));
    }

    public static async Task WriteAsync(Rq1Result report, string output, TextWriter console, CancellationToken ct = default)
    {
        await StatisticsReport.SaveAsync(report, output, ct);
        await console.WriteLineAsync("RQ1 aggregate statistics; no question/answer content.");
        await console.WriteLineAsync($"Incomplete rows excluded: {report.IncompleteRows}");
        await Row("Tổng", report.Overall);
        foreach (var (category, group) in report.ByCategory) await Row(category, group);
        await console.WriteLineAsync(report.Methods);
        await console.WriteLineAsync(output);
        async Task Row(string category, Rq1Group g)
        {
            await console.WriteLineAsync($"{category}: N={g.N}; LLM mean={StatisticsReport.Number(g.Llm.Mean)}, median={StatisticsReport.Number(g.Llm.Median)}; Extractive mean={StatisticsReport.Number(g.Extractive.Mean)}, median={StatisticsReport.Number(g.Extractive.Median)}; better/equal/worse={g.LlmBetter}/{g.Equal}/{g.LlmWorse}");
            await console.WriteLineAsync($"Wilcoxon n={g.Wilcoxon?.NonZeroN ?? 0}; W={StatisticsReport.Number(g.Wilcoxon?.Statistic)}; p={StatisticsReport.Number(g.Wilcoxon?.PValue)}; r={StatisticsReport.Number(g.Wilcoxon?.R)}; {g.Wilcoxon?.Method ?? "no-data"}; unsafe LLM/Extractive={g.LlmUnsafe}/{g.ExtractiveUnsafe}; McNemar b={g.UnsafeMcNemar?.B ?? 0}, c={g.UnsafeMcNemar?.C ?? 0}, p={StatisticsReport.Number(g.UnsafeMcNemar?.PValue)}; kappa={StatisticsReport.Number(g.Agreement.Kappa)}; linear kappa={StatisticsReport.Number(g.Agreement.LinearWeightedKappa)}");
            await console.WriteLineAsync($"Latency ms LLM median/p95={StatisticsReport.Number(g.LlmLatency.MedianMs)}/{StatisticsReport.Number(g.LlmLatency.P95Ms)}; Extractive median/p95={StatisticsReport.Number(g.ExtractiveLatency.MedianMs)}/{StatisticsReport.Number(g.ExtractiveLatency.P95Ms)}");
        }
    }
}
