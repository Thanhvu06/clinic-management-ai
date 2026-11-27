using System.Globalization;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto?> ResolveDoctorInformationAsync(string message, int version, CancellationToken ct)
    {
        if (PatientChatDoctorName.InformationName(message) is not { } name) return null;

        var normalizedName = NormalizeVietnamese(name);
        var matches = (await QueryActiveDoctorsAsync(ct)).Where(doctor =>
        {
            var fullName = NormalizeVietnamese(doctor.FullName);
            return fullName == normalizedName || !normalizedName.Contains(' ') && fullName.Split(' ')[^1] == normalizedName;
        }).ToList();
        var response = new AiChatResponseDto
        {
            DialogueOutcome = "DoctorInformation",
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled",
            ProviderState = "NotCalled",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion
        };
        if (matches.Count != 1)
        {
            response.Message = matches.Count == 0 ? $"Mình không tìm thấy bác sĩ {name} trong danh sách đang hoạt động."
                : $"Có nhiều bác sĩ khớp tên {name}. Bạn hãy xem danh sách hoặc cho mình biết họ tên đầy đủ.";
            response.Actions.Add(new AiActionDto
            {
                Id = "act-doctor-directory", Type = AiActionTypes.ViewDoctors, Label = "Xem danh sách bác sĩ",
                Style = "secondary", Payload = new() { TargetUrl = SafeRoutes.Doctors }
            });
            return WithDraftVersionSync(response, version);
        }

        var doctor = matches[0];
        var memberships = await _dbContext.DoctorSpecialties.AsNoTracking()
            .Where(item => item.DoctorId == doctor.DoctorId && item.Specialty.IsActive)
            .OrderByDescending(item => item.IsPrimary).ThenBy(item => item.SpecialtyId)
            .Select(item => new { item.SpecialtyId, item.Specialty.Name, item.Specialty.AiEnabled }).ToListAsync(ct);
        var specialtyNames = string.Join(", ", memberships.Select(item => item.Name));
        var title = string.IsNullOrWhiteSpace(doctor.AcademicTitle) ? "Chưa cập nhật" : doctor.AcademicTitle;
        var today = _dateTimeProvider.VietnamToday;
        var slots = await _availabilityPolicy.GetAvailableSlotsAsync(new BatchSlotAvailabilityRequest
        {
            DoctorId = doctor.DoctorId, FromDate = today, ToDate = today.AddDays(13)
        }, ct);
        var earliest = slots.OrderBy(slot => slot.SlotDate).ThenBy(slot => slot.StartTime).Take(3)
            .Select(slot => $"- {slot.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture)}–{slot.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture)} ngày {slot.SlotDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}")
            .ToList();
        var schedule = earliest.Count == 0 ? "Chưa có lịch trống trong 14 ngày tới." : $"Khung giờ trống gần nhất:\n{string.Join('\n', earliest)}";
        response.Message = $"**Bác sĩ {doctor.FullName}**\nHọc hàm/học vị: {title}\nChuyên khoa: {(specialtyNames.Length > 0 ? specialtyNames : "Chưa cập nhật")}\n\n{schedule}";
        foreach (var membership in memberships.Where(item => item.AiEnabled))
        {
            if ((await AppointmentFacilityResolver.GetEligibleAsync(_dbContext, doctor.DoctorId, membership.SpecialtyId, ct)).Count == 0)
                continue;
            response.Actions.Add(new AiActionDto
            {
                Id = "act-book-this-doctor", Type = AiActionTypes.StartBooking, Label = "Đặt lịch với bác sĩ này",
                Style = "primary", RequiresAuthentication = true,
                Payload = new() { DoctorId = doctor.DoctorId, SpecialtyId = membership.SpecialtyId }
            });
            break;
        }
        return WithDraftVersionSync(response, version);
    }
}
