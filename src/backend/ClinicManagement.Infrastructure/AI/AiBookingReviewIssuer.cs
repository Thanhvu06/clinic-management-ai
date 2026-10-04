using System.Globalization;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>Shared review issuance. Booking and confirmation consumption stay in the existing flow.</summary>
public sealed class AiBookingReviewIssuer
{
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<AiSpecialtyService> _logger;
    private readonly IAppointmentAvailabilityPolicy _availabilityPolicy;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiSessionSnapshotStore _snapshotStore;
    private readonly IAiBookingConfirmationStore _confirmationStore;

    public AiBookingReviewIssuer(AppDbContext dbContext, IDateTimeProvider dateTimeProvider,
        ILogger<AiSpecialtyService> logger, IAppointmentAvailabilityPolicy availabilityPolicy,
        ICurrentUserService currentUserService, IAiSessionSnapshotStore snapshotStore,
        IAiBookingConfirmationStore confirmationStore)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _availabilityPolicy = availabilityPolicy;
        _currentUserService = currentUserService;
        _snapshotStore = snapshotStore;
        _confirmationStore = confirmationStore;
    }

    public async Task<bool> IssueAsync(
        AiChatRequestDto request,
        AiChatResponseDto response,
        AiBookingDraftDto draft,
        string sessionId,
        string draftId,
        int draftVersion,
        string? contextSnapshotId,
        CancellationToken cancellationToken,
        ClinicManagement.Application.Appointments.Interfaces.SlotAvailabilityResult? verifiedSlot = null)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
        {
            response.DialogueOutcome = "ClarificationRequired";
            response.Message = "Bạn cần đăng nhập để xác nhận đặt lịch khám.";
            response.MissingFields = new List<string> { "Authentication" };
            return false;
        }

        var cleanSessionId = sessionId.Trim();
        var cleanDraftId = draftId.Trim();
        if (string.IsNullOrWhiteSpace(cleanSessionId) || string.IsNullOrWhiteSpace(cleanDraftId) || draftVersion < 1)
        {
            response.DialogueOutcome = "ClarificationRequired";
            response.Message = "Phiên hoặc bản nháp đặt lịch không hợp lệ. Vui lòng bắt đầu lại thao tác đặt lịch.";
            response.MissingFields = new List<string> { "Session", "Draft" };
            return false;
        }

        var patient = await _dbContext.Patients.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == currentUserId.Value, cancellationToken);
        var slot = verifiedSlot ?? await _availabilityPolicy.EvaluateSlotAvailabilityAsync(
            new ClinicManagement.Application.Appointments.Interfaces.SlotAvailabilityRequest
            {
                SlotId = draft.SlotId!.Value,
                DoctorId = draft.DoctorId,
                SpecialtyId = draft.SpecialtyId,
                PatientId = patient?.Id,
                CheckAiEnabledSpecialty = true
            }, cancellationToken);

        if (!slot.IsAvailable || !slot.SlotDate.HasValue || !slot.StartTime.HasValue || !slot.EndTime.HasValue)
        {
            response.DialogueOutcome = "ClarificationRequired";
            response.Message = slot.FailureReason ?? "Khung giờ đã thay đổi hoặc không còn khả dụng. Vui lòng chọn lại khung giờ mới.";
            response.MissingFields = new List<string> { "TimeSlot" };
            return false;
        }

        var touch = await _snapshotStore.TouchSessionAsync(
            cleanSessionId,
            currentUserId,
            cleanDraftId,
            draftVersion,
            facilityId: draft.FacilityId,
            cancellationToken: cancellationToken);
        if (!touch.IsAccepted)
        {
            response.DialogueOutcome = touch.ErrorCode == "DRAFT_CANCELLED" ? "DraftCancelled" : "SessionRejected";
            response.Message = touch.ErrorMessage ?? "Phiên làm việc không còn hợp lệ. Vui lòng bắt đầu bản nháp mới.";
            response.MissingFields = new List<string> { "Session" };
            return false;
        }

        var cleanSnapshotId = string.IsNullOrWhiteSpace(contextSnapshotId) ? null : contextSnapshotId.Trim();
        if (cleanSnapshotId == null)
        {
            var snapshot = await _snapshotStore.CreateSnapshotAsync(new CreateSnapshotRequest
            {
                UserId = currentUserId,
                SessionId = cleanSessionId,
                DraftId = cleanDraftId,
                DraftVersion = draftVersion,
                SpecialtyId = draft.SpecialtyId,
                DoctorId = draft.DoctorId,
                SlotDate = slot.SlotDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DoctorIds = new List<long> { draft.DoctorId!.Value },
                SlotIds = new List<long> { draft.SlotId!.Value }
            }, cancellationToken);
            cleanSnapshotId = snapshot.SnapshotId;
        }
        else
        {
            var snapshotValidation = await _snapshotStore.ValidateSnapshotAsync(new ValidateSnapshotRequest
            {
                SnapshotId = cleanSnapshotId,
                CurrentUserId = currentUserId,
                CurrentSessionId = cleanSessionId,
                CurrentDraftId = cleanDraftId,
                CurrentDraftVersion = draftVersion,
                CurrentSpecialtyId = draft.SpecialtyId,
                RequestedDoctorIds = new List<long> { draft.DoctorId!.Value },
                RequestedSlotIds = new List<long> { draft.SlotId!.Value },
                NowUtc = _dateTimeProvider.UtcNow
            }, cancellationToken);
            if (!snapshotValidation.IsValid)
            {
                response.DialogueOutcome = snapshotValidation.ErrorCode == "DRAFT_CANCELLED" ? "DraftCancelled" : "ClarificationRequired";
                response.Message = snapshotValidation.ErrorMessage ?? "Danh sách lựa chọn đã thay đổi. Vui lòng chọn lại rồi xác nhận.";
                response.MissingFields = new List<string> { "Selection" };
                return false;
            }
        }

        AiBookingConfirmationDto confirmation;
        try
        {
            confirmation = await _confirmationStore.CreateAsync(new CreateAiBookingConfirmationRequest
            {
                UserId = currentUserId.Value,
                SessionId = cleanSessionId,
                DraftId = cleanDraftId,
                DraftVersion = draftVersion,
                ContextSnapshotId = cleanSnapshotId,
                SpecialtyId = draft.SpecialtyId!.Value,
                DoctorId = draft.DoctorId!.Value,
                SlotId = draft.SlotId!.Value,
                FacilityId = draft.FacilityId,
                SlotDate = slot.SlotDate.Value,
                StartTime = slot.StartTime.Value,
                EndTime = slot.EndTime.Value,
                Reason = draft.Reason!
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist AI booking confirmation for {SessionId}/{DraftId}", cleanSessionId, cleanDraftId);
            response.DialogueOutcome = "ClarificationRequired";
            response.Message = "Không thể lưu lượt xác nhận đặt lịch. Vui lòng thử lại trên thông tin mới nhất.";
            response.MissingFields = new List<string> { "Confirmation" };
            return false;
        }

        draft.DraftId = cleanDraftId;
        draft.SessionId = cleanSessionId;
        draft.ContextSnapshotId = cleanSnapshotId;
        draft.ConfirmationId = confirmation.ConfirmationId;
        draft.SlotDate = slot.SlotDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        draft.StartTime = slot.StartTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        draft.EndTime = slot.EndTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        draft.IsComplete = true;
        response.ContextSnapshotId = cleanSnapshotId;
        response.SessionId = cleanSessionId;
        response.DraftId = cleanDraftId;
        response.DialogueOutcome = "PendingConfirmation";
        response.Message = "Thông tin lịch khám đã đầy đủ và sẵn sàng xác nhận. Bạn vui lòng bấm nút \"Xác nhận đặt lịch\" bên dưới để hoàn tất nhé:";

        var payload = new AiActionPayloadDto
        {
            ConfirmationId = confirmation.ConfirmationId,
            ContextSnapshotId = cleanSnapshotId,
            SessionId = cleanSessionId,
            DraftId = cleanDraftId,
            SpecialtyId = draft.SpecialtyId,
            FacilityId = draft.FacilityId,
            FacilityName = draft.FacilityName,
            SpecialtyName = draft.SpecialtyName,
            DoctorId = draft.DoctorId,
            DoctorName = draft.DoctorName,
            SlotId = draft.SlotId,
            SlotDate = draft.SlotDate,
            StartTime = draft.StartTime,
            EndTime = draft.EndTime,
            Reason = draft.Reason,
            DraftVersion = draftVersion
        };

        response.Actions.RemoveAll(a => a.Type == AiActionTypes.ConfirmBooking || a.Type == AiActionTypes.ReviewBooking);
        response.Actions.Add(new AiActionDto
        {
            Id = $"act-confirm-booking-{draft.SlotId}",
            Type = AiActionTypes.ConfirmBooking,
            Label = "Xác nhận đặt lịch",
            Description = $"Xác nhận khám với {draft.DoctorName} vào ngày {draft.SlotDate} ({draft.StartTime} - {draft.EndTime})",
            Style = "primary",
            RequiresAuthentication = true,
            RequiresConfirmation = true,
            DraftVersion = draftVersion,
            Payload = payload
        });
        response.Actions.Add(new AiActionDto
        {
            Id = $"act-review-booking-{draft.SlotId}",
            Type = AiActionTypes.ReviewBooking,
            Label = "Xem tóm tắt thông tin khám",
            Style = "secondary",
            RequiresAuthentication = true,
            RequiresConfirmation = false,
            DraftVersion = draftVersion,
            Payload = payload
        });

        return true;
    }

}
