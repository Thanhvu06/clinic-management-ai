using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.DTOs;

namespace ClinicManagement.Application.AI.Interfaces;

public interface IAiSpecialtyService
{
    Task<AiSuggestionResponseDto> GetSuggestionsAsync(AiSuggestionRequestDto request, CancellationToken cancellationToken = default);
    Task<AiChatResponseDto> ChatAsync(AiChatRequestDto request, CancellationToken cancellationToken = default);
}

public interface IAiSpecialtySuggestionProvider
{
    Task<List<AiProviderSuggestionResult>> GetSuggestionsFromAiAsync(string symptomDescription, List<WhitelistItemDto> whitelist, CancellationToken cancellationToken = default);
    Task<AiChatProviderResult> ChatWithAiAsync(string message, List<ChatMessageDto> context, List<WhitelistItemDto> whitelist, string clinicContextJson, CancellationToken cancellationToken = default);
}

public interface IClinicAiContextService
{
    Task<string> GetClinicContextJsonAsync(CancellationToken cancellationToken = default);
}

public interface IAiSpecialtyClassifier
{
    SpecialtyClassificationResult? ClassifySymptom(string symptomDescription);
}

public interface IVietnameseIntentClassifier
{
    IntentClassificationResult Classify(string? message, IntentClassificationContext? context = null);
}

public class IntentClassificationContext
{
    public bool HasActiveDraft { get; set; }
    public bool HasDoctor { get; set; }
    public bool HasSlot { get; set; }
    public bool HasReason { get; set; }
    public string? LastModelQuestion { get; set; }
    public List<string>? DisplayedDoctorNames { get; set; }
    public List<string>? DisplayedSlotLabels { get; set; }
}

public class IntentClassificationResult
{
    public string Intent { get; set; } = AiChatIntentTypes.UnclearOrOutOfScope;
    public float Confidence { get; set; } = 1.0f;
    public bool IsClear { get; set; } = true;
    public string? ClarificationPrompt { get; set; }
    public bool IsCorrection { get; set; }
    public string? NegatedDoctorName { get; set; }
    public string? NegatedSymptom { get; set; }
    public string? CorrectionTarget { get; set; }
    public string? ExtractedDoctorName { get; set; }
    public string? ExtractedDate { get; set; }
    public string? ExtractedTimePreference { get; set; }
    public string? ExtractedReason { get; set; }
    public string Method { get; set; } = "RuleBased";
}
