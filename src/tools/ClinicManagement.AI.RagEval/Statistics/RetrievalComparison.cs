using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed record RetrievalModeResult(string Mode, CategoryMetrics Overall, IReadOnlyDictionary<string, CategoryMetrics> ByCategory);
public sealed record RetrievalPairResult(string Left, string Right, int N, McNemarResult McNemar);
public sealed record RetrievalComparisonResult(string EvaluationLevel, int Documents, int Chunks, int TopKChunks,
    double MinCosine, string EmbeddingModel, string CategoryPolicy, IReadOnlyList<RetrievalModeResult> Modes,
    IReadOnlyList<RetrievalPairResult> PairwiseHitAt1);

public static class RetrievalComparison
{
    public static async Task<RetrievalComparisonResult> EvaluateAsync(IReadOnlyList<EvaluationQuestion> questions,
        EvaluationRetrievers retrievers, IReadOnlyList<string>? modes = null, CancellationToken ct = default)
    {
        if (questions.Count == 0) throw new InvalidDataException("No evaluation questions.");
        modes ??= ["bm25", "dense", "hybrid"];
        var results = new List<RetrievalModeResult>();
        var correctness = new Dictionary<string, List<bool>>();
        var categories = questions.Select(q => QuestionCategories.Canonical(q.Category, q.ExpectedDocId is not null)).ToArray();
        foreach (var mode in modes)
        {
            var overall = new CategoryMetrics();
            var byCategory = QuestionCategories.All.ToDictionary(category => category, _ => new CategoryMetrics());
            var correct = new List<bool>();
            for (var i = 0; i < questions.Count; i++)
            {
                var question = questions[i];
                var retrieval = await retrievers.Modes[mode].RetrieveAsync(question.Text, ct);
                if (mode == "hybrid" && retrieval.Hits.Count > 0 && !retrieval.HybridUsed)
                    throw new InvalidOperationException("Hybrid query embedding failed; refusing a mislabeled result.");
                var ids = retrieval.Hits.Select(hit => hit.Chunk.Source.DocId).Distinct(StringComparer.Ordinal).ToArray();
                overall.Add(question, ids);
                byCategory[categories[i]].Add(question, ids);
                if (question.ExpectedDocId is not null) correct.Add(ids.FirstOrDefault() == question.ExpectedDocId);
            }
            correctness[mode] = correct;
            results.Add(new(mode, overall, byCategory));
        }
        var pairs = new List<RetrievalPairResult>();
        for (var i = 0; i < modes.Count; i++)
            for (var j = i + 1; j < modes.Count; j++)
            {
                var paired = correctness[modes[i]].Zip(correctness[modes[j]]).ToArray();
                pairs.Add(new(modes[i], modes[j], paired.Length,
                    StatisticalTests.McNemar(paired.Count(p => p.First && !p.Second), paired.Count(p => !p.First && p.Second))));
            }
        return new("document; distinct document IDs within the top-k chunks", retrievers.Documents, retrievers.Chunks,
            retrievers.Options.TopK, retrievers.Options.MinCosine, retrievers.Options.EmbeddingModel,
            "Input Loại; JSON without Loại uses Trong phạm vi/Ngoài phạm vi from expectedDocId; no inferred near/far/paraphrase labels.", results, pairs);
    }

    public static async Task WriteAsync(RetrievalComparisonResult report, string output, TextWriter console, CancellationToken ct = default)
    {
        await StatisticsReport.SaveAsync(new
        {
            report.EvaluationLevel, report.Documents, report.Chunks, report.TopKChunks, report.MinCosine,
            report.EmbeddingModel, report.CategoryPolicy,
            denseRejectionPolicy = "không áp dụng: dense không có ngưỡng từ chối; correctRejection=null",
            modes = report.Modes.Select(mode => new
            {
                mode.Mode, overall = Export(mode.Mode, mode.Overall),
                byCategory = mode.ByCategory.ToDictionary(pair => pair.Key, pair => Export(mode.Mode, pair.Value))
            }),
            report.PairwiseHitAt1
        }, output, ct);
        await console.WriteLineAsync($"Đánh giá mức tài liệu ({report.Documents} tài liệu); top-k={report.TopKChunks} đoạn; hybrid cosine >= {StatisticsReport.Number(report.MinCosine)}");
        await console.WriteLineAsync("Dense: không có ngưỡng từ chối; từ chối đúng không áp dụng (JSON: null).");
        await console.WriteLineAsync("Mode | Loại | N | InScope | Outside | Hit@1 | Hit@3 | MRR | CorrectRejection");
        foreach (var mode in report.Modes)
        {
            await Row(mode.Mode, "Tổng", mode.Overall);
            foreach (var (category, metrics) in mode.ByCategory) await Row(mode.Mode, category, metrics);
        }
        foreach (var pair in report.PairwiseHitAt1)
            await console.WriteLineAsync($"McNemar Hit@1 {pair.Left}–{pair.Right}: N={pair.N}; b={pair.McNemar.B}; c={pair.McNemar.C}; p={StatisticsReport.Number(pair.McNemar.PValue)}; {pair.McNemar.Method}");
        await console.WriteLineAsync("b=left correct/right wrong; c=left wrong/right correct; raw two-sided p-values.");
        await console.WriteLineAsync(output);
        async Task Row(string mode, string category, CategoryMetrics m) =>
            await console.WriteLineAsync($"{mode} | {category} | {m.N} | {m.InScope} | {m.Outside} | {StatisticsReport.Number(m.HitAt1)} | {StatisticsReport.Number(m.HitAt3)} | {StatisticsReport.Number(m.Mrr)} | {(mode == "dense" ? "không áp dụng" : StatisticsReport.Number(m.CorrectRejection))}");
    }

    private static object Export(string mode, CategoryMetrics metrics) => new
    {
        metrics.N, metrics.InScope, metrics.Outside, metrics.HitAt1, metrics.HitAt3, metrics.Mrr,
        correctRejection = mode == "dense" ? null : metrics.CorrectRejection
    };
}
