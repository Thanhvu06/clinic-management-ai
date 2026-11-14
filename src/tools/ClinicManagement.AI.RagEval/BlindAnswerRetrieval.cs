using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed class BlindAnswerRetrieval(IClinicKnowledgeRetriever retriever) : IClinicKnowledgeRetriever
{
    private readonly Dictionary<string, KnowledgeRetrieval> _results = new(StringComparer.Ordinal);
    private readonly HashSet<bool> _answeredModes = [];
    private readonly HashSet<string> _currentQueries = new(StringComparer.Ordinal);
    private readonly List<KnowledgeHit> _usedHits = [];
    private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);

    private ReplayPassage[] _llmPassages = [];

    internal (ReplayPassage[] Llm, ReplayPassage[] Extractive) PairPassages() =>
        (_llmPassages, _usedHits.Select(ReplayPassage.FromHit).ToArray());
    private string? _question;

    public void BeginAnswer(string question, bool useLlm)
    {
        var samePair = string.Equals(_question, question, StringComparison.Ordinal) && !_answeredModes.Contains(useLlm);
        _llmPassages = samePair && !useLlm ? _usedHits.Select(ReplayPassage.FromHit).ToArray() : [];
        _currentQueries.Clear();
        _usedHits.Clear();
        _usedIds.Clear();
        if (!samePair)
        {
            _question = question;
            _results.Clear();
            _answeredModes.Clear();
        }
        _answeredModes.Add(useLlm);
    }

    public async Task<KnowledgeRetrieval> RetrieveAsync(string question, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_results.TryGetValue(question, out var snapshot))
        {
            var result = await retriever.RetrieveAsync(question, ct);
            snapshot = result with { Hits = Array.AsReadOnly(result.Hits.ToArray()) };
            _results.Add(question, snapshot);
        }
        if (_currentQueries.Add(question))
            foreach (var hit in snapshot.Hits)
                if (_usedIds.Add(hit.Chunk.Id)) _usedHits.Add(hit);
        return snapshot;
    }
}
