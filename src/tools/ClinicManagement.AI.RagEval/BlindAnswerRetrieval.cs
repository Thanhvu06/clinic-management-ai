using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed class BlindAnswerRetrieval(IClinicKnowledgeRetriever retriever) : IClinicKnowledgeRetriever
{
    private readonly Dictionary<string, KnowledgeRetrieval> _results = new(StringComparer.Ordinal);
    private readonly HashSet<bool> _answeredModes = [];
    private string? _question;

    public void BeginAnswer(string question, bool useLlm)
    {
        if (!string.Equals(_question, question, StringComparison.Ordinal) || _answeredModes.Contains(useLlm))
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
        if (_results.TryGetValue(question, out var snapshot)) return snapshot;
        var result = await retriever.RetrieveAsync(question, ct);
        snapshot = result with { Hits = Array.AsReadOnly(result.Hits.ToArray()) };
        _results.Add(question, snapshot);
        return snapshot;
    }
}
