using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>
/// Deterministic first pass for every actor. Provider output can enrich this
/// analysis later, but it cannot replace normalization, safety or server policy.
/// </summary>
public sealed class AiConversationPipeline : IAiConversationPipeline
{
    private readonly IVietnameseIntentClassifier _classifier;
    private readonly IAiSafetyGuard _safetyGuard;

    public AiConversationPipeline(IVietnameseIntentClassifier classifier, IAiSafetyGuard safetyGuard)
    {
        _classifier = classifier;
        _safetyGuard = safetyGuard;
    }

    public AiConversationAnalysis Analyze(string? rawMessage, IntentClassificationContext? context = null)
    {
        var normalized = AiTextNormalizer.Normalize(rawMessage);
        var safety = _safetyGuard.Inspect(normalized);
        var intent = _classifier.Classify(normalized, context);
        var entities = ExtractEntities(normalized, intent);

        return new AiConversationAnalysis
        {
            NormalizedText = normalized,
            Intent = intent,
            Entities = entities,
            Safety = safety,
            ProviderStatus = safety.IsEmergency || safety.IsPromptInjection
                ? AiProviderStatusContract.SafetyBlocked
                : AiProviderStatusContract.NotCalled
        };
    }

    private static AiEntityExtractionResult ExtractEntities(string message, IntentClassificationResult intent)
    {
        var entities = new List<AiExtractedEntity>();
        var reason = intent.ExtractedReason;
        if (string.IsNullOrWhiteSpace(reason) &&
            (intent.Intent == AiChatIntentTypes.ProvideReason || intent.Intent == AiChatIntentTypes.SpecialtyRecommendation))
        {
            reason = ExtractClinicalReason(message);
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            var normalizedReason = AiTextNormalizer.NormalizeClinicalReason(reason);
            entities.Add(new AiExtractedEntity(
                "ClinicalReason", normalizedReason, "local.intent", .98m,
                VietnameseIntentClassifier.IsPlausibleClinicalReason(normalizedReason),
                VietnameseIntentClassifier.IsPlausibleClinicalReason(normalizedReason) ? null : "not_clinical"));
        }

        if (!string.IsNullOrWhiteSpace(intent.ExtractedDoctorName) &&
            !intent.ExtractedDoctorName.StartsWith("@relative:", StringComparison.Ordinal))
        {
            var candidate = AiTextNormalizer.Normalize(intent.ExtractedDoctorName);
            var isInterrogative = Regex.IsMatch(candidate, @"^(?:nào|ai|người nào|vị nào|gì|ở đâu)$", RegexOptions.IgnoreCase) ||
                                   Regex.IsMatch(candidate, @"\b(?:nào|ai|người nào|vị nào|bác sĩ gì|ở đâu)\b", RegexOptions.IgnoreCase);
            entities.Add(new AiExtractedEntity(
                "DoctorName", isInterrogative ? null : candidate, "local.intent", isInterrogative ? .0m : .9m,
                !isInterrogative, isInterrogative ? "interrogative_not_a_name" : null));
        }

        if (!string.IsNullOrWhiteSpace(intent.NegatedDoctorName))
        {
            entities.Add(new AiExtractedEntity("NegatedDoctor", AiTextNormalizer.Normalize(intent.NegatedDoctorName), "local.intent", .9m, true));
        }

        return new AiEntityExtractionResult { Entities = entities };
    }

    private static string? ExtractClinicalReason(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var normalized = AiTextNormalizer.Normalize(message);
        var lower = normalized.ToLowerInvariant();
        var match = Regex.Match(lower, @"(?:có\s+)?bác\s+sĩ\s+nào\s+(?:khám|chữa|điều\s+trị)\s+(?:bệnh\s+)?(?<reason>.+?)(?:\s+không)?$", RegexOptions.IgnoreCase);
        if (match.Success) return AiTextNormalizer.NormalizeClinicalReason(match.Groups["reason"].Value);
        return AiTextNormalizer.NormalizeClinicalReason(normalized);
    }
}
