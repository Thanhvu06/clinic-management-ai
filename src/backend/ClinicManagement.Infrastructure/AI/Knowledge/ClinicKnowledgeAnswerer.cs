using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public sealed class ClinicKnowledgeAnswerer(IClinicKnowledgeRetriever retriever, OllamaKnowledgeClient ollama)
    : IClinicKnowledgeAnswerer
{
    public async Task<KnowledgeAnswer?> AnswerAsync(string question, CancellationToken ct = default)
    {
        var publicQuestion = KnowledgeQuestionPolicy.PublicQuestion(question);
        if (publicQuestion is null) return null;
        var retrieval = await retriever.RetrieveAsync(question, ct);
        if (retrieval.Hits.Count == 0) return null;
        var documents = string.Join("\n\n", retrieval.Hits.Select(h =>
            $"[{h.Chunk.Source.DocId}] {h.Chunk.Source.Title} — {h.Chunk.Source.Heading}\n{h.Chunk.Text}"));
        var generated = await ollama.ChatAsync(publicQuestion, documents, ct);
        var hasAnswer = !string.IsNullOrWhiteSpace(generated.Value);
        var text = hasAnswer ? Limit(generated.Value!) : Limit(retrieval.Hits[0].Chunk.Text);
        var sources = (hasAnswer ? retrieval.Hits : retrieval.Hits.Take(1))
            .Select(h => h.Chunk.Source).Distinct().ToArray();
        return new(text, sources, hasAnswer, generated.WasCalled || retrieval.EmbeddingWasCalled);
    }

    private static string Limit(string text) => string.Join(" ", Regex.Split(text.Trim(), @"(?<=[.!?])\s+").Take(5));
}
