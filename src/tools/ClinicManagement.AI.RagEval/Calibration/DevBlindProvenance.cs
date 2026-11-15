using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed record DevAnswer(string Code, string Side, string Mode, string Text, string[] SourceIds, string? ExactSourceId)
{
    public string? Outcome { get; init; }
    public ReplayPassage[]? Passages { get; init; }
    [JsonIgnore]
    public bool IsExtractive => Outcome is null ? ExactSourceId is not null : Outcome == "KnowledgeExtractive";
    [JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(Text) && (Outcome is null
        ? SourceIds.Length > 0 : Outcome is "KnowledgeAnswer" or "KnowledgeExtractive");
}

internal sealed class DevBlindProvenance
{
    public const string FileName = "dev-nguon.json";
    internal const string SupplementAuthor = "Claude (chat), chủ nhiệm duyệt";
    private const string SupplementResource = "RagEval.dev-chinh-bo-sung.csv";
    private const string SupplementSha256 = "8EB14A7348145B419099F45FCC764C85176D7F959FF5BA73C02E0E5DB16E4960";
    private readonly IReadOnlyList<KnowledgeChunk> _chunks = new MarkdownKnowledgeCatalog().Chunks;
    public List<DevAnswer> Answers { get; } = [];

    public void Add(BlindAnswerRow row, AiChatResponseDto llm, AiChatResponseDto extractive)
    {
        var llmIsA = row.AIs == "LLM";
        AddAnswer(row.Stt, llmIsA ? "A" : "B", "LLM", llmIsA ? row.AnswerA : row.AnswerB, llm, llmIsA ? row.PassagesA : row.PassagesB);
        AddAnswer(row.Stt, llmIsA ? "B" : "A", "Trích đoạn", llmIsA ? row.AnswerB : row.AnswerA, extractive, llmIsA ? row.PassagesB : row.PassagesA);
    }

    private void AddAnswer(string code, string side, string mode, string text, AiChatResponseDto response, ReplayPassage[]? passages)
    {
        var cited = _chunks.Where(chunk => response.Sources.Contains(chunk.Source)).ToArray();
        var exact = response.DialogueOutcome == "KnowledgeExtractive" && text.Length > 0
            ? cited.FirstOrDefault(chunk => chunk.Text.StartsWith(text, StringComparison.Ordinal)) : null;
        Answers.Add(new(code, side, mode, text, cited.Select(chunk => chunk.Id).ToArray(), exact?.Id)
        {
            Outcome = response.DialogueOutcome,
            Passages = passages
        });
    }

    public static Task<EvaluationQuestion[]> LoadDevCsvAsync(string path) =>
        LoadDevCsvAsync(path, LoadRegisteredSupplementAsync);

    internal static async Task<EvaluationQuestion[]> LoadDevCsvAsync(string path, Func<Task<EvaluationQuestion[]>> loadSupplement)
    {
        var questions = await EvaluationInput.Parse(["--csv", path]).LoadAsync(requireBothScopes: false);
        var rq2 = (await DevQuestions.LoadRq2Async()).ToDictionary(q => q.Stt!, StringComparer.Ordinal);
        if (questions.All(q => Matches(q, rq2)))
            return questions;

        var supplement = await loadSupplement();
        if (supplement.Any(q => rq2.ContainsKey(q.Stt!)))
            throw new InvalidDataException("The dev supplement reuses an RQ2 STT.");
        var registered = supplement.ToDictionary(q => q.Stt!, StringComparer.Ordinal);
        if (questions.Any(q => !Matches(q, rq2) && !Matches(q, registered)))
            throw new InvalidDataException("The CSV does not match the labeled RQ2 dev questions.");
        return questions;
    }

    private static bool Matches(EvaluationQuestion question, IReadOnlyDictionary<string, EvaluationQuestion> registered) =>
        question.Stt is not null && registered.TryGetValue(question.Stt, out var original) &&
        question.Text == original.Text && question.Category == original.Category && question.ExpectedDocId == original.ExpectedDocId;

    private static async Task<EvaluationQuestion[]> LoadRegisteredSupplementAsync()
    {
        using var stream = typeof(DevBlindProvenance).Assembly.GetManifestResourceStream(SupplementResource)
            ?? throw new InvalidDataException("The registered dev supplement is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ParseSupplement(await reader.ReadToEndAsync(), SupplementSha256);
    }

    internal static EvaluationQuestion[] ParseSupplement(string content, string expectedSha256)
    {
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))) != expectedSha256)
            throw new InvalidDataException("The registered dev supplement does not match its SHA-256.");

        var rows = EvaluationInput.ParseCsvQuestions(text, SupplementResource, SupplementAuthor);
        if (rows.Any(q => string.IsNullOrWhiteSpace(q.Stt)) ||
            rows.Select(q => q.Stt).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new InvalidDataException("The registered dev supplement requires unique, nonempty STT values.");
        return rows;
    }
}
