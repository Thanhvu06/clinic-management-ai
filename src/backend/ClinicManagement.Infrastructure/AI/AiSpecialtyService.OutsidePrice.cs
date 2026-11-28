using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto?> ResolveOutsidePriceAsync(AiChatRequestDto request, string message, int version, CancellationToken ct)
    {
        if (!PatientChatPricing.HasOutsidePriceEvidence(message)) return null;
        if (await PatientChatPricing.NamesPricedSpecialtyAsync(_dbContext, message, ct))
            return await HandlePricingInquiryAsync(request, message, version, ct);
        var response = new AiChatResponseDto
        {
            Message = "ClinicCare chỉ có thông tin về phí khám và giá thuốc của phòng khám.",
            DialogueOutcome = "OutsideClinicPricing", PrimaryIntent = AiChatIntentTypes.PricingInquiry,
            Urgency = "ROUTINE", AssistantStatus = "Online", ProviderStatus = "NotCalled", ProviderState = "NotCalled",
            Actions = [OpenMedicinePrices()]
        };
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }
}
