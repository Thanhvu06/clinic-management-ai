using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public sealed class Bm25KnowledgeIndex
{
    private readonly IReadOnlyList<KnowledgeChunk> _chunks;
    private readonly Dictionary<string, int>[] _terms;
    private readonly Dictionary<string, int> _chunkIndexById;
    private readonly Dictionary<string, int> _frequency;
    private readonly double _averageLength;

    public Bm25KnowledgeIndex(IReadOnlyList<KnowledgeChunk> chunks)
    {
        _chunks = chunks;
        _terms = chunks.Select(c => KnowledgeText.Terms(c.Source.Title + " " + c.Source.Heading + " " + c.Text)
            .GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count())).ToArray();
        _chunkIndexById = chunks.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        _frequency = _terms.SelectMany(t => t.Keys).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        _averageLength = Math.Max(1, _terms.Select(t => t.Values.Sum()).DefaultIfEmpty(1).Average());
    }

    public IReadOnlyList<KnowledgeHit> Search(string question)
    {
        var query = KnowledgeText.Terms(question).Distinct(StringComparer.Ordinal).ToArray();
        return _chunks.Select((chunk, i) =>
        {
            var length = _terms[i].Values.Sum();
            var score = query.Sum(term =>
            {
                if (!_terms[i].TryGetValue(term, out var tf)) return 0;
                var idf = Math.Log(1 + (_chunks.Count - _frequency[term] + 0.5) / (_frequency[term] + 0.5));
                return idf * tf * 2.2 / (tf + 1.2 * (0.25 + 0.75 * length / _averageLength));
            });
            return new KnowledgeHit(chunk, score / (score + 5));
        }).Where(h => h.Score > 0).OrderByDescending(h => h.Score).ThenBy(h => h.Chunk.Id, StringComparer.Ordinal).ToArray();
    }

    public (double Coverage, double MaxMatchedIdf) EvaluateCoverage(string question, KnowledgeChunk chunk)
    {
        var contentTerms = KnowledgeText.ContentTerms(question);
        if (contentTerms.Length == 0 || !_chunkIndexById.TryGetValue(chunk.Id, out var index)) return (0, 0);
        var terms = _terms[index];
        var matched = contentTerms.Where(terms.ContainsKey).ToArray();
        if (matched.Length == 0) return (0, 0);
        return ((double)matched.Length / contentTerms.Length, matched.Max(Idf));
    }

    public bool MeetsCoverage(string question, KnowledgeChunk chunk, double minCoverage = 0.5, double minTermIdf = 1.5)
    {
        var (coverage, maxIdf) = EvaluateCoverage(question, chunk);
        return coverage + 1e-9 >= minCoverage && maxIdf + 1e-9 >= minTermIdf;
    }

    public double Idf(string term) => _frequency.TryGetValue(term, out var df)
        ? Math.Log(1 + (_chunks.Count - df + 0.5) / (df + 0.5))
        : 0;

    public static IReadOnlyList<KnowledgeHit> Fuse(params IReadOnlyList<KnowledgeHit>[] rankings) => rankings
        .SelectMany(ranking => ranking.Select((hit, rank) => new { hit.Chunk, Score = 1.0 / (60 + rank + 1) }))
        .GroupBy(hit => hit.Chunk.Id).Select(group => new KnowledgeHit(group.First().Chunk, group.Sum(hit => hit.Score)))
        .OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Chunk.Id, StringComparer.Ordinal).ToArray();

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++) { dot += (double)a[i] * b[i]; normA += (double)a[i] * a[i]; normB += (double)b[i] * b[i]; }
        return normA > 0 && normB > 0 ? dot / Math.Sqrt(normA * normB) : 0;
    }
}
