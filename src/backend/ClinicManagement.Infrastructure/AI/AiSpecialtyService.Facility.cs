using System.Text;
using ClinicManagement.Application.AI.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto> HandleFacilityInquiryAsync(
        AiChatRequestDto request,
        string cleanMessage,
        int nextDraftVersion,
        CancellationToken cancellationToken)
    {
        var facility = await _dbContext.Facilities
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            DialogueOutcome = "FacilityInquiryResolved",
            PrimaryIntent = AiChatIntentTypes.FacilityInquiry,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled"
        };

        var facilityName = facility?.Name ?? "Phòng khám ClinicCare";
        var address = !string.IsNullOrWhiteSpace(facility?.Address)
            ? (!string.IsNullOrWhiteSpace(facility.City) ? $"{facility.Address.Trim()}, {facility.City.Trim()}" : facility.Address.Trim())
            : "Chưa cập nhật";
        var phone = !string.IsNullOrWhiteSpace(facility?.Phone) ? facility.Phone.Trim() : "Chưa cập nhật";
        var email = !string.IsNullOrWhiteSpace(facility?.Email) ? facility.Email.Trim() : "Chưa cập nhật";

        var sb = new StringBuilder();
        sb.AppendLine($"Thông tin liên hệ và cơ sở của **{facilityName}**:");
        sb.AppendLine($"- **Địa chỉ**: {address}");
        sb.AppendLine($"- **Hotline/SĐT**: {phone}");
        sb.AppendLine($"- **Email**: {email}");
        sb.AppendLine($"- **Thời gian làm việc**: {await GetOpeningHoursAsync(facility?.Code, cancellationToken)}");
        response.Message = sb.ToString();

        var contactAction = await CreateContactReceptionActionAsync(cancellationToken);
        response.Actions.Add(contactAction);

        response.Actions.Add(new AiActionDto
        {
            Id = "act-start-booking",
            Type = AiActionTypes.StartBooking,
            Label = "Đặt lịch khám ngay",
            Style = "primary",
            RequiresAuthentication = false,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.BookAppointment }
        });

        await PreserveExistingDraftAsync(request, response, cancellationToken);
        return WithDraftVersionSync(response, nextDraftVersion);
    }

    private async Task<AiActionDto> CreateContactReceptionActionAsync(CancellationToken cancellationToken)
    {
        var facility = await _dbContext.Facilities
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        string? phone = !string.IsNullOrWhiteSpace(facility?.Phone) ? facility.Phone.Trim() : null;
        string? address = !string.IsNullOrWhiteSpace(facility?.Address)
            ? (!string.IsNullOrWhiteSpace(facility.City) ? $"{facility.Address.Trim()}, {facility.City.Trim()}" : facility.Address.Trim())
            : null;

        return new AiActionDto
        {
            Id = "act-contact-reception",
            Type = AiActionTypes.ContactReception,
            Label = "Liên hệ bàn tiếp đón lễ tân",
            Description = "Thông tin liên hệ quầy tiếp đón của phòng khám",
            Style = "secondary",
            RequiresAuthentication = false,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto
            {
                PhoneNumber = phone,
                Address = address,
                FacilityName = facility?.Name,
                Reason = phone != null
                    ? $"Bàn tiếp đón tại: {address ?? "Phòng khám"}. Giờ làm việc: {await GetOpeningHoursAsync(facility?.Code, cancellationToken)}."
                    : "Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống."
            }
        };
    }

    private async Task<string> GetOpeningHoursAsync(string? facilityCode, CancellationToken cancellationToken)
    {
        var hours = await _dbContext.ClinicLocations.AsNoTracking()
            .Where(location => location.IsActive && location.Code == facilityCode)
            .Select(location => location.OpeningHours).FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(hours) ? "Chưa cập nhật" : hours.Trim();
    }
}
