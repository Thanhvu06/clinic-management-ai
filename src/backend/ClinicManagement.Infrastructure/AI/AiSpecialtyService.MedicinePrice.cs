using System.Text;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private const int MaxListedMedicines = 5;

    // Same rows as the public "Bảng giá thuốc" page (active medicine, active or no category).
    private async Task<AiChatResponseDto?> ResolveMedicinePriceAsync(AiChatRequestDto request, string message, int version, CancellationToken ct)
    {
        if (!MedicinePriceQuestion.IsPriceQuestion(message)) return null;
        var explicitMedicine = MedicinePriceQuestion.HasMedicineContext(message);
        if (!explicitMedicine && !MedicinePriceQuestion.MayNameMedicine(message)) return null;
        var terms = MedicinePriceQuestion.Terms(message);
        if (!explicitMedicine && terms.Count == 0) return null;

        var catalog = terms.Count == 0 ? [] : await _dbContext.Medicines.AsNoTracking()
            .Where(m => m.IsActive && (m.CategoryId == null || m.Category!.IsActive))
            .OrderBy(m => m.Name).ThenBy(m => m.Id)
            .Select(m => new { m.Name, m.ActiveIngredient, m.Unit, m.UnitPrice, m.IsPrescriptionRequired })
            .ToListAsync(ct);
        var found = catalog.Where(m => MedicinePriceQuestion.Matches(terms, m.Name, m.ActiveIngredient)).Take(MaxListedMedicines).ToList();
        if (found.Count == 0 && !explicitMedicine) return null;

        var text = new StringBuilder();
        if (found.Count > 0)
        {
            text.AppendLine("Giá thuốc trên Bảng giá thuốc của ClinicCare:");
            foreach (var medicine in found)
            {
                var price = medicine.UnitPrice is { } value ? $"{BusinessDisplay.Money(value)} / {medicine.Unit}" : "chưa niêm yết giá";
                text.AppendLine($"- **{medicine.Name}**: {price}{(medicine.IsPrescriptionRequired ? " (thuốc kê đơn)" : "")}");
            }
            text.Append("Bạn xem chi tiết và tình trạng còn hàng trên trang Bảng giá thuốc.");
        }
        else
        {
            text.Append(terms.Count == 0
                ? "Bạn cho ClinicCare biết tên thuốc cần tra giá, hoặc xem giá từng thuốc trên trang Bảng giá thuốc."
                : $"Không tìm thấy thuốc \"{string.Join(' ', terms)}\" trong Bảng giá thuốc của ClinicCare. Bạn có thể tra cứu trên trang Bảng giá thuốc hoặc hỏi nhà thuốc của phòng khám.");
        }

        var response = new AiChatResponseDto
        {
            Message = text.ToString(), DialogueOutcome = found.Count > 0 ? "MedicinePriceResolved" : "MedicinePriceNotFound",
            PrimaryIntent = AiChatIntentTypes.PricingInquiry, Urgency = "ROUTINE", AssistantStatus = "Online",
            ProviderStatus = "NotCalled", ProviderState = "NotCalled", PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            ExecutionMode = AiProviderStatusContract.ExecutionDeterministicFallback, FallbackActive = true
        };
        response.Actions.Add(new AiActionDto
        {
            Id = "act-open-medicine-prices", Type = AiActionTypes.ViewSpecialty, Label = "Mở Bảng giá thuốc", Style = "secondary",
            RequiresAuthentication = false, RequiresConfirmation = false, Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Medicines }
        });
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }
}
