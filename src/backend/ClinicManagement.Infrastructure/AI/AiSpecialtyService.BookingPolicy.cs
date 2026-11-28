using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private ExpiryRulesOptions NoShowRule => _expiryOptions?.Value ?? new ExpiryRulesOptions();

    // Runs before every booking, read and specialty branch: a rule question that mentions
    // "đặt lịch" is otherwise taken as a booking request without a specialty.
    private async Task<AiChatResponseDto?> ResolveBookingPolicyAsync(AiChatRequestDto request, string message, int version, CancellationToken ct)
    {
        if (NoShowPolicyAnswer.IsLockQuestion(message))
            return await BookingPolicyResponseAsync(request, NoShowPolicyAnswer.Compose(message, NoShowRule), [NoShowPolicyAnswer.Source], version, ct);
        if (LoginLockPolicyAnswer.IsQuestion(message))
            return await BookingPolicyResponseAsync(request, LoginLockPolicyAnswer.Compose(_identityOptions?.Value.Lockout), [LoginLockPolicyAnswer.Source], version, ct);
        // Must precede the cancellation flow, which would read "hủy hẹn trước mấy tiếng" as a cancel command.
        if (CancellationPolicyAnswer.IsQuestion(message))
            return await BookingPolicyResponseAsync(request, CancellationPolicyAnswer.Compose(message), CancellationPolicyAnswer.Sources, version, ct);
        if (await ResolveMedicinePriceAsync(request, message, version, ct) is { } medicinePrice) return medicinePrice;
        if (await ResolveOutsidePriceAsync(request, message, version, ct) is { } outsidePrice) return outsidePrice;
        if (_knowledgeAnswerer is null || !NoShowPolicyAnswer.IsBookingPolicyQuestion(message)) return null;
        var answer = await _knowledgeAnswerer.AnswerAsync(message, ct);
        if (answer is null || AiProviderOutputGuard.Inspect(AiActorRole.Patient, answer.Text) is not null) return null;
        var response = await BookingPolicyResponseAsync(request, answer.Text, answer.Sources, version, ct);
        response.ProviderWasCalled = answer.ProviderWasCalled;
        response.ProviderAttemptCount = answer.ProviderWasCalled ? 1 : 0;
        return response;
    }

    private async Task<AiChatResponseDto> BookingPolicyResponseAsync(AiChatRequestDto request, string text,
        IReadOnlyList<KnowledgeSource> sources, int version, CancellationToken ct)
    {
        var response = new AiChatResponseDto
        {
            Message = text, DialogueOutcome = "KnowledgeExtractive", Sources = sources.ToList(), Urgency = "ROUTINE",
            PrimaryIntent = AiChatIntentTypes.UnclearOrOutOfScope, ProviderStatus = "NotCalled", ProviderState = "NotCalled",
            ExecutionMode = AiProviderStatusContract.ExecutionDeterministicFallback, FallbackActive = true
        };
        response.ToolResults.Add(new AiToolExecutionResult { ResultType = "knowledge_sources", Data = new { sources = response.Sources } });
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }
}
