using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Tools;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public partial class AiSpecialtyService
{
    private async Task<AiChatResponseDto?> ResolvePatientChatReadAsync(AiChatRequestDto request, string message, int version, CancellationToken ct)
    {
        if (AppointmentChange(request.Message) is { } change)
            return await ResolveCancellationAsync(request, message, version, ct, change);
        if (IsShortDraftCancellation(message))
            return await ResolveCancellationAsync(request, message, version, ct);

        var text = AiTextNormalizer.NormalizeForComparison(message);
        var read = ClassifyFilteredRead(text);
        if (read is null)
            return await TryResolveLocalPatientReadAsync(request, message, version, ct);

        var filters = PatientReadFilters.FromText(text, read.Tool, _dateTimeProvider.VietnamToday, out var invalidDate);
        var upcoming = read.Tool == "patient.get_my_appointments" && Regex.IsMatch(text, @"\bsap toi\b");
        if (!filters.Active && !invalidDate && !upcoming)
            return await TryResolveLocalPatientReadAsync(request, message, version, ct);

        var response = PatientOperationResponse(read.Intent, read.Action, read.Route, read.Label);
        if (invalidDate)
            response.Message = "Khoảng ngày không hợp lệ. Bạn hãy nhập ngày theo định dạng dd/MM/yyyy.";
        else if (!_currentUserService.UserId.HasValue || _toolExecutor is null)
            response.Message = $"Vui lòng đăng nhập để xem {read.Label} của chính mình.";
        else
        {
            var result = await _toolExecutor.ExecuteAsync(new AiToolInvocation
            {
                ToolName = read.Tool, ToolVersion = "1.0", SessionId = request.SessionId,
                ArgumentsJson = upcoming ? """{"status":"Upcoming"}""" : filters.ArgumentsJson()
            }, ct);
            response.ToolResults.Add(result);
            response.Message = result.DisplayText ?? result.Error?.Message ?? response.Message;
        }

        response.Reply = response.Message;
        await PreserveExistingDraftAsync(request, response, ct);
        return WithDraftVersionSync(response, version);
    }

    private static PatientRead? ClassifyFilteredRead(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:dat lich|muon kham|dau|sot|kho tho|gia|bao nhieu|cach)\b")) return null;
        if (Regex.IsMatch(text, @"\b(?:hoa don|bien lai|vien phi)\b"))
            return new("patient.get_my_bills", AiActionTypes.ViewBills, SafeRoutes.Invoices, "hóa đơn", AiChatIntentTypes.ViewAppointments);
        if (Regex.IsMatch(text, @"\b(?:don thuoc|toa thuoc)\b"))
            return new("patient.get_my_prescriptions", AiActionTypes.ViewPrescriptions, SafeRoutes.Prescriptions, "đơn thuốc", AiChatIntentTypes.PrescriptionLookup);
        if (Regex.IsMatch(text, @"\bket qua\b") &&
            (Regex.IsMatch(text, @"\b(?:xet nghiem|can lam sang|sieu am|x[\s-]+quang)\b") || DiagnosticServiceAliases.Resolve(text).Count > 0))
            return new("patient.get_my_diagnostic_results", AiActionTypes.ViewDiagnosticResults, SafeRoutes.DiagnosticResults, "kết quả cận lâm sàng", AiChatIntentTypes.DiagnosticLookup);
        if (Regex.IsMatch(text, @"\b(?:luot kham|lich su kham)\b"))
            return new("patient.get_my_visits", AiActionTypes.ViewMyAppointments, SafeRoutes.Appointments, "lượt khám", AiChatIntentTypes.ViewAppointments);
        if (Regex.IsMatch(text, @"\b(?:lich hen|lich kham|lich) sap toi\b"))
            return new("patient.get_my_appointments", AiActionTypes.ViewMyAppointments, SafeRoutes.Appointments, "lịch hẹn", AiChatIntentTypes.ViewAppointments);
        return null;
    }

    private async Task<AiChatResponseDto> ResolveCancellationAsync(
        AiChatRequestDto request, string cleanMessage, int passiveDraftVersion, CancellationToken cancellationToken, string? appointmentChange = null)
    {
        var target = appointmentChange == "cancel" ? ReadAppointmentCancelTarget(request.Message) : default;
        if (target.IsSpecified &&
            await ResolveAppointmentCancelAsync(request, target, passiveDraftVersion, cancellationToken) is { } appointmentCancel)
            return appointmentCancel;

        var currentUserId = _currentUserService.UserId;
        var nowUtc = _dateTimeProvider.UtcNow;
        // A legacy draft containing selections but no persisted identity belongs
        // only to this client; clearing it must not cancel another saved tab.
        var hasLocalDraft = string.IsNullOrWhiteSpace(request.DraftId) && string.IsNullOrWhiteSpace(request.ContextSnapshotId)
            && (request.HasOpenBookingWizard || (request.PendingSpecialtyId.HasValue && string.IsNullOrWhiteSpace(request.SessionId)));
        var scopeResult = await _snapshotStore.TryResolveCancelScopeFromSnapshotAsync(
            request.ContextSnapshotId,
            currentUserId,
            request.SessionId,
            request.DraftId,
            nowUtc,
            cancellationToken);

        var activeDraftScopes = await _dbContext.AiSessions.AsNoTracking()
            .Where(s => currentUserId.HasValue && s.UserId == currentUserId && s.IsActive &&
                        s.ExpiresAtUtc > nowUtc && s.ActiveDraftId != null && s.ActiveDraftId != "")
            .Select(s => new { s.SessionId, DraftId = s.ActiveDraftId! })
            .Union(_dbContext.AiSelectionSnapshots.AsNoTracking()
                .Where(s => currentUserId.HasValue && s.UserId == currentUserId && !s.IsRevoked &&
                            s.ExpiresAtUtc > nowUtc && s.SessionId != null && s.SessionId != "" &&
                            s.DraftId != null && s.DraftId != "")
                .Select(s => new { SessionId = s.SessionId!, DraftId = s.DraftId! }))
            .Take(2)
            .ToListAsync(cancellationToken);

        var hasDraft = scopeResult.HasResolved || activeDraftScopes.Count > 0 || hasLocalDraft || request.PendingSpecialtyId.HasValue;
        if ((appointmentChange == "cancel" || (!hasDraft && IsShortDraftCancellation(cleanMessage))) &&
            await ResolveAppointmentCancelAsync(request, target, passiveDraftVersion, cancellationToken, hasDraft) is { } matchingAppointments)
            return matchingAppointments;

        if (!hasDraft && appointmentChange == "reschedule" && await ResolveAppointmentChangeAsync(request,
                AiTextNormalizer.NormalizeForComparison(cleanMessage), appointmentChange, passiveDraftVersion, cancellationToken) is { } reschedule)
            return reschedule;

        if (!scopeResult.HasResolved && !hasLocalDraft && activeDraftScopes.Count == 1 &&
            string.IsNullOrWhiteSpace(request.ContextSnapshotId))
        {
            var candidate = activeDraftScopes[0];
            if ((string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Trim() == candidate.SessionId) &&
                (string.IsNullOrWhiteSpace(request.DraftId) || request.DraftId.Trim() == candidate.DraftId))
            {
                scopeResult = await _snapshotStore.TryResolveCancelScopeFromSnapshotAsync(
                    null, currentUserId, candidate.SessionId, candidate.DraftId, nowUtc, cancellationToken);
            }
        }

        var hasResolvedScope = scopeResult.HasResolved;
        var cancelTargetSessionId = scopeResult.SessionId;
        var cancelTargetDraftId = scopeResult.DraftId;

        if (hasResolvedScope && cancelTargetSessionId != null && cancelTargetDraftId != null)
        {
            await _snapshotStore.InvalidateDraftSnapshotsForCancelAsync(
                cancelTargetDraftId,
                cancelTargetSessionId,
                currentUserId,
                facilityId: null,
                nowUtc: nowUtc,
                cancellationToken: cancellationToken);

            if (currentUserId.HasValue)
            {
                await _confirmationStore.RevokeForDraftAsync(
                    currentUserId.Value,
                    cancelTargetSessionId,
                    cancelTargetDraftId,
                    cancellationToken);
            }

            await _auditService.LogActionAsync(new AiAuditLogEntry
            {
                UserId = currentUserId,
                SessionId = cancelTargetSessionId,
                DraftId = cancelTargetDraftId,
                DraftVersion = passiveDraftVersion,
                ActionType = "CancelDraft",
                Outcome = "DraftCancelled"
            }, cancellationToken);

            var cancelResponse = new AiChatResponseDto
            {
                Message = "Đã hủy bản nháp đặt lịch hiện tại. Bạn có cần hỗ trợ gì khác không?",
                DialogueOutcome = "DraftCancelled",
                PrimaryIntent = AiChatIntentTypes.CancelDraft,
                AssistantStatus = "Online",
                ProviderStatus = "NotCalled",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                BookingDraft = null,
                SessionId = cancelTargetSessionId,
                DraftId = null
            };
            return WithDraftVersionSync(cancelResponse, passiveDraftVersion);
        }

        if ((!hasLocalDraft && activeDraftScopes.Count > 0) ||
            !string.IsNullOrWhiteSpace(request.DraftId) ||
            !string.IsNullOrWhiteSpace(request.ContextSnapshotId))
        {
            await _auditService.LogActionAsync(new AiAuditLogEntry
            {
                UserId = currentUserId,
                SessionId = request.SessionId,
                DraftId = request.DraftId,
                DraftVersion = passiveDraftVersion,
                ActionType = "CancelDraft",
                Outcome = "ClarificationRequired",
                ErrorCode = "AMBIGUOUS_SCOPE"
            }, cancellationToken);

            // Fail closed: do NOT revoke snapshots across sessions/drafts when target scope cannot be uniquely determined
            var failClosedCancelResponse = new AiChatResponseDto
            {
                Message = "ClinicCare chưa xác định được bản nháp cần hủy. Nếu muốn hủy lịch hẹn đã đặt, hãy gửi ngày hoặc mã lịch hẹn, hoặc mở Lịch hẹn của tôi.",
                ClarificationPrompt = "Bạn muốn hủy lịch hẹn ngày nào hoặc có mã nào?",
                Actions = PatientOperationResponse(AiChatIntentTypes.CancelDraft, AiActionTypes.ViewMyAppointments, SafeRoutes.Appointments, "lịch hẹn").Actions,
                DialogueOutcome = "ClarificationRequired",
                PrimaryIntent = AiChatIntentTypes.CancelDraft,
                AssistantStatus = "Online",
                ProviderStatus = "NotCalled",
                PromptVersion = GeminiAiProvider.CurrentPromptVersion,
                BookingDraft = null,
                SessionId = !string.IsNullOrWhiteSpace(request.SessionId) ? request.SessionId.Trim() : null,
                DraftId = null
            };
            return WithDraftVersionSync(failClosedCancelResponse, passiveDraftVersion);
        }

        // A legacy client-only draft can still be cleared. Otherwise, distinguish
        // real upcoming appointments from an empty account instead of claiming cancellation.
        var today = _dateTimeProvider.VietnamToday;
        var time = _dateTimeProvider.VietnamTime;
        var hasUpcomingAppointment = currentUserId.HasValue && await _dbContext.Appointments.AsNoTracking()
            .AnyAsync(a => a.Patient.UserId == currentUserId.Value &&
                ClinicManagement.Domain.Enums.AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status) &&
                (a.AppointmentDate > today || (a.AppointmentDate == today && a.StartTime > time)), cancellationToken);

        var statelessCancelResponse = new AiChatResponseDto
        {
            Message = hasLocalDraft
                ? "Đã hủy bản nháp đặt lịch hiện tại. Bạn có cần hỗ trợ gì khác không?"
                : hasUpcomingAppointment
                    ? "Bạn có lịch hẹn sắp tới. Vui lòng mở Lịch hẹn của tôi, chọn lịch hẹn cần hủy và gửi yêu cầu hủy."
                    : "Hiện bạn chưa có lịch hẹn hay yêu cầu đặt lịch nào đang chờ để hủy.",
            DialogueOutcome = hasLocalDraft ? "DraftCancelled" : hasUpcomingAppointment ? "NavigationInquiryResolved" : "NothingToCancel",
            PrimaryIntent = AiChatIntentTypes.CancelDraft,
            AssistantStatus = "Online",
            ProviderStatus = "NotCalled",
            PromptVersion = GeminiAiProvider.CurrentPromptVersion,
            BookingDraft = null,
            SessionId = request.SessionId,
            DraftId = null
        };
        if (!hasLocalDraft && hasUpcomingAppointment)
        {
            await AddNavigationActionsIfRequestedAsync(cleanMessage, string.Empty, AiActionTypes.ViewMyAppointments,
                statelessCancelResponse, cancellationToken);
        }
        return WithDraftVersionSync(statelessCancelResponse, passiveDraftVersion);
    }

    private string? AppointmentChange(string message)
    {
        var text = AiTextNormalizer.NormalizeForComparison(message);
        var match = Regex.Match(text, @"\b(huy|doi) (?:lich(?: hen| kham)?(?! (?:truc|lam viec|cong tac)\b)|cuoc hen)\b|\b(huy) hen\b");
        if (!match.Success || Regex.IsMatch(text, @"\b(?:khong (?:muon )?|dung )(?:huy|doi)\b"))
            return IsTargetedAppointmentCancellation(text) ? "cancel" : null;
        if (match.Groups[1].Value == "doi") return "reschedule";
        return Regex.IsMatch(AiTextNormalizer.Normalize(message), @"\b(?:hủy|huỷ)\b", RegexOptions.IgnoreCase)
            || ReadAppointmentCancelTarget(message).IsSpecified ? "cancel" : null;
    }

    private async Task<AiChatResponseDto?> ResolveAppointmentChangeAsync(
        AiChatRequestDto request, string text, string operation, int version, CancellationToken ct)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return null;
        var own = _dbContext.Appointments.AsNoTracking().Where(a => a.Patient.UserId == userId.Value);
        var eligible = PatientCopilotToolHandler.FutureAppointments(own, _dateTimeProvider)
            .Where(a => a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed);
        var appointments = await eligible.OrderBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.DoctorId, a.SpecialtyId, a.AppointmentSlotId })
            .ToListAsync(ct);

        var cancel = operation == "cancel";
        var label = cancel ? "Yêu cầu hủy" : "Yêu cầu đổi lịch";
        var verb = cancel ? "hủy" : "đổi";
        var response = PatientOperationResponse(cancel ? AiChatIntentTypes.CancelDraft : AiChatIntentTypes.ViewAppointments,
            AiActionTypes.ViewMyAppointments, SafeRoutes.Appointments, "lịch hẹn");
        response.Message = appointments.Count == 0
            ? "Bạn không có lịch hẹn sắp tới nào có thể hủy/đổi."
            : $"Có {appointments.Count} lịch hẹn sắp tới có thể {verb}. Chọn lịch bạn muốn {verb}.";
        var requestedDay = !cancel && Regex.IsMatch(text, @"\bngay mai\b")
            ? _dateTimeProvider.VietnamToday.AddDays(1)
            : PatientReadFilters.FromText(text, "", _dateTimeProvider.VietnamToday, out _).FromDate;
        response.Actions.InsertRange(0, appointments.Select(a => new AiActionDto
        {
            Id = $"patient-change-{operation}-{a.Id}",
            Type = cancel ? "RequestCancellation" : "RequestReschedule",
            Label = $"{label} · {a.AppointmentDate:dd/MM/yyyy} {a.StartTime:HH\\:mm}",
            Style = "secondary", RequiresAuthentication = true,
            Payload = new AiActionPayloadDto
            {
                AppointmentId = a.Id, AppointmentCode = a.AppointmentCode, TargetUrl = SafeRoutes.Appointments,
                DoctorId = a.DoctorId, SpecialtyId = a.SpecialtyId, SlotId = a.AppointmentSlotId,
                SlotDate = requestedDay?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            }
        }));
        if (appointments.Count == 0) response.DialogueOutcome = "NothingToCancel";
        response.Reply = response.Message;
        return WithDraftVersionSync(response, version);
    }

    private static AiChatResponseDto PatientOperationResponse(string intent, string action, string route, string label) => new()
    {
        AssistantStatus = "Online", ProviderStatus = "NotCalled", ProviderState = "NotCalled",
        PrimaryIntent = intent, DialogueOutcome = "PatientReadResolved", PromptVersion = GeminiAiProvider.CurrentPromptVersion,
        Actions = [new AiActionDto
        {
            Id = "act-own-data", Type = action, Label = $"Xem {label} của tôi", Style = "primary",
            RequiresAuthentication = true, Payload = new AiActionPayloadDto { TargetUrl = route }
        }]
    };
}
