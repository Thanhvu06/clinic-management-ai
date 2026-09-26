using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.AI.Interfaces;

namespace ClinicManagement.Application.AI.Conversation;

public sealed record AiExtractedEntity(
    string Type,
    string? Value,
    string Source,
    decimal Confidence,
    bool IsValid,
    string? RejectionReason = null);

public sealed class AiEntityExtractionResult
{
    public IReadOnlyList<AiExtractedEntity> Entities { get; init; } = Array.Empty<AiExtractedEntity>();
    public string? ClinicalReason => Entities.FirstOrDefault(x => x.Type == "ClinicalReason" && x.IsValid)?.Value;
    public string? DoctorName => Entities.FirstOrDefault(x => x.Type == "DoctorName" && x.IsValid)?.Value;
    public string? NegatedDoctor => Entities.FirstOrDefault(x => x.Type == "NegatedDoctor" && x.IsValid)?.Value;
}

public sealed class AiConversationAnalysis
{
    public string NormalizedText { get; init; } = string.Empty;
    public IntentClassificationResult Intent { get; init; } = new();
    public AiEntityExtractionResult Entities { get; init; } = new();
    public AiSafetyGuardResult Safety { get; init; } = new();
    public string ProviderStatus { get; init; } = "NotCalled";
}
