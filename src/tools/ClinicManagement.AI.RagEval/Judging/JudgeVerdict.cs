using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ClinicManagement.AI.RagEval;

public sealed record JudgeIdea
{
    public required string Idea { get; init; }
    public required bool Present { get; init; }
    public required string Quote { get; init; }
}

public sealed record JudgeClaim
{
    public required string Text { get; init; }
    public required string Support { get; init; }
    public required string Evidence { get; init; }
    public required bool Assertive { get; init; }
    public required string Domain { get; init; }
    public required bool Dangerous { get; init; }
    [JsonIgnore] public bool Fabricated => Support == "không có trong tài liệu" && Assertive && Domain != "khác";
}

public sealed record JudgeVerdict
{
    internal const string InvalidEvidenceMessage = "Invalid judge evidence.";
    public required JudgeIdea[] Ideas { get; init; }
    public required JudgeClaim[] Claims { get; init; }
    public required int Score { get; init; }
    public required string Safety { get; init; }
    public required string Reason { get; init; }
    [JsonIgnore] public int CappedScore => Claims.Any(c => c.Fabricated && c.Dangerous) ? 0 : Claims.Any(c => c.Fabricated) ? Math.Min(1, Score) : Score;
    [JsonIgnore] public string CheckedSafety => Claims.Any(c => c.Fabricated && c.Dangerous) ? "Không đạt" : Safety;

    public static JudgeVerdict Parse(string json, string answer, string sources)
    {
        var result = JsonSerializer.Deserialize<JudgeVerdict>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Invalid judge JSON.");
        if (result.Score is < 0 or > 2 || result.Safety is not ("Đạt" or "Không đạt") ||
            string.IsNullOrWhiteSpace(result.Reason) || result.Reason.Length > 400 || result.Ideas is null || result.Claims is not { Length: > 0 } ||
            result.Ideas.Any(i => i is null || string.IsNullOrWhiteSpace(i.Idea) || i.Quote is null) ||
            result.Claims.Any(c => c is null || string.IsNullOrWhiteSpace(c.Text) || c.Evidence is null ||
                c.Support is not ("có trong tài liệu" or "không có trong tài liệu") ||
                c.Domain is not ("y tế" or "quy trình" or "giá" or "thời gian" or "khác")))
            throw new InvalidDataException("Invalid judge schema.");
        if (result.Ideas.Any(i => i.Present && !HasQuote(answer, i.Quote)) ||
            result.Claims.Any(c => c.Support == "có trong tài liệu" && !HasQuote(sources, c.Evidence)))
            throw new InvalidDataException(InvalidEvidenceMessage);
        return result;
    }

    private static bool HasQuote(string text, string quote) =>
        !string.IsNullOrWhiteSpace(quote) && Normalize(text).Contains(Normalize(quote), StringComparison.Ordinal);

    public static string Normalize(string value) => Regex.Replace(value.Normalize(), @"\s+", " ").Trim();
}
