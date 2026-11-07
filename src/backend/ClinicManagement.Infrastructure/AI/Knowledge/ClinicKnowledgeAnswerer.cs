using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public sealed class ClinicKnowledgeAnswerer(IClinicKnowledgeRetriever retriever, OllamaKnowledgeClient ollama)
    : IClinicKnowledgeAnswerer
{
    private static readonly Regex NoInformationAnswer = new(
        @"\b(?:chua co thong tin|khong co thong tin|tai lieu (?:cong khai )?(?:khong|chua) (?:co thong tin|de cap|neu)|khong tim thay thong tin|ngoai pham vi tai lieu)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<KnowledgeAnswer?> AnswerAsync(string question, CancellationToken ct = default)
    {
        var maskedQuestion = KnowledgePrivacyMask.Mask(question);
        if (string.IsNullOrWhiteSpace(maskedQuestion)) return null;
        var retrieval = await retriever.RetrieveAsync(maskedQuestion, ct);
        if (retrieval.Hits.Count == 0) return null;
        var supported = retrieval.Hits.Where(hit => hit.Score >= retrieval.Hits[0].Score * 0.6)
            .DistinctBy(hit => (hit.Chunk.Source.DocId, hit.Chunk.Source.Heading)).Take(2).ToArray();
        var documents = string.Join("\n\n", supported.Select(h =>
            $"[{h.Chunk.Source.DocId}] {h.Chunk.Source.Title} — {h.Chunk.Source.Heading}\n{h.Chunk.Text}"));
        var generated = await ollama.ChatAsync(maskedQuestion, documents, ct);
        var hasAnswer = !string.IsNullOrWhiteSpace(generated.Value);
        if (hasAnswer && NoInformationAnswer.IsMatch(KnowledgeText.Normalize(generated.Value!))) return null;
        var text = hasAnswer ? Limit(generated.Value!) : Limit(retrieval.Hits[0].Chunk.Text);
        var sources = (hasAnswer ? supported : retrieval.Hits.Take(1))
            .Select(h => h.Chunk.Source).Distinct().ToArray();
        return new(text, sources, hasAnswer, generated.WasCalled || retrieval.EmbeddingWasCalled);
    }

    private static string Limit(string text) => string.Join(" ", Regex.Split(text.Trim(), @"(?<=[.!?])\s+").Take(5));
}
