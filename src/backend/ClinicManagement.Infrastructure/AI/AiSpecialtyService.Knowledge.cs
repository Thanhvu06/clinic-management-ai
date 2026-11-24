using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private readonly IClinicKnowledgeAnswerer? _knowledgeAnswerer;

    private async Task<AiChatResponseDto?> ResolveKnowledgeAsync(AiChatRequestDto request, string message,
        string intent, ClinicManagement.Application.AI.Interfaces.IntentClassificationResult classification, int version, CancellationToken ct, bool early = false)
    {
        var expanded = ClinicQuestionNormalizer.Expand(message);
        var fresh = IsFreshKnowledgeQuestion(request);
        if (ClinicKnowledgeGatePolicy.IsOutsideClinic(expanded)) return null;
        if (early && fresh && ClinicKnowledgeGatePolicy.CanUseSemanticFallback(expanded) &&
            (!classification.IsClear || !string.Equals(expanded, message, StringComparison.Ordinal)) &&
            ClinicKnowledgeGatePolicy.InformationTopic(expanded) is { } topic && topic != ClinicInformationTopic.None)
            return await ResolveRecoveredClinicInformationAsync(request, expanded, topic, version, ct);

        var legacy = KnowledgeQuestionPolicy.IsCandidate(message, intent, !string.IsNullOrWhiteSpace(request.Intent), classification);
        var recover = fresh && ClinicKnowledgeGatePolicy.CanUseSemanticFallback(expanded) &&
            (!classification.IsClear || ClinicKnowledgeGatePolicy.IsInformationQuestion(expanded)) &&
            (intent != AiChatIntentTypes.StartBooking || ClinicKnowledgeGatePolicy.IsProcedureQuestion(expanded));
        var earlyGuidance = KnowledgeQuestionPolicy.IsEarlyGuidance(message, classification);
        if (_knowledgeAnswerer is null || early != earlyGuidance || !legacy && !recover)
            return null;
        var answer = recover && _knowledgeAnswerer is IClinicSemanticKnowledgeAnswerer semantic
            ? await semantic.AnswerThroughIntentGateAsync(expanded, ct)
            : legacy ? await _knowledgeAnswerer.AnswerAsync(message, ct) : null;
        if (answer is null) return null;
        var block = AiProviderOutputGuard.Inspect(AiActorRole.Patient, answer.Text);
        var response = new AiChatResponseDto
        {
            Message = block?.Message ?? answer.Text,
            DialogueOutcome = block?.Code ?? (answer.Generated ? "KnowledgeAnswer" : "KnowledgeExtractive"),
            Sources = block is null ? answer.Sources.ToList() : [],
            Urgency = block?.Emergency == true ? "EMERGENCY" : "ROUTINE",
            SafetyNotice = block?.Message,
            PrimaryIntent = AiChatIntentTypes.UnclearOrOutOfScope,
            ProviderWasCalled = answer.ProviderWasCalled, ProviderAttemptCount = answer.ProviderWasCalled ? 1 : 0,
            ProviderStatus = answer.Generated ? "Success" : answer.ProviderWasCalled ? "FallbackToLocal" : "NotCalled",
            ProviderState = block is not null ? AiProviderStatusContract.SafetyBlocked : answer.Generated ? "Online"
                : answer.ProviderWasCalled ? "Degraded" : "NotCalled",
            ExecutionMode = block is not null ? AiProviderStatusContract.ExecutionManualHandoff : answer.Generated
                ? AiProviderStatusContract.ExecutionProviderAssisted : AiProviderStatusContract.ExecutionDeterministicFallback,
            FallbackActive = !answer.Generated || block is not null, ManualSelectionRequired = block is not null
        };
        if (block?.Emergency == true)
            response.Actions.Add(new AiActionDto { Id = "act-emergency-115", Type = AiActionTypes.CallEmergency,
                Label = "Gọi cấp cứu 115", Style = "danger", Payload = new AiActionPayloadDto { TargetUrl = "tel:115" } });
        // The existing protected hook already transports toolResults; this mirrors public citations without changing that hook.
        if (response.Sources.Count > 0)
            response.ToolResults.Add(new AiToolExecutionResult { ResultType = "knowledge_sources", Data = new { sources = response.Sources } });
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }

    private static bool IsFreshKnowledgeQuestion(AiChatRequestDto request) =>
        string.IsNullOrWhiteSpace(request.Intent) && string.IsNullOrWhiteSpace(request.ContextSnapshotId) &&
        string.IsNullOrWhiteSpace(request.DraftId) && string.IsNullOrWhiteSpace(request.PendingSlotDate) &&
        !request.PendingSpecialtyId.HasValue && !request.PendingDoctorId.HasValue && !request.PendingSlotId.HasValue &&
        string.IsNullOrWhiteSpace(request.Reason);
}
