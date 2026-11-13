using System.Text.Json;

namespace ClinicManagement.AI.RagEval;

internal enum JudgeFailureKind
{
    DocumentQuoteMismatch,
    NoDocuments,
    MissingCitation,
    JsonSchema,
    Other
}

internal sealed record JudgeGradeDiagnostics(int RetryCount, JudgeFailureKind? FinalFailure,
    bool? SourceEmpty = null, bool? RetrievalHasDocuments = null, bool? CitationUnverified = null)
{
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
