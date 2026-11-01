using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto?> TryResolveLocalPatientReadAsync(AiChatRequestDto request, string message, int version, CancellationToken ct)
    {
        var text = AiTextNormalizer.NormalizeForComparison(message);
        var readsOwnData = Regex.IsMatch(text, @"\b(?:xem|tra cuu|cua toi|cua minh)\b");
        var read = !readsOwnData ? null
            : Regex.IsMatch(text, @"\b(?:hoa don|bien lai|vien phi)\b") ? new PatientRead("patient.get_my_bills", AiActionTypes.ViewBills, SafeRoutes.Invoices, "hóa đơn", AiChatIntentTypes.ViewAppointments)
            : Regex.IsMatch(text, @"\b(?:don thuoc|toa thuoc)\b") ? new PatientRead("patient.get_my_prescriptions", AiActionTypes.ViewPrescriptions, SafeRoutes.Prescriptions, "đơn thuốc", AiChatIntentTypes.PrescriptionLookup)
            : Regex.IsMatch(text, @"\bket qua xet nghiem\b") ? new PatientRead("patient.get_my_diagnostic_results", AiActionTypes.ViewDiagnosticResults, SafeRoutes.DiagnosticResults, "kết quả xét nghiệm", AiChatIntentTypes.DiagnosticLookup)
            : Regex.IsMatch(text, @"\blich hen\b") ? new PatientRead("patient.get_my_appointments", AiActionTypes.ViewMyAppointments, SafeRoutes.Appointments, "lịch hẹn", AiChatIntentTypes.ViewAppointments)
            : null;
        if (read is null)
        {
            if (request.PendingSpecialtyId.HasValue || !SymptomSpecialtyRules.HasNamedSpecialty(message) ||
                !Regex.IsMatch(text, @"\bdat lich\b")) return null;
            var named = await SymptomSpecialtyRules.ApplyAsync(_dbContext, new AiChatResponseDto
            {
                AssistantStatus = "Online", ProviderStatus = "NotCalled", ProviderState = "NotCalled",
                PrimaryIntent = AiChatIntentTypes.SpecialtyRecommendation, DialogueOutcome = "NamedSpecialtyResolved",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion
            }, message, ct, namedOnly: true);
            return named.SpecialtySuggestions.Count > 0 ? WithDraftVersionSync(named, version) : null;
        }

        var response = read.Tool == "patient.get_my_appointments"
            ? await HandleViewAppointmentsAsync(request, version, ct)
            : new AiChatResponseDto
            {
                AssistantStatus = "Online", ProviderStatus = "NotCalled", ProviderState = "NotCalled",
                PrimaryIntent = read.Intent, DialogueOutcome = "PatientReadResolved", PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                Message = $"Bạn có thể xem {read.Label} của chính mình trong tài khoản.",
                Actions = [new AiActionDto
                {
                    Id = "act-own-data", Type = read.Action, Label = $"Xem {read.Label} của tôi", Style = "primary",
                    RequiresAuthentication = true, RequiresConfirmation = false, Payload = new AiActionPayloadDto { TargetUrl = read.Route }
                }]
            };
        if (_currentUserService.UserId.HasValue && _toolExecutor is not null)
        {
            var result = await _toolExecutor.ExecuteAsync(new AiToolInvocation
            {
                ToolName = read.Tool, ToolVersion = "1.0", ArgumentsJson = "{\"page\":1,\"pageSize\":20}", SessionId = request.SessionId
            }, ct);
            response.ToolResults.Add(result);
            if (read.Tool == "patient.get_my_appointments") response.Message += " " + result.DisplayText;
            else response.Message = result.DisplayText ?? result.Error?.Message ?? response.Message;
        }
        else response.Message = $"Vui lòng đăng nhập để xem {read.Label} của chính mình.";
        response.Reply = response.Message;
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }

    private sealed record PatientRead(string Tool, string Action, string Route, string Label, string Intent);

    private async Task<AiChatResponseDto> HandleViewAppointmentsAsync(
        AiChatRequestDto request,
        int nextDraftVersion,
        CancellationToken cancellationToken)
    {
        var response = new AiChatResponseDto
        {
            Urgency = "ROUTINE",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            DialogueOutcome = "NavigationInquiryResolved",
            PrimaryIntent = AiChatIntentTypes.ViewAppointments,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled",
            Message = "Bạn có thể xem danh sách lịch khám đã đặt và lịch sử khám bệnh của mình tại mục Lịch hẹn."
        };

        var currentUserId = _currentUserService?.UserId;
        long? verifiedAppointmentId = null;
        string? verifiedAppointmentCode = null;

        if (currentUserId.HasValue && currentUserId.Value != Guid.Empty)
        {
            var pat = await _dbContext.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == currentUserId.Value, cancellationToken);
            if (pat != null)
            {
                var apt = await _dbContext.Appointments.AsNoTracking()
                    .Where(a => a.PatientId == pat.Id && ClinicManagement.Domain.Enums.AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
                    .OrderByDescending(a => a.AppointmentDate)
                    .ThenByDescending(a => a.StartTime)
                    .FirstOrDefaultAsync(cancellationToken);
                if (apt != null)
                {
                    verifiedAppointmentId = apt.Id;
                    verifiedAppointmentCode = apt.AppointmentCode;
                    response.Message = $"Lịch khám gần nhất của bạn là mã **{apt.AppointmentCode}** vào ngày **{apt.AppointmentDate:dd/MM/yyyy}** ({apt.StartTime:HH\\:mm}). Bạn có thể mở chi tiết hoặc vào danh sách lịch hẹn để quản lý:";
                }
            }
        }

        response.Actions.Add(new AiActionDto
        {
            Id = "act-nav-my-appointments",
            Type = AiActionTypes.ViewMyAppointments,
            Label = "Xem lịch hẹn của tôi",
            Description = "Mở danh sách các lịch khám sắp tới và lịch sử khám bệnh",
            Style = "primary",
            RequiresAuthentication = true,
            RequiresConfirmation = false,
            Payload = new AiActionPayloadDto { TargetUrl = SafeRoutes.Appointments }
        });

        if (verifiedAppointmentId.HasValue)
        {
            response.Actions.Add(new AiActionDto
            {
                Id = "act-nav-appointment-detail",
                Type = AiActionTypes.OpenAppointmentDetail,
                Label = $"Xem chi tiết lịch {verifiedAppointmentCode}",
                Description = "Mở xem chi tiết cuộc hẹn gần nhất",
                Style = "secondary",
                RequiresAuthentication = true,
                RequiresConfirmation = false,
                Payload = new AiActionPayloadDto
                {
                    AppointmentId = verifiedAppointmentId,
                    AppointmentCode = verifiedAppointmentCode,
                    TargetUrl = $"{SafeRoutes.Appointments}?appointmentId={verifiedAppointmentId.Value}"
                }
            });
        }

        await PreserveExistingDraftAsync(request, response, cancellationToken);
        return WithDraftVersionSync(response, nextDraftVersion);
    }

}
