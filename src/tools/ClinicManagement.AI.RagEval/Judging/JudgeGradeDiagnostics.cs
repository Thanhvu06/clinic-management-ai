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

internal sealed record JudgeGradeDiagnostics(int RetryCount, JudgeFailureKind? FinalFailure)
{
    public static JudgeFailureKind Classify(Exception error, string? response, string sources, bool? hasDocuments)
    {
        if (response is null) return error is JsonException ? JudgeFailureKind.JsonSchema : JudgeFailureKind.Other;
        try
        {
            using var document = JsonDocument.Parse(response);
            var result = document.RootElement;
            if (result.ValueKind != JsonValueKind.Object) return JudgeFailureKind.JsonSchema;
            var invalidQuote = error is InvalidDataException && error.Message == JudgeVerdict.InvalidEvidenceMessage;

            if (Entries(result, "ideas").Any(idea => Missing(idea, "quote") ||
                invalidQuote && idea.TryGetProperty("present", out var present) && present.ValueKind == JsonValueKind.True &&
                Empty(idea, "quote")))
                return JudgeFailureKind.MissingCitation;

            var claims = Entries(result, "claims").ToArray();
            if (claims.Any(claim => Missing(claim, "evidence"))) return JudgeFailureKind.MissingCitation;
            if (!invalidQuote) return JudgeFailureKind.JsonSchema;

            var supported = claims.Where(claim => claim.TryGetProperty("support", out var support) &&
                support.ValueKind == JsonValueKind.String && support.GetString() == "có trong tài liệu").ToArray();
            if (supported.Any(claim => Empty(claim, "evidence")))
                return hasDocuments == false ? JudgeFailureKind.NoDocuments : JudgeFailureKind.MissingCitation;

            var normalizedSources = JudgeVerdict.Normalize(sources);
            if (supported.Any(claim => claim.GetProperty("evidence").ValueKind == JsonValueKind.String &&
                !normalizedSources.Contains(JudgeVerdict.Normalize(claim.GetProperty("evidence").GetString()!), StringComparison.Ordinal)))
                return hasDocuments == false ? JudgeFailureKind.NoDocuments : JudgeFailureKind.DocumentQuoteMismatch;
            return JudgeFailureKind.Other;
        }
        catch (JsonException)
        {
            return JudgeFailureKind.JsonSchema;
        }
        catch (ArgumentException)
        {
            return JudgeFailureKind.Other;
        }
    }

    private static IEnumerable<JsonElement> Entries(JsonElement result, string name) =>
        result.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object) : [];

    private static bool Missing(JsonElement item, string name) =>
        !item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static bool Empty(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        string.IsNullOrWhiteSpace(value.GetString());
}
