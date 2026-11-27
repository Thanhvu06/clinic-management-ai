using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto> RespondToPatientSafetyAsync(
        AiChatRequestDto request, AiSafetyGuardResult safety, CancellationToken cancellationToken)
    {
        await _auditService.LogActionAsync(new AiAuditLogEntry
        {
            UserId = _currentUserService.UserId,
            SessionId = request.SessionId,
            ActionType = "SafetyGuard",
            Outcome = safety.IsSelfHarm ? "SELF_HARM" : "EMERGENCY",
            ErrorCode = safety.IsSelfHarm ? "SELF_HARM_RISK" : "EMERGENCY_RED_FLAG"
        }, cancellationToken);
        return new AiChatResponseDto
        {
            Message = AiSafetyGuard.PatientEmergencyMessage(safety),
            Urgency = safety.IsEmergency ? "EMERGENCY" : "ROUTINE",
            SafetyNotice = safety.IsEmergency ? "TÌNH HUỐNG Y TẾ CẤP CỨU: Gọi 115 hoặc đến cơ sở cấp cứu gần nhất." : null,
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled",
            ProviderState = AiProviderStatusContract.SafetyBlocked,
            DialogueOutcome = safety.IsSelfHarm ? "SafetySelfHarm" : "SafetyEmergency",
            Actions = safety.IsEmergency ?
            [
                new() { Id = "act-emergency-115", Type = AiActionTypes.CallEmergency, Label = "Gọi cấp cứu 115", Style = "danger", RequiresAuthentication = false, RequiresConfirmation = false, Payload = new AiActionPayloadDto { TargetUrl = "tel:115" } }
            ] : []
        };
    }

    private async Task<AiChatResponseDto?> ResolveMedicationRequestAsync(string message, CancellationToken ct)
    {
        var pickingMedicine = AiMedicalScopeGuard.IsMedicinePickingRequest(message);
        if (!pickingMedicine && !AiMedicalScopeGuard.IsPrescriptionRequest(message)) return null;

        var refusal = pickingMedicine ? AiMedicalScopeGuard.PatientPickingRefusal : AiMedicalScopeGuard.PatientRefusal;
        var response = new AiChatResponseDto
        {
            Message = refusal,
            Reply = refusal,
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            AssistantStatus = "Online",
            ProviderStatus = AiProviderStatusContract.NotCalled,
            ProviderState = AiProviderStatusContract.NotCalled,
            ExecutionMode = AiProviderStatusContract.ExecutionDeterministicFallback,
            FallbackActive = true,
            DialogueOutcome = "OutOfScopeMedicalRequest",
            PrimaryIntent = AiChatIntentTypes.UnclearOrOutOfScope,
            ManualSelectionRequired = true
        };

        return pickingMedicine ? await SymptomSpecialtyRules.ApplyAsync(_dbContext, response, message, ct) : response;
    }
}
