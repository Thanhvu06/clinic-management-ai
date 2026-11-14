using System.Text.Json;
using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed record ReplayPassage(string Id, string Text, double Score)
{
    internal static ReplayPassage FromHit(KnowledgeHit hit) => new(hit.Chunk.Id, hit.Chunk.Text, hit.Score);

    internal static string Format(IEnumerable<ReplayPassage> passages) =>
        string.Join("\n\n", passages.Select(passage => $"[{passage.Id}]\n{passage.Text}"));
}

public sealed record RetrievalReplayResult(string SourceCommit, string EmbeddingDigest,
    IReadOnlyDictionary<string, ReplayPassage[]> ByStt);

public static class RetrievalReplay
{
    public static async Task<IReadOnlyDictionary<string, ReplayPassage[]>> CollectAsync(
        IReadOnlyList<EvaluationQuestion> questions, IClinicKnowledgeRetriever retriever, CancellationToken ct = default)
    {
        if (questions.Count == 0 || questions.Any(q => string.IsNullOrWhiteSpace(q.Stt)) ||
            questions.Select(q => q.Stt).Distinct(StringComparer.Ordinal).Count() != questions.Count)
            throw new InvalidDataException("Replay requires unique, nonempty question codes.");

        var entries = new Dictionary<string, ReplayPassage[]>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            var result = await retriever.RetrieveAsync(question.Text, ct);
            if (!result.HybridUsed && (result.EmbeddingWasCalled || result.Hits.Count != 0))
                throw new InvalidOperationException("Replay could not complete hybrid retrieval.");
            entries.Add(question.Stt!, result.Hits.Select(ReplayPassage.FromHit).ToArray());
        }
        return entries;
    }

    internal static RetrievalReplayResult Read(string path)
    {
        using var stream = File.OpenRead(path);
        var replay = JsonSerializer.Deserialize<RetrievalReplayResult>(stream, StatisticsReport.JsonOptions);
        if (replay?.ByStt is null || replay.SourceCommit?.Length != 40 || !replay.SourceCommit.All(Uri.IsHexDigit) ||
            replay.EmbeddingDigest?.Length != 64 || !replay.EmbeddingDigest.All(Uri.IsHexDigit) || replay.ByStt.Count == 0 || replay.ByStt.Values.Any(passages => passages is null ||
            passages.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) ||
                string.IsNullOrWhiteSpace(p.Text) || !double.IsFinite(p.Score)) ||
            passages.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != passages.Length))
            throw new InvalidDataException("Invalid replay file.");
        return replay;
    }
}
