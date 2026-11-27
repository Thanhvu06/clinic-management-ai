using System.Globalization;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Tools;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private static readonly Regex AppointmentCancelCode = new(@"\bapt-[a-z0-9]+(?:-[a-z0-9]+)*\b",
        RegexOptions.CultureInvariant);

    private static bool IsShortDraftCancellation(string message) =>
        AiTextNormalizer.Normalize(message).ToLowerInvariant().TrimEnd('!', '.', '?', '…', ' ') is
            "hủy" or "huỷ" or "thôi" or "không đặt nữa";

    private static bool IsTargetedAppointmentCancellation(string text) =>
        Regex.IsMatch(text, @"^(?:(?:(?:toi|minh|em) muon|hay|xin|giup toi) )?huy (?:ma )?(?:apt-|ngay |hom nay\b|(?:sang|trua|chieu|toi) mai\b|\d{1,2}[/\-])");

    private AppointmentCancelTarget ReadAppointmentCancelTarget(string message)
    {
        // The date segment of an APT code is removed by the provider's numeric redaction.
        var text = AiTextNormalizer.NormalizeForComparison(message);
        var code = AppointmentCancelCode.Match(text);
        var dateText = code.Success ? text.Remove(code.Index, code.Length) : text;
        var day = AppointmentCancelDate.Parse(dateText, _dateTimeProvider.VietnamToday, out var invalidDate);
        return new(day, invalidDate, code.Success ? code.Value.ToUpperInvariant() : null);
    }

    private async Task<AiChatResponseDto?> ResolveAppointmentCancelAsync(
        AiChatRequestDto request, AppointmentCancelTarget target, int version, CancellationToken ct, bool clarifyIfFound = false)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return null;

        var day = target.Day;
        var response = PatientOperationResponse(AiChatIntentTypes.CancelDraft, AiActionTypes.ViewMyAppointments, SafeRoutes.Appointments, "lịch hẹn");
        response.SessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim();

        if (target.InvalidDate)
        {
            response.Message = "Ngày bạn nhập không hợp lệ. Hãy nhập theo dạng dd/MM/yyyy, ví dụ \"hủy lịch hẹn ngày 27/11/2026\".";
            response.Reply = response.Message;
            await PreserveExistingDraftAsync(request, response, ct);
            return WithDraftVersionSync(response, version);
        }

        var own = _dbContext.Appointments.AsNoTracking().Where(a => a.Patient.UserId == userId.Value);
        var eligible = PatientCopilotToolHandler.FutureAppointments(own, _dateTimeProvider)
            .Where(a => a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed);
        if (day.HasValue) eligible = eligible.Where(a => a.AppointmentDate == day.Value);
        if (target.Code is not null) eligible = eligible.Where(a => a.AppointmentCode == target.Code);
        var appointments = await eligible.OrderBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).ThenBy(a => a.Id)
            .Select(a => new CancelCandidate(a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.Status,
                a.Specialty.Name, _dbContext.Users.Where(u => u.Id == a.Doctor.UserId).Select(u => u.FullName).FirstOrDefault()))
            .Take(21)
            .ToListAsync(ct);
        if (clarifyIfFound && appointments.Count == 0) return null;

        var onDay = day.HasValue ? $" vào ngày {day.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}" : string.Empty;
        if (clarifyIfFound)
        {
            response.DialogueOutcome = "ClarificationRequired";
            response.ClarificationPrompt = "Bạn muốn hủy bản nháp đang đặt dở hay một lịch hẹn đã đặt?";
            response.Message = response.ClarificationPrompt + " Nhắn \"hủy bản nháp\" hoặc chọn lịch bên dưới.";
        }
        else if (appointments.Count == 0)
        {
            response.DialogueOutcome = "NothingToCancel";
            response.Message = target.Code is not null
                ? "Không tìm thấy lịch hẹn nào có mã đã nhập và còn có thể hủy. Bạn có thể kiểm tra trong Lịch hẹn của tôi."
                : day.HasValue
                ? $"Không tìm thấy lịch hẹn nào phù hợp{onDay} để hủy. Bạn có thể mở Lịch hẹn của tôi để kiểm tra các lịch đang có."
                : "Bạn không có lịch hẹn sắp tới nào có thể hủy/đổi.";
        }
        else if (appointments.Count == 1 && target.IsSpecified)
        {
            var only = appointments[0];
            response.Message = $"Tìm thấy 1 lịch hẹn phù hợp: {only.Describe()}. " + (only.Status == AppointmentStatus.Pending
                ? "Lịch đang Chờ xác nhận nên sẽ được hủy ngay sau khi bạn xác nhận. Bạn có chắc muốn hủy lịch này không?"
                : "Lịch đã được xác nhận nên ClinicCare sẽ gửi yêu cầu hủy để lễ tân duyệt. Bạn có chắc muốn hủy lịch này không?");
        }
        else if (appointments.Count <= 20)
        {
            response.Message = $"Có {appointments.Count} lịch hẹn sắp tới{onDay} có thể hủy. Chọn lịch bạn muốn hủy.";
        }
        else
        {
            response.Message = "Có hơn 20 lịch hẹn có thể hủy. Hãy chọn một lịch bên dưới hoặc gửi ngày hay mã lịch hẹn.";
        }

        response.Actions.InsertRange(0, appointments.Take(20).Select(a => new AiActionDto
        {
            Id = $"patient-change-cancel-{a.Id}",
            Type = AiActionTypes.RequestCancellation,
            Label = appointments.Count == 1 && !clarifyIfFound
                ? a.Status == AppointmentStatus.Pending ? "Hủy lịch này" : "Gửi yêu cầu hủy lịch này"
                : $"{(a.Status == AppointmentStatus.Pending ? "Hủy ngay" : "Yêu cầu hủy")} · {a.When()} · {a.AppointmentCode}",
            Style = "secondary", RequiresAuthentication = true,
            Payload = new AiActionPayloadDto { AppointmentId = a.Id, AppointmentCode = a.AppointmentCode, TargetUrl = SafeRoutes.Appointments }
        }));
        response.Reply = response.Message;
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }

    private readonly record struct AppointmentCancelTarget(DateOnly? Day, bool InvalidDate, string? Code)
    {
        public bool IsSpecified => Day.HasValue || InvalidDate || Code is not null;
    }

    private sealed record CancelCandidate(long Id, string AppointmentCode, DateOnly Date, TimeOnly Start, AppointmentStatus Status,
        string? Specialty, string? Doctor)
    {
        public string When() => $"{Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} {Start:HH\\:mm}";

        public string Describe() => string.Join(", ", new[]
        {
            $"ngày {Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} lúc {Start:HH\\:mm}", Specialty,
            string.IsNullOrWhiteSpace(Doctor) ? null : $"BS {Doctor}"
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
