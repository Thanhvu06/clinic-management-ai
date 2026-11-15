using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicManagement.AI.RagEval;

internal enum JudgeFailureKind
{
    DocumentQuoteMismatch,
    NoDocuments,
    MissingCitation,
    JsonSchema,
    Other,
    UnknownSentenceId,
    MissingSourceId,
    Timeout,
    MissingAnswerId
}

internal sealed record JudgeAttemptFailure(int Run, int Attempt, JudgeFailureKind Kind, string Reason);

internal sealed record JudgeGradeDiagnostics(int RetryCount, JudgeFailureKind? FinalFailure,
    bool? SourceEmpty = null, bool? RetrievalHasDocuments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CitationUnverified = null,
    string? ExceptionType = null,
    [property: JsonPropertyName("format_invalid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? FormatInvalid = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AgreedRuns = null,
    [property: JsonPropertyName("citation_unverified"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? LegacyFormatFlag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<JudgeAttemptFailure>? AttemptFailures = null)
{
    [JsonIgnore]
    public bool HasFormatFlag => (FormatInvalid ?? CitationUnverified ?? LegacyFormatFlag) == true;

    public static JudgeFailureKind ClassifySchemaFailure(Exception error, string? response)
    {
        if (response is null) return error is JsonException ? JudgeFailureKind.JsonSchema : JudgeFailureKind.Other;
        try
        {
            using var document = JsonDocument.Parse(response);
            var result = document.RootElement;
            if (result.ValueKind != JsonValueKind.Object) return JudgeFailureKind.JsonSchema;
            if (Entries(result, "ideas").Any(idea => Missing(idea, "quote")))
                return JudgeFailureKind.MissingCitation;

            var claims = Entries(result, "claims").ToArray();
            if (claims.Any(claim => Missing(claim, "evidence"))) return JudgeFailureKind.MissingCitation;
            return JudgeFailureKind.JsonSchema;
        }
        catch (JsonException)
        {
            return JudgeFailureKind.JsonSchema;
        }
    }

    private static IEnumerable<JsonElement> Entries(JsonElement result, string name) =>
        result.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object) : [];

    private static bool Missing(JsonElement item, string name) =>
        !item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

}
