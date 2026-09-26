using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI.Persistence;

public class EfAiSessionSnapshotStore : IAiSessionSnapshotStore
{
    private const int MaxIdentifierLength = 128;
    private const int MaxSnapshotListSize = 100;
    private static readonly TimeSpan DefaultSnapshotTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxSnapshotTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(24);
    // Cancellation tombstones are terminal anti-replay records, not TTL cache rows.
    // DateTime.MaxValue is supported by SQL Server datetime2 and keeps an exact
    // cancelled scope blocked after sessions/snapshots have been purged.
    private static readonly DateTime PermanentCancellationExpiryUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<EfAiSessionSnapshotStore> _logger;
    private readonly IFacilityAuthorizationService? _facilityAuthService;

    public EfAiSessionSnapshotStore(
        AppDbContext dbContext,
        IDateTimeProvider dateTimeProvider,
        ILogger<EfAiSessionSnapshotStore> logger,
        IFacilityAuthorizationService? facilityAuthService = null)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _facilityAuthService = facilityAuthService;
    }

    public async Task<AiSelectionSnapshotDto> CreateSnapshotAsync(CreateSnapshotRequest request, CancellationToken cancellationToken = default)
    {
        var now = _dateTimeProvider.UtcNow;
        var ttl = request.Ttl ?? TimeSpan.FromMinutes(15);
        if (ttl <= TimeSpan.Zero || ttl > MaxSnapshotTtl)
        {
            ttl = DefaultSnapshotTtl;
        }
        var expiresAt = now.Add(ttl);

        var cleanSessionId = NormalizeIdentifier(request.SessionId);
        var cleanDraftId = NormalizeIdentifier(request.DraftId);
        if (request.DoctorIds?.Count > MaxSnapshotListSize || request.SlotIds?.Count > MaxSnapshotListSize)
        {
            throw new ArgumentException("Snapshot selection lists are too large.", nameof(request));
        }

        var randomHex = RandomNumberGenerator.GetHexString(8);
        var snapshotId = $"snap_{Guid.NewGuid():N}_{randomHex}";

        var cleanDoctorIds = request.DoctorIds?.Distinct().ToList() ?? new List<long>();
        var cleanSlotIds = request.SlotIds?.Distinct().ToList() ?? new List<long>();

        var snapshot = new AiSelectionSnapshot
        {
            SnapshotId = snapshotId,
            UserId = request.UserId,
            SessionId = cleanSessionId,
            DraftId = cleanDraftId,
            DraftVersion = request.DraftVersion,
            FacilityId = request.FacilityId,
            SpecialtyId = request.SpecialtyId,
            DoctorId = request.DoctorId,
            SlotDate = request.SlotDate,
            DoctorIdsJson = JsonSerializer.Serialize(cleanDoctorIds),
            SlotIdsJson = JsonSerializer.Serialize(cleanSlotIds),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            IsRevoked = false
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                // Serializable is deliberate here: cancellation and creation must observe a
                // single database order. The cancellation marker is read in the same
                // transaction that inserts the snapshot, so it cannot be bypassed by a
                // second application instance.
                if (cleanDraftId != null && cleanSessionId != null &&
                    await IsDraftCancelledInternalAsync(cleanDraftId, request.UserId, cleanSessionId, cancellationToken))
                {
                    _logger.LogWarning("Snapshot creation blocked: draft {DraftId} in session {SessionId} is cancelled", cleanDraftId, cleanSessionId);
                    throw new InvalidOperationException("Cannot create snapshot for cancelled draft.");
                }

                _dbContext.AiSelectionSnapshots.Add(snapshot);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                break;
            }
            catch (Exception ex) when (attempt < 3 && IsUniqueOrSerializationConflict(ex))
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist AI selection snapshot {SnapshotId}", snapshotId);
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                throw;
            }
        }

        return MapToDto(snapshot, cleanDoctorIds, cleanSlotIds);
    }

    public async Task<SnapshotValidationResult> ValidateSnapshotAsync(ValidateSnapshotRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SnapshotId))
        {
            return SnapshotValidationResult.Fail("MISSING_SNAPSHOT", "Yêu cầu thiếu snapshot danh sách lựa chọn. Vui lòng tải lại lựa chọn mới.");
        }

        try
        {
            var cleanSnapshotId = request.SnapshotId.Trim();
            if (cleanSnapshotId.Length > 64)
            {
                return SnapshotValidationResult.Fail("INVALID_SNAPSHOT", "Mã snapshot không hợp lệ.");
            }
            var snapshot = await _dbContext.AiSelectionSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SnapshotId == cleanSnapshotId, cancellationToken);

            if (snapshot == null)
            {
                return SnapshotValidationResult.Fail("NOT_FOUND", "Danh sách lựa chọn không tồn tại hoặc đã hết hạn. Vui lòng tải lại danh sách mới.");
            }

            if (snapshot.ExpiresAtUtc <= request.NowUtc)
            {
                return SnapshotValidationResult.Fail("EXPIRED", "Danh sách lựa chọn đã hết hạn sau 15 phút. Vui lòng tải lại danh sách mới.");
            }

            if (snapshot.IsRevoked)
            {
                return SnapshotValidationResult.Fail("REVOKED", "Danh sách lựa chọn đã bị thu hồi. Vui lòng tải lại danh sách mới.");
            }

            if (snapshot.UserId != request.CurrentUserId)
            {
                return SnapshotValidationResult.Fail("USER_MISMATCH", "Danh sách lựa chọn thuộc phiên người dùng khác. Vui lòng chọn trên phiên của bạn.");
            }

            var cleanCurrentSessionId = NormalizeIdentifier(request.CurrentSessionId);
            var cleanSnapshotSessionId = NormalizeIdentifier(snapshot.SessionId);
            if (cleanSnapshotSessionId != null || cleanCurrentSessionId != null)
            {
                if (!string.Equals(cleanSnapshotSessionId, cleanCurrentSessionId, StringComparison.Ordinal))
                {
                    return SnapshotValidationResult.Fail("SESSION_MISMATCH", "Danh sách lựa chọn thuộc phiên làm việc (tab/session) khác. Vui lòng chọn trên phiên hiện tại.");
                }
            }

            var cleanCurrentDraftId = NormalizeIdentifier(request.CurrentDraftId);
            var cleanSnapshotDraftId = NormalizeIdentifier(snapshot.DraftId);

            // Check if draft has been cancelled
            var effectiveSessionId = cleanCurrentSessionId ?? cleanSnapshotSessionId;
            if (cleanSnapshotDraftId != null && await IsDraftCancelledInternalAsync(cleanSnapshotDraftId, request.CurrentUserId, cleanSnapshotSessionId, cancellationToken))
            {
                return SnapshotValidationResult.Fail("DRAFT_CANCELLED", "Danh sách lựa chọn thuộc bản nháp đã hủy. Vui lòng chọn lại trên bản nháp mới.");
            }

            if (cleanCurrentDraftId != null && await IsDraftCancelledInternalAsync(cleanCurrentDraftId, request.CurrentUserId, effectiveSessionId, cancellationToken))
            {
                return SnapshotValidationResult.Fail("DRAFT_CANCELLED", "Danh sách lựa chọn thuộc bản nháp đã hủy. Vui lòng chọn lại trên bản nháp mới.");
            }

            if (cleanSnapshotDraftId != null || cleanCurrentDraftId != null)
            {
                if (!string.Equals(cleanSnapshotDraftId, cleanCurrentDraftId, StringComparison.Ordinal))
                {
                    return SnapshotValidationResult.Fail("DRAFT_MISMATCH", "Danh sách lựa chọn thuộc bản nháp đặt lịch khác. Vui lòng chọn trên danh sách của bản nháp hiện tại.");
                }
            }

            if (snapshot.DraftVersion.HasValue && (!request.CurrentDraftVersion.HasValue || snapshot.DraftVersion.Value != request.CurrentDraftVersion.Value))
            {
                return SnapshotValidationResult.Fail("DRAFT_VERSION_MISMATCH", "Danh sách lựa chọn không khớp với phiên bản thảo lịch hiện tại. Vui lòng chọn trên danh sách mới nhất.");
            }

            if (snapshot.SpecialtyId.HasValue && request.CurrentSpecialtyId.HasValue && snapshot.SpecialtyId.Value != request.CurrentSpecialtyId.Value)
            {
                return SnapshotValidationResult.Fail("SPECIALTY_MISMATCH", "Danh sách lựa chọn thuộc chuyên khoa khác với thảo lịch hiện tại. Vui lòng chọn lại.");
            }

            // Facility check if present
            if (snapshot.FacilityId.HasValue && request.CurrentFacilityId.HasValue && snapshot.FacilityId.Value != request.CurrentFacilityId.Value)
            {
                return SnapshotValidationResult.Fail("FACILITY_MISMATCH", "Danh sách lựa chọn thuộc cơ sở y tế khác. Vui lòng tải lại danh sách mới.");
            }

            if (snapshot.FacilityId.HasValue && request.CurrentUserId.HasValue && _facilityAuthService != null)
            {
                try
                {
                    await _facilityAuthService.ValidateUserFacilityAccessAsync(request.CurrentUserId.Value, snapshot.FacilityId.Value, cancellationToken);
                }
                catch
                {
                    return SnapshotValidationResult.Fail("FACILITY_DENIED", "Bạn không có quyền truy cập cơ sở y tế của danh sách lựa chọn này.");
                }
            }

            var docIds = DeserializeIds(snapshot.DoctorIdsJson);
            var slotIds = DeserializeIds(snapshot.SlotIdsJson);

            if (request.RequestedDoctorIds != null && request.RequestedDoctorIds.Count > 0)
            {
                if (!request.RequestedDoctorIds.SequenceEqual(docIds))
                {
                    return SnapshotValidationResult.Fail("DOCTOR_LIST_TAMPERED", "Danh sách bác sĩ hiển thị đã bị thay đổi hoặc không khớp với hệ thống.");
                }
            }

            if (request.RequestedSlotIds != null && request.RequestedSlotIds.Count > 0)
            {
                if (!request.RequestedSlotIds.SequenceEqual(slotIds))
                {
                    return SnapshotValidationResult.Fail("SLOT_LIST_TAMPERED", "Danh sách khung giờ hiển thị đã bị thay đổi hoặc không khớp với hệ thống.");
                }
            }

            var dto = MapToDto(snapshot, docIds, slotIds);
            return SnapshotValidationResult.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fail-closed: Error accessing snapshot storage during validation for snapshot {SnapshotId}", request.SnapshotId);
            return SnapshotValidationResult.Fail("STORE_ERROR", "Lỗi truy cập dữ liệu lựa chọn. Vui lòng thử lại.");
        }
    }

    public async Task InvalidateDraftSnapshotsForCancelAsync(
        string? draftId,
        string? sessionId,
        Guid? userId,
        long? facilityId = null,
        DateTime? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        var cleanDraftId = !string.IsNullOrWhiteSpace(draftId) ? draftId.Trim() : null;
        var cleanSessionId = !string.IsNullOrWhiteSpace(sessionId) ? sessionId.Trim() : null;

        // Fail closed: never expand scope when either identifier is missing
        if (cleanDraftId == null || cleanSessionId == null)
        {
            return;
        }

        var effectiveNow = nowUtc ?? _dateTimeProvider.UtcNow;
        // Keep a permanent terminal tombstone. Session/snapshot cleanup must
        // never make an old session+draft replayable.
        var expiresAt = PermanentCancellationExpiryUtc;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var existingScope = await _dbContext.AiCancelledDraftScopes
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.SessionId == cleanSessionId && c.DraftId == cleanDraftId, cancellationToken);

                if (existingScope != null)
                {
                    existingScope.CancelledAtUtc = effectiveNow;
                    existingScope.ExpiresAtUtc = expiresAt;
                    if (facilityId.HasValue) existingScope.FacilityId = facilityId;
                }
                else
                {
                    _dbContext.AiCancelledDraftScopes.Add(new AiCancelledDraftScope
                    {
                        UserId = userId,
                        SessionId = cleanSessionId,
                        DraftId = cleanDraftId,
                        FacilityId = facilityId,
                        CancelledAtUtc = effectiveNow,
                        ExpiresAtUtc = expiresAt
                    });
                }

                // Revoke the rows in the same transaction as the tombstone.
                var matchingSnapshots = await _dbContext.AiSelectionSnapshots
                    .Where(s => s.UserId == userId && s.SessionId == cleanSessionId && s.DraftId == cleanDraftId && !s.IsRevoked)
                    .ToListAsync(cancellationToken);

                foreach (var s in matchingSnapshots)
                {
                    s.IsRevoked = true;
                    s.RevokedAtUtc = effectiveNow;
                }

                var matchingSessions = await _dbContext.AiSessions
                    .Where(s => s.UserId == userId && s.SessionId == cleanSessionId && s.IsActive)
                    .ToListAsync(cancellationToken);
                foreach (var session in matchingSessions)
                {
                    if (string.Equals(session.ActiveDraftId, cleanDraftId, StringComparison.Ordinal))
                    {
                        session.ActiveDraftId = null;
                        session.ActiveDraftVersion = null;
                    }
                    session.LastActiveAtUtc = effectiveNow;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < 3 && IsUniqueOrSerializationConflict(ex))
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                _logger.LogError(ex, "Failed to persist draft cancellation scope for draft {DraftId} in session {SessionId}", cleanDraftId, cleanSessionId);
                throw;
            }
        }
    }

    public async Task<bool> IsDraftCancelledAsync(string? draftId, Guid? userId = null, string? sessionId = null, DateTime? nowUtc = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return await IsDraftCancelledInternalAsync(draftId.Trim(), userId, sessionId.Trim(), cancellationToken);
    }

    private async Task<bool> IsDraftCancelledInternalAsync(string cleanDraftId, Guid? userId, string? cleanSessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cleanSessionId))
        {
            return false;
        }

        try
        {
            var isCancelled = await _dbContext.AiCancelledDraftScopes
                .AsNoTracking()
                .AnyAsync(c => c.UserId == userId && c.SessionId == cleanSessionId && c.DraftId == cleanDraftId, cancellationToken);

            return isCancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking draft cancellation for draft {DraftId}", cleanDraftId);
            // Fail closed: if check fails, treat as cancelled or handle securely
            return true;
        }
    }

    public async Task<ResolveCancelScopeResult> TryResolveCancelScopeFromSnapshotAsync(
        string? snapshotId,
        Guid? userId,
        string? requestedSessionId,
        string? requestedDraftId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var resolvedSessionId = !string.IsNullOrWhiteSpace(requestedSessionId) ? requestedSessionId.Trim() : null;
        var resolvedDraftId = !string.IsNullOrWhiteSpace(requestedDraftId) ? requestedDraftId.Trim() : null;

        if (resolvedSessionId != null && resolvedDraftId != null)
        {
            try
            {
                var activeSessionOwnsDraft = await _dbContext.AiSessions
                    .AsNoTracking()
                    .AnyAsync(s => s.SessionId == resolvedSessionId
                        && s.UserId == userId
                        && s.IsActive
                        && s.ExpiresAtUtc > nowUtc
                        && s.ActiveDraftId == resolvedDraftId, cancellationToken);

                var activeSnapshotMatchesDraft = await _dbContext.AiSelectionSnapshots
                    .AsNoTracking()
                    .AnyAsync(s => s.SessionId == resolvedSessionId
                        && s.DraftId == resolvedDraftId
                        && s.UserId == userId
                        && !s.IsRevoked
                        && s.ExpiresAtUtc > nowUtc, cancellationToken);

                if (activeSessionOwnsDraft || activeSnapshotMatchesDraft)
                {
                    return new ResolveCancelScopeResult { HasResolved = true, SessionId = resolvedSessionId, DraftId = resolvedDraftId };
                }

                return new ResolveCancelScopeResult { HasResolved = false };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fail-closed: Error validating cancel scope {SessionId}/{DraftId}", resolvedSessionId, resolvedDraftId);
                return new ResolveCancelScopeResult { HasResolved = false };
            }
        }

        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            return new ResolveCancelScopeResult { HasResolved = false };
        }

        try
        {
            var cleanSnapshotId = snapshotId.Trim();
            var snap = await _dbContext.AiSelectionSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SnapshotId == cleanSnapshotId, cancellationToken);

            if (snap == null || snap.ExpiresAtUtc <= nowUtc || snap.IsRevoked || snap.UserId != userId)
            {
                return new ResolveCancelScopeResult { HasResolved = false };
            }

            var snapSessionId = !string.IsNullOrWhiteSpace(snap.SessionId) ? snap.SessionId.Trim() : null;
            var snapDraftId = !string.IsNullOrWhiteSpace(snap.DraftId) ? snap.DraftId.Trim() : null;

            if (resolvedSessionId != null && !string.Equals(resolvedSessionId, snapSessionId, StringComparison.Ordinal))
            {
                return new ResolveCancelScopeResult { HasResolved = false };
            }

            if (resolvedDraftId != null && !string.Equals(resolvedDraftId, snapDraftId, StringComparison.Ordinal))
            {
                return new ResolveCancelScopeResult { HasResolved = false };
            }

            resolvedSessionId ??= snapSessionId;
            resolvedDraftId ??= snapDraftId;

            if (resolvedSessionId != null && resolvedDraftId != null)
            {
                return new ResolveCancelScopeResult { HasResolved = true, SessionId = resolvedSessionId, DraftId = resolvedDraftId };
            }

            return new ResolveCancelScopeResult { HasResolved = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fail-closed: Error resolving cancel scope from snapshot {SnapshotId}", snapshotId);
            return new ResolveCancelScopeResult { HasResolved = false };
        }
    }

    public async Task<bool> HasAnyActiveSnapshotForUserAsync(Guid? userId, DateTime? nowUtc = null, CancellationToken cancellationToken = default)
    {
        var effectiveNow = nowUtc ?? _dateTimeProvider.UtcNow;
        try
        {
            return await _dbContext.AiSelectionSnapshots
                .AsNoTracking()
                .AnyAsync(s => s.UserId == userId && !s.IsRevoked && s.ExpiresAtUtc > effectiveNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking active snapshots for user {UserId}", userId);
            return false;
        }
    }

    public async Task<AiSessionTouchResult> TouchSessionAsync(string sessionId, Guid? userId, string? draftId, int? draftVersion, long? facilityId, CancellationToken cancellationToken = default)
    {
        var cleanSessionId = NormalizeIdentifier(sessionId);
        if (cleanSessionId == null)
        {
            return AiSessionTouchResult.Rejected("INVALID_SESSION", "Phiên làm việc không hợp lệ.");
        }
        var cleanDraftId = NormalizeIdentifier(draftId);
        var now = _dateTimeProvider.UtcNow;
        var expiresAt = now.Add(SessionTtl);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var session = await _dbContext.AiSessions
                    .FirstOrDefaultAsync(s => s.SessionId == cleanSessionId && s.UserId == userId, cancellationToken);

                if (session == null)
                {
                    if (cleanDraftId != null && await IsDraftCancelledInternalAsync(cleanDraftId, userId, cleanSessionId, cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return AiSessionTouchResult.Rejected("DRAFT_CANCELLED", "Bản nháp đã bị hủy. Vui lòng bắt đầu bản nháp mới.");
                    }

                    _dbContext.AiSessions.Add(new AiSession
                    {
                        SessionId = cleanSessionId,
                        UserId = userId,
                        ActiveDraftId = cleanDraftId,
                        ActiveDraftVersion = draftVersion,
                        FacilityId = facilityId,
                        CreatedAtUtc = now,
                        LastActiveAtUtc = now,
                        ExpiresAtUtc = expiresAt,
                        IsActive = true
                    });
                }
                else
                {
                    if (!session.IsActive || session.ExpiresAtUtc <= now)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return AiSessionTouchResult.Rejected("SESSION_EXPIRED", "Phiên làm việc đã hết hạn. Vui lòng bắt đầu lại.");
                    }

                    if (cleanDraftId != null && await IsDraftCancelledInternalAsync(cleanDraftId, userId, cleanSessionId, cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return AiSessionTouchResult.Rejected("DRAFT_CANCELLED", "Bản nháp đã bị hủy. Vui lòng bắt đầu bản nháp mới.");
                    }

                    if (cleanDraftId != null) session.ActiveDraftId = cleanDraftId;
                    if (draftVersion.HasValue) session.ActiveDraftVersion = draftVersion;
                    if (facilityId.HasValue) session.FacilityId = facilityId;
                    session.LastActiveAtUtc = now;
                    session.ExpiresAtUtc = expiresAt;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return AiSessionTouchResult.Accepted();
            }
            catch (Exception ex) when (attempt < 3 && IsUniqueOrSerializationConflict(ex))
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                _dbContext.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Failed to update AI session {SessionId}", cleanSessionId);
                return AiSessionTouchResult.Rejected("SESSION_STORE_ERROR", "Không thể xác thực phiên làm việc. Vui lòng thử lại.");
            }
        }

        return AiSessionTouchResult.Rejected("SESSION_STORE_ERROR", "Không thể xác thực phiên làm việc sau nhiều lần thử. Vui lòng thử lại.");
    }

    public async Task PurgeExpiredRecordsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            var expiredSnapshots = await _dbContext.AiSelectionSnapshots
                .Where(s => s.ExpiresAtUtc <= nowUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (expiredSnapshots.Count > 0)
            {
                _dbContext.AiSelectionSnapshots.RemoveRange(expiredSnapshots);
            }

            var expiredSessions = await _dbContext.AiSessions
                .Where(s => s.ExpiresAtUtc <= nowUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (expiredSessions.Count > 0)
            {
                _dbContext.AiSessions.RemoveRange(expiredSessions);
            }

            if (expiredSnapshots.Count > 0 || expiredSessions.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Purged {SnapshotCount} expired snapshots and {SessionCount} expired sessions; cancellation tombstones are retained permanently",
                    expiredSnapshots.Count, expiredSessions.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error purging expired AI records");
        }
    }

    private static AiSelectionSnapshotDto MapToDto(AiSelectionSnapshot snapshot, List<long> doctorIds, List<long> slotIds)
    {
        return new AiSelectionSnapshotDto
        {
            SnapshotId = snapshot.SnapshotId,
            UserId = snapshot.UserId,
            SessionId = snapshot.SessionId,
            DraftId = snapshot.DraftId,
            DraftVersion = snapshot.DraftVersion,
            FacilityId = snapshot.FacilityId,
            SpecialtyId = snapshot.SpecialtyId,
            DoctorId = snapshot.DoctorId,
            SlotDate = snapshot.SlotDate,
            DoctorIds = doctorIds,
            SlotIds = slotIds,
            ExpiresAtUtc = snapshot.ExpiresAtUtc,
            IsRevoked = snapshot.IsRevoked
        };
    }

    private static List<long> DeserializeIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<long>();
        try
        {
            return JsonSerializer.Deserialize<List<long>>(json) ?? new List<long>();
        }
        catch
        {
            return new List<long>();
        }
    }

    private static string? NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim();
        return clean.Length <= MaxIdentifierLength ? clean : null;
    }

    private static bool IsUniqueOrSerializationConflict(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            var name = current.GetType().FullName ?? string.Empty;
            if (name.Contains("SqliteException", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SqlException", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("constraint", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("locked", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = current.InnerException;
        }
        return false;
    }
}
