using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto?> ResolvePatientAppointmentCodeAsync(
        AiChatRequestDto request, int version, CancellationToken ct)
    {
        var text = AiTextNormalizer.NormalizeForComparison(request.Message);
        if (!Regex.IsMatch(text, @"\b(?:xem|tra|kiem tra)\b"))
            return null;

        var match = AppointmentCancelCode.Match(text);
        if (!match.Success)
            return null;

        var response = PatientOperationResponse(AiChatIntentTypes.ViewAppointments, AiActionTypes.ViewMyAppointments,
            SafeRoutes.Appointments, "lịch hẹn");
        var userId = _currentUserService.UserId;
        var code = DisplayCodeSearch.Canonical(match.Value);
        var id = await _dbContext.Appointments.AsNoTracking()
            .Where(a => userId.HasValue && a.Patient.UserId == userId.Value &&
                (a.ShortCode == code || a.AppointmentCode.ToUpper() == code))
            .Select(a => (long?)a.Id).FirstOrDefaultAsync(ct);
        response.Message = "Không tìm thấy lịch hẹn có mã đã nhập trong lịch hẹn của bạn.";
        if (id.HasValue && _toolExecutor is not null)
        {
            var result = await _toolExecutor.ExecuteAsync(new AiToolInvocation
            {
                ToolName = "patient.get_appointment_detail",
                ToolVersion = "1.0",
                SessionId = request.SessionId,
                ArgumentsJson = JsonSerializer.Serialize(new { appointmentId = id.Value })
            }, ct);
            response.ToolResults.Add(result);
            response.Message = result.DisplayText ?? result.Error?.Message ?? response.Message;
        }

        response.Reply = response.Message;
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }
}
