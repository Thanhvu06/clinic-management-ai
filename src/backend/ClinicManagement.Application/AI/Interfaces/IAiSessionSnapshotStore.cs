using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.Application.AI.Interfaces;

public class AiSelectionSnapshotDto
{
    public string SnapshotId { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? DraftId { get; set; }
    public int? DraftVersion { get; set; }
    public long? FacilityId { get; set; }
    public long? SpecialtyId { get; set; }
    public long? DoctorId { get; set; }
    public string? SlotDate { get; set; }
    public List<long> DoctorIds { get; set; } = new();
    public List<long> SlotIds { get; set; } = new();
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsRevoked { get; set; }
}

public class CreateSnapshotRequest
{
    public Guid? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? DraftId { get; set; }
    public int? DraftVersion { get; set; }
    public long? FacilityId { get; set; }
    public long? SpecialtyId { get; set; }
    public long? DoctorId { get; set; }
    public string? SlotDate { get; set; }
    public List<long>? DoctorIds { get; set; }
    public List<long>? SlotIds { get; set; }
    public TimeSpan? Ttl { get; set; }
}

public class ValidateSnapshotRequest
{
    public string? SnapshotId { get; set; }
    public Guid? CurrentUserId { get; set; }
    public string? CurrentSessionId { get; set; }
    public string? CurrentDraftId { get; set; }
    public int? CurrentDraftVersion { get; set; }
    public long? CurrentFacilityId { get; set; }
    public long? CurrentSpecialtyId { get; set; }
    public List<long>? RequestedDoctorIds { get; set; }
    public List<long>? RequestedSlotIds { get; set; }
    public DateTime NowUtc { get; set; }
}

public class SnapshotValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public AiSelectionSnapshotDto? Snapshot { get; set; }

    public static SnapshotValidationResult Success(AiSelectionSnapshotDto snapshot) =>
        new() { IsValid = true, Snapshot = snapshot };

    public static SnapshotValidationResult Fail(string errorCode, string errorMessage) =>
        new() { IsValid = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

public class ResolveCancelScopeResult
{
    public bool HasResolved { get; set; }
    public string? SessionId { get; set; }
    public string? DraftId { get; set; }
}

public class AiSessionTouchResult
{
    public bool IsAccepted { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static AiSessionTouchResult Accepted() => new() { IsAccepted = true };
    public static AiSessionTouchResult Rejected(string code, string message) => new()
    {
        IsAccepted = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}

public interface IAiSessionSnapshotStore
{
    Task<AiSelectionSnapshotDto> CreateSnapshotAsync(CreateSnapshotRequest request, CancellationToken cancellationToken = default);
    Task<SnapshotValidationResult> ValidateSnapshotAsync(ValidateSnapshotRequest request, CancellationToken cancellationToken = default);
    Task InvalidateDraftSnapshotsForCancelAsync(string? draftId, string? sessionId, Guid? userId, long? facilityId = null, DateTime? nowUtc = null, CancellationToken cancellationToken = default);
    Task<bool> IsDraftCancelledAsync(string? draftId, Guid? userId = null, string? sessionId = null, DateTime? nowUtc = null, CancellationToken cancellationToken = default);
    Task<ResolveCancelScopeResult> TryResolveCancelScopeFromSnapshotAsync(string? snapshotId, Guid? userId, string? requestedSessionId, string? requestedDraftId, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> HasAnyActiveSnapshotForUserAsync(Guid? userId, DateTime? nowUtc = null, CancellationToken cancellationToken = default);
    Task<AiSessionTouchResult> TouchSessionAsync(string sessionId, Guid? userId, string? draftId, int? draftVersion, long? facilityId, CancellationToken cancellationToken = default);
    Task PurgeExpiredRecordsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
