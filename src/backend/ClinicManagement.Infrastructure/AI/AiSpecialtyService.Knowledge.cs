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
        string intent, int version, CancellationToken ct)
    {
        if (_knowledgeAnswerer is null || !KnowledgeQuestionPolicy.IsCandidate(message, intent, !string.IsNullOrWhiteSpace(request.Intent)))
            return null;
        var answer = await _knowledgeAnswerer.AnswerAsync(message, ct);
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
}
