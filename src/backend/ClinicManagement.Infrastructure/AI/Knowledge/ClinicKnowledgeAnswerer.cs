using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public sealed class ClinicKnowledgeAnswerer(IClinicKnowledgeRetriever retriever, OllamaKnowledgeClient ollama)
    : IClinicSemanticKnowledgeAnswerer
{
    private static readonly Regex NumberedListItem = new(
        @"(?<!\S)\d+[.)]\s+(?=\S)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NoInformationAnswer = new(
        @"\b(?:(?:chua co|khong co|khong tim thay) thong tin|khong co du lieu ve|" +
        @"khong (?:co|nam|(?:duoc )?(?:de cap|neu|nhac den)) trong tai lieu|" +
        @"tai lieu (?:cong khai )?(?:duoc cung cap )?(?:khong|chua) (?:de cap|neu|nhac den)|" +
        // Normalization removes the comma in attributions like "theo tài liệu, không có phí".
        @"(?<!theo )tai lieu (?:cong khai )?(?:duoc cung cap )?khong co|" +
        @"ngoai pham vi (?:cua )?tai lieu)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool IsNoInformationAnswer(string generated) =>
        NoInformationAnswer.IsMatch(KnowledgeText.Normalize(generated));

    public async Task<KnowledgeAnswer?> AnswerAsync(string question, CancellationToken ct = default)
    {
        var maskedQuestion = KnowledgePrivacyMask.Mask(question);
        if (string.IsNullOrWhiteSpace(maskedQuestion)) return null;
        var retrieval = await retriever.RetrieveAsync(maskedQuestion, ct);
        return await AnswerRetrievedAsync(maskedQuestion, retrieval, ct);
    }

    public async Task<KnowledgeAnswer?> AnswerThroughIntentGateAsync(string question, CancellationToken ct = default)
    {
        var expanded = ClinicQuestionNormalizer.Expand(question);
        if (!ClinicKnowledgeGatePolicy.CanUseSemanticFallback(expanded)) return null;
        var masked = KnowledgePrivacyMask.Mask(expanded);
        if (string.IsNullOrWhiteSpace(masked)) return null;
        var retrieval = await retriever.RetrieveAsync(masked, ct);
        if (retrieval.Hits.Count == 0 && retriever is IClinicSemanticKnowledgeRetriever semantic)
            retrieval = await semantic.RetrieveSemanticAsync(masked, ct);
        return await AnswerRetrievedAsync(masked, retrieval, ct);
    }

    private async Task<KnowledgeAnswer?> AnswerRetrievedAsync(string maskedQuestion, KnowledgeRetrieval retrieval, CancellationToken ct)
    {
        if (retrieval.Hits.Count == 0) return null;
        var supported = retrieval.Hits.Where(hit => hit.Score >= retrieval.Hits[0].Score * 0.6)
            .DistinctBy(hit => (hit.Chunk.Source.DocId, hit.Chunk.Source.Heading)).Take(2).ToArray();
        var documents = string.Join("\n\n", supported.Select(h =>
            $"[{h.Chunk.Source.DocId}] {h.Chunk.Source.Title} — {h.Chunk.Source.Heading}\n{h.Chunk.Text}"));
        var generated = await ollama.ChatAsync(maskedQuestion, documents, ct);
        var hasAnswer = !string.IsNullOrWhiteSpace(generated.Value);
        if (hasAnswer && IsNoInformationAnswer(generated.Value!)) return null;
        var text = hasAnswer ? Limit(generated.Value!) : Limit(retrieval.Hits[0].Chunk.Text);
        var sources = (hasAnswer ? supported : retrieval.Hits.Take(1))
            .Select(h => h.Chunk.Source).Distinct().ToArray();
        return new(text, sources, hasAnswer, generated.WasCalled || retrieval.EmbeddingWasCalled);
    }

    private static string Limit(string text)
    {
        var trimmed = text.Trim();
        return NumberedListItem.Matches(trimmed).Count >= 2
            ? trimmed
            : string.Join(" ", Regex.Split(trimmed, @"(?<=[.!?])\s+").Take(5));
    }
}
