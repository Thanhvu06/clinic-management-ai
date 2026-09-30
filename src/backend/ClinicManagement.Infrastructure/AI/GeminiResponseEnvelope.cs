using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Planning;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>
/// Reads a GenerateContent response envelope. Only the first candidate is
/// used (the request never asks for more). Its text is the in-order
/// concatenation of the non-thought text parts of that one candidate; parts
/// from different candidates are never mixed. A MAX_TOKENS or blocked finish
/// reason is reported as such instead of as malformed JSON.
/// </summary>
internal sealed class GeminiResponseEnvelope
{
    public string? Text { get; private init; }
    public AiProviderFinishReason FinishReason { get; private init; } = AiProviderFinishReason.NotReported;
    public AiPlannerValidationDiagnostic? Diagnostic { get; private init; }

    public static GeminiResponseEnvelope Read(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Fail(AiPlannerValidationReason.MalformedJson);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail(AiPlannerValidationReason.InvalidFieldType);

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                var blocked = root.TryGetProperty("promptFeedback", out var feedback) &&
                              feedback.ValueKind == JsonValueKind.Object &&
                              feedback.TryGetProperty("blockReason", out var blockReason) &&
                              blockReason.ValueKind == JsonValueKind.String;
                return Fail(AiPlannerValidationReason.MissingCandidate, blocked ? AiProviderFinishReason.Blocked : AiProviderFinishReason.NotReported);
            }

            var candidate = candidates[0];
            if (candidate.ValueKind != JsonValueKind.Object)
                return Fail(AiPlannerValidationReason.InvalidFieldType);

            var finishReason = AiProviderFinishReason.NotReported;
            if (candidate.TryGetProperty("finishReason", out var finish))
            {
                if (finish.ValueKind != JsonValueKind.String)
                    return Fail(AiPlannerValidationReason.InvalidFieldType);
                finishReason = AiPlannerValidationDiagnostic.MapFinishReason(finish.GetString());
            }

            if (finishReason == AiProviderFinishReason.MaxTokens)
                return Fail(AiPlannerValidationReason.OutputTruncated, finishReason);
            if (finishReason is not (AiProviderFinishReason.NotReported or AiProviderFinishReason.Stop))
                return Fail(AiPlannerValidationReason.UnsupportedFinishReason, finishReason);

            if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object ||
                !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                return Fail(AiPlannerValidationReason.MissingText, finishReason);

            var text = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                    return Fail(AiPlannerValidationReason.InvalidFieldType, finishReason);
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                    continue;
                if (!part.TryGetProperty("text", out var partText))
                    continue;
                if (partText.ValueKind != JsonValueKind.String)
                    return Fail(AiPlannerValidationReason.InvalidFieldType, finishReason);
                text.Append(partText.GetString());
            }

            if (string.IsNullOrWhiteSpace(text.ToString()))
                return Fail(AiPlannerValidationReason.MissingText, finishReason);

            return new GeminiResponseEnvelope { Text = text.ToString(), FinishReason = finishReason };
        }
    }

    private static GeminiResponseEnvelope Fail(AiPlannerValidationReason reason, AiProviderFinishReason finishReason = AiProviderFinishReason.NotReported) => new()
    {
        FinishReason = finishReason,
        Diagnostic = new AiPlannerValidationDiagnostic(AiPlannerValidationStage.ProviderEnvelope, reason) { FinishReason = finishReason }
    };
}
