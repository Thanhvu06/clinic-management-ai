using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
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
