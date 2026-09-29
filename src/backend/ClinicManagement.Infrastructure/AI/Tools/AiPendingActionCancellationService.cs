using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

/// <summary>Cancels only the caller-owned, server-stored preview; it never executes its write.</summary>
public sealed class AiPendingActionCancellationService : IAiPendingActionCancellationService
{
    private static readonly HashSet<string> StaffRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        AiActorRole.Receptionist.ToString(), AiActorRole.Doctor.ToString(),
        AiActorRole.DiagnosticTechnician.ToString(), AiActorRole.Pharmacist.ToString()
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IHttpContextAccessor _http;
    private readonly IDateTimeProvider _clock;
    private readonly IAiAuditService _audit;

    public AiPendingActionCancellationService(
        AppDbContext db,
        ICurrentUserService currentUser,
        IHttpContextAccessor http,
        IDateTimeProvider clock,
        IAiAuditService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _http = http;
        _clock = clock;
        _audit = audit;
    }

    public Task<AiToolExecutionResult> CancelPatientActionAsync(Guid actionId, string sessionId, CancellationToken cancellationToken = default) =>
        CancelAsync(actionId, sessionId, patientEndpoint: true, cancellationToken);

    public Task<AiToolExecutionResult> CancelRoleActionAsync(Guid actionId, string sessionId, CancellationToken cancellationToken = default) =>
        CancelAsync(actionId, sessionId, patientEndpoint: false, cancellationToken);

    private async Task<AiToolExecutionResult> CancelAsync(Guid actionId, string sessionId, bool patientEndpoint, CancellationToken cancellationToken)
    {
        var actorId = _currentUser.UserId;
        if (!actorId.HasValue || actorId.Value == Guid.Empty)
            return AiToolExecutionResult.Failed("AUTHENTICATION_REQUIRED", "Bạn cần đăng nhập lại để hủy thao tác.");
        if (string.IsNullOrWhiteSpace(sessionId))
            return AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần phiên hội thoại hợp lệ để hủy thao tác.");

        var action = await _db.AiPendingToolActions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActionId == actionId && x.UserId == actorId.Value, cancellationToken);
        if (action is null)
            return AiToolExecutionResult.Failed("ACTION_NOT_FOUND", "Thao tác không tồn tại hoặc không thuộc tài khoản này.");
        if (!string.Equals(action.SessionId, sessionId.Trim(), StringComparison.Ordinal))
            return AiToolExecutionResult.Failed("SESSION_MISMATCH", "Thao tác thuộc một phiên hội thoại khác.");
        if (!CanUseEndpoint(action.ActorRole, patientEndpoint))
            return AiToolExecutionResult.Failed("ROLE_MISMATCH", "Vai trò hiện tại không được phép hủy thao tác này.");

        if (action.State == AiPendingToolActionState.Cancelled || action.CancelledAtUtc.HasValue)
            return Cancelled(actionId, replay: true);

        // Once execution starts, expiry no longer makes cancellation safe: the
        // business mutation may already be in flight or committed.
        if (action.State is AiPendingToolActionState.Executing or AiPendingToolActionState.Completed)
            return AiToolExecutionResult.Failed("ACTION_NOT_CANCELLABLE", "Thao tác đang thực hiện hoặc đã hoàn tất nên không thể hủy.");

        var now = _clock.UtcNow;
        if (action.State == AiPendingToolActionState.Expired || action.ExpiresAtUtc <= now)
        {
            await _db.AiPendingToolActions
                .Where(x => x.ActionId == actionId && x.UserId == actorId.Value &&
                            x.State != AiPendingToolActionState.Executing && x.State != AiPendingToolActionState.Completed &&
                            x.State != AiPendingToolActionState.Cancelled)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.State, AiPendingToolActionState.Expired)
                    .SetProperty(x => x.ExecutionLeaseId, (Guid?)null)
                    .SetProperty(x => x.ExecutionLeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
            return AiToolExecutionResult.Failed("ACTION_EXPIRED", "Thao tác đã hết hạn; không thể hủy bằng preview cũ.");
        }

        if (action.State is not (AiPendingToolActionState.PendingConfirmation or AiPendingToolActionState.FailedRetryable))
            return AiToolExecutionResult.Failed("ACTION_NOT_CANCELLABLE", "Trạng thái thao tác hiện tại không cho phép hủy.");

        var changed = await _db.AiPendingToolActions
            .Where(x => x.ActionId == actionId && x.UserId == actorId.Value && x.SessionId == sessionId.Trim() &&
                        x.ExpiresAtUtc > now && x.CancelledAtUtc == null &&
                        (x.State == AiPendingToolActionState.PendingConfirmation || x.State == AiPendingToolActionState.FailedRetryable))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.State, AiPendingToolActionState.Cancelled)
                .SetProperty(x => x.CancelledAtUtc, now)
                .SetProperty(x => x.ExecutionLeaseId, (Guid?)null)
                .SetProperty(x => x.ExecutionLeaseExpiresAtUtc, (DateTime?)null), cancellationToken);

        if (changed == 0)
        {
            var latest = await _db.AiPendingToolActions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ActionId == actionId && x.UserId == actorId.Value, cancellationToken);
            if (latest?.State == AiPendingToolActionState.Cancelled || latest?.CancelledAtUtc.HasValue == true)
                return Cancelled(actionId, replay: true);
            if (latest?.State == AiPendingToolActionState.Expired || latest?.ExpiresAtUtc <= now)
                return AiToolExecutionResult.Failed("ACTION_EXPIRED", "Thao tác đã hết hạn; không thể hủy bằng preview cũ.");
            return AiToolExecutionResult.Failed("ACTION_NOT_CANCELLABLE", "Thao tác đã bắt đầu thực hiện hoặc trạng thái đã thay đổi.");
        }

        await _audit.LogActionAsync(new AiAuditLogEntry
        {
            UserId = actorId,
            SessionId = sessionId.Trim(),
            ActionType = "CancelPendingAction",
            Outcome = "cancelled",
            MetadataJson = JsonSerializer.Serialize(new
            {
                source = "ai_pending_action",
                operation = "cancel_pending_action",
                status = "cancelled",
                role = action.ActorRole
            })
        }, cancellationToken);
        return Cancelled(actionId, replay: false);
    }

    private bool CanUseEndpoint(string actorRole, bool patientEndpoint)
    {
        var principal = _http.HttpContext?.User;
        if (patientEndpoint)
            return actorRole.Equals(AiActorRole.Patient.ToString(), StringComparison.OrdinalIgnoreCase) && principal?.IsInRole("Patient") == true;
        return StaffRoles.Contains(actorRole) && principal?.IsInRole(actorRole) == true;
    }

    private static AiToolExecutionResult Cancelled(Guid actionId, bool replay) => new()
    {
        Status = "cancelled",
        ActionId = actionId.ToString(),
        ResultType = "pending_action_cancelled",
        DisplayText = "Đã hủy thao tác chờ xác nhận. Không có thay đổi nghiệp vụ nào được thực hiện.",
        Data = new { actionId, status = "cancelled" },
        IsIdempotentReplay = replay,
        DataSources = new[] { new AiToolDataSource("pending_action_store", "database") }
    };
}
