using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed record DevAnswer(string Code, string Side, string Mode, string Text, string[] SourceIds, string? ExactSourceId);

internal sealed class DevBlindProvenance
{
    public const string FileName = "dev-nguon.json";
    private readonly IReadOnlyList<KnowledgeChunk> _chunks = new MarkdownKnowledgeCatalog().Chunks;
    public List<DevAnswer> Answers { get; } = [];

    public void Add(BlindAnswerRow row, AiChatResponseDto llm, AiChatResponseDto extractive)
    {
        var llmIsA = row.AIs == "LLM";
        AddAnswer(row.Stt, llmIsA ? "A" : "B", "LLM", llmIsA ? row.AnswerA : row.AnswerB, llm);
        AddAnswer(row.Stt, llmIsA ? "B" : "A", "Trích đoạn", llmIsA ? row.AnswerB : row.AnswerA, extractive);
    }

    private void AddAnswer(string code, string side, string mode, string text, AiChatResponseDto response)
    {
        var cited = _chunks.Where(chunk => response.Sources.Contains(chunk.Source)).ToArray();
        var exact = response.DialogueOutcome == "KnowledgeExtractive" && text.Length > 0
            ? cited.FirstOrDefault(chunk => chunk.Text.StartsWith(text, StringComparison.Ordinal)) : null;
        Answers.Add(new(code, side, mode, text, cited.Select(chunk => chunk.Id).ToArray(), exact?.Id));
    }

    public static async Task<EvaluationQuestion[]> LoadDevCsvAsync(string path)
    {
        var questions = await EvaluationInput.Parse(["--csv", path]).LoadAsync(requireBothScopes: false);
        var rq2 = (await DevQuestions.LoadRq2Async()).ToDictionary(q => q.Stt!, StringComparer.Ordinal);
        if (questions.Any(q => q.Stt is null || !rq2.TryGetValue(q.Stt, out var original) ||
            q.Text != original.Text || q.Category != original.Category || q.ExpectedDocId != original.ExpectedDocId))
            throw new InvalidDataException("The CSV does not match the labeled RQ2 dev questions.");
        return questions;
    }
}
