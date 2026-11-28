using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Infrastructure.AI.Tools;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto?> ResolveAppointmentOrDoctorInformationAsync(
        AiChatRequestDto request, string message, int version, CancellationToken ct) =>
        await ResolvePatientAppointmentCodeAsync(request, message, version, ct) ??
        await ResolveDoctorInformationAsync(message, version, ct);

    private async Task<AiChatResponseDto?> ResolvePatientAppointmentCodeAsync(
        AiChatRequestDto request, string message, int version, CancellationToken ct)
    {
        var text = AiTextNormalizer.NormalizeForComparison(request.Message);
        var match = AppointmentCancelCode.Match(text);
        if (!match.Success || AppointmentChange(request.Message) is not null ||
            Regex.IsMatch(text, @"\b(?:dat lich|dat kham|dang ky kham)\b"))
            return null;

        if (await ResolveBookingPolicyAsync(request, message, version, ct) is { } policy)
            return policy;

        var response = PatientOperationResponse(AiChatIntentTypes.ViewAppointments, AiActionTypes.ViewMyAppointments,
            SafeRoutes.Appointments, "lịch hẹn");
        var userId = _currentUserService.UserId;
        var code = DisplayCodeSearch.Canonical(match.Value);
        var found = await _dbContext.Appointments.AsNoTracking()
            .Where(a => a.Patient.UserId == userId && userId.HasValue &&
                (a.ShortCode == code || a.AppointmentCode.ToUpper() == code))
            .Select(a => new { a.Id, Facility = a.Facility == null ? null : a.Facility.Name })
            .FirstOrDefaultAsync(ct);
        response.Message = $"Không tìm thấy lịch hẹn mã {code} trong tài khoản của bạn.";
        if (found is not null && _toolExecutor is not null)
        {
            var result = await _toolExecutor.ExecuteAsync(new AiToolInvocation
            {
                ToolName = "patient.get_appointment_detail",
                ToolVersion = "1.0",
                SessionId = request.SessionId,
                ArgumentsJson = JsonSerializer.Serialize(new { appointmentId = found.Id })
            }, ct);
            response.ToolResults.Add(result);
            if (result.Data is AppointmentDto appointment)
                response.Message = FormatAppointmentCodeResult(appointment, found.Facility);
        }

        response.Reply = response.Message;
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }

    private static string FormatAppointmentCodeResult(AppointmentDto appointment, string? facility) =>
        $"Lịch hẹn {appointment.AppointmentCode}\n" +
        $"Ngày khám: {appointment.AppointmentDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}\n" +
        $"Giờ khám: {appointment.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture)} – {appointment.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture)}\n" +
        $"Bác sĩ: {appointment.DoctorName}\n" +
        $"Chuyên khoa: {appointment.SpecialtyName}\n" +
        $"Cơ sở: {facility ?? "Chưa xác định"}\n" +
        $"Trạng thái: {CopilotDisplayText.DoctorStatus(appointment.Status)}";
}
