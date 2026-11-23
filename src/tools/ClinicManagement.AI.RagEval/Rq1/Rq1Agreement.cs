using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record Rq1ScoreAgreement(int N, double? PercentAgreement, double? Kappa, double? LinearWeightedKappa);
public sealed record Rq1ItemAgreement(int N, double? PercentAgreement, double? Kappa);
public sealed record Rq1AgreementReport(Rq1ScoreAgreement Overall, IReadOnlyDictionary<string, Rq1ScoreAgreement> ByCategory,
    IReadOnlyDictionary<string, Rq1ItemAgreement> Items);
internal sealed record Rq1RatingComparison(string Category, bool HasIdeas, Rq1Checklist First, Rq1Checklist Second,
    bool? FirstRefused, bool? SecondRefused);

internal static class Rq1Agreement
{
    private sealed record Rating(string Category, bool HasIdeas, Rq1Checklist Codex, Rq1Checklist Human,
        bool? CodexRefused, bool? HumanRefused);

    internal static Rq1AgreementReport? Read(XLWorkbook workbook)
    {
        var codex = MergedChecklists.Read(workbook, 6);
        var human = MergedChecklists.Read(workbook, 11);
        if (codex is null || human is null) return null;

        var ratings = new List<Rating>();
        foreach (var (stt, person) in human.Rows)
        {
            if (person is not { A: { } humanA, B: { } humanB } ||
                !codex.Rows.TryGetValue(stt, out var automated) ||
                automated is not { A: { } codexA, B: { } codexB })
                continue;

            var hasIdeas = person.HasIdeas || automated.HasIdeas;
            ratings.Add(new(person.Category, hasIdeas, codexA, humanA, automated.RefusedA, person.RefusedA));
            ratings.Add(new(person.Category, hasIdeas, codexB, humanB, automated.RefusedB, person.RefusedB));
        }

        return Summarize(ratings);
    }

    internal static Rq1AgreementReport Compare(IEnumerable<Rq1RatingComparison> pairs) =>
        Summarize(pairs.Select(pair => new Rating(pair.Category, pair.HasIdeas, pair.First, pair.Second,
            pair.FirstRefused, pair.SecondRefused)).ToArray());

    private static Rq1AgreementReport Summarize(IReadOnlyCollection<Rating> ratings)
    {
        var groups = ratings.ToLookup(rating => rating.Category);
        var byCategory = QuestionCategories.All.Where(groups.Contains)
            .ToDictionary(category => category, category => Scores(groups[category]));
        return new(Scores(ratings), byCategory, new Dictionary<string, Rq1ItemAgreement>
        {
            ["C1"] = Items(ratings.Select(rating => (rating.Codex.C1, rating.Human.C1))),
            ["C2"] = Items(ratings.Select(rating => (C2(rating.Codex, rating.HasIdeas), C2(rating.Human, rating.HasIdeas)))),
            ["C3"] = Items(ratings.Select(rating => (rating.Codex.C3, rating.Human.C3))),
            ["C4"] = Items(ratings.Select(rating => (rating.Codex.C4, rating.Human.C4))),
            ["C5"] = Items(ratings.Select(rating => (rating.Codex.C5, rating.Human.C5))),
            ["Từ chối"] = Items(ratings.Where(rating => rating.CodexRefused.HasValue && rating.HumanRefused.HasValue)
                .Select(rating => (rating.CodexRefused.GetValueOrDefault(), rating.HumanRefused.GetValueOrDefault())))
        });
    }

    internal static string C2(Rq1Checklist checklist, bool hasIdeas) =>
        !hasIdeas && checklist.C2 == Rq1Checklist.NotApplicable ? Rq1Checklist.Full : checklist.C2;

    private static Rq1ScoreAgreement Scores(IEnumerable<Rating> ratings)
    {
        var pairs = ratings.Select(rating => (Codex: rating.Codex.Score, Human: rating.Human.Score)).ToArray();
        var kappa = StatisticalTests.Kappa(pairs.Select(pair => pair.Codex).ToArray(), pairs.Select(pair => pair.Human).ToArray());
        return new(kappa.Ratings, pairs.Length == 0 ? null : 100 * ObservedAgreement(pairs), kappa.Kappa, kappa.LinearWeightedKappa);
    }

    private static Rq1ItemAgreement Items<T>(IEnumerable<(T Codex, T Human)> ratings) where T : notnull
    {
        var pairs = ratings.ToArray();
        var n = pairs.Length;
        if (n == 0) return new(0, null, null);

        var humanCounts = pairs.GroupBy(pair => pair.Human).ToDictionary(group => group.Key, group => group.Count());
        var expected = pairs.GroupBy(pair => pair.Codex)
            .Sum(group => (double)group.Count() * humanCounts.GetValueOrDefault(group.Key)) / ((double)n * n);
        var observed = ObservedAgreement(pairs);
        return new(n, 100 * observed, expected >= 1 ? null : (observed - expected) / (1 - expected));
    }

    private static double ObservedAgreement<T>((T Codex, T Human)[] pairs) =>
        (double)pairs.Count(pair => EqualityComparer<T>.Default.Equals(pair.Codex, pair.Human)) / pairs.Length;

    internal static async Task WriteAsync(Rq1AgreementReport report, TextWriter console)
    {
        await console.WriteLineAsync("Đồng thuận Người chấm (grader2) – Bộ chấm tự động Codex (grader1)");
        await WriteScore("Tổng", report.Overall);
        foreach (var (category, score) in report.ByCategory) await WriteScore(category, score);
        foreach (var (item, agreement) in report.Items)
            await console.WriteLineAsync($"{item} | N={agreement.N} | Đồng thuận (%)={StatisticsReport.Number(agreement.PercentAgreement)} | Kappa={StatisticsReport.Number(agreement.Kappa)}");

        Task WriteScore(string group, Rq1ScoreAgreement agreement) =>
            console.WriteLineAsync($"Điểm 0/1/2 – {group} | N={agreement.N} | Đồng thuận (%)={StatisticsReport.Number(agreement.PercentAgreement)} | Kappa={StatisticsReport.Number(agreement.Kappa)} | Kappa trọng số tuyến tính={StatisticsReport.Number(agreement.LinearWeightedKappa)}");
    }
}
