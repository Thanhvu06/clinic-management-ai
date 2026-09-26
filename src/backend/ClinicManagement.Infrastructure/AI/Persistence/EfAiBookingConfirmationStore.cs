using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ClinicManagement.Infrastructure.AI.Persistence;

public sealed class EfAiBookingConfirmationStore : IAiBookingConfirmationStore
{
    private static readonly TimeSpan RetentionAfterTerminalState = TimeSpan.FromHours(24);
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<EfAiBookingConfirmationStore> _logger;

    public EfAiBookingConfirmationStore(
        AppDbContext dbContext,
        IDateTimeProvider dateTimeProvider,
        ILogger<EfAiBookingConfirmationStore> logger)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task<AiBookingConfirmationDto> CreateAsync(
        CreateAiBookingConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        var cleanSessionId = Normalize(request.SessionId) ?? throw new ArgumentException("SessionId is required.", nameof(request));
        var cleanDraftId = Normalize(request.DraftId) ?? throw new ArgumentException("DraftId is required.", nameof(request));
        var now = _dateTimeProvider.UtcNow;
        var ttl = request.Ttl ?? TimeSpan.FromMinutes(15);
        if (ttl > TimeSpan.FromMinutes(30)) ttl = TimeSpan.FromMinutes(15);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var pending = await _dbContext.AiBookingConfirmations
                    .Where(x => x.UserId == request.UserId && x.SessionId == cleanSessionId && x.DraftId == cleanDraftId && !x.UsedAtUtc.HasValue && !x.RevokedAtUtc.HasValue)
                    .ToListAsync(cancellationToken);
                foreach (var item in pending) item.RevokedAtUtc = now;

                var confirmation = new AiBookingConfirmation
                {
                    ConfirmationId = $"conf_{Guid.NewGuid():N}",
                    UserId = request.UserId,
                    SessionId = cleanSessionId,
                    DraftId = cleanDraftId,
                    DraftVersion = request.DraftVersion,
                    ContextSnapshotId = Normalize(request.ContextSnapshotId),
                    SpecialtyId = request.SpecialtyId,
                    DoctorId = request.DoctorId,
                    SlotId = request.SlotId,
                    SlotDate = request.SlotDate,
                    StartTime = request.StartTime,
                    EndTime = request.EndTime,
                    ReasonHash = AiBookingConfirmationHasher.Compute(request.Reason),
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.Add(ttl)
                };

                _dbContext.AiBookingConfirmations.Add(confirmation);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new AiBookingConfirmationDto { ConfirmationId = confirmation.ConfirmationId, ExpiresAtUtc = confirmation.ExpiresAtUtc };
            }
            catch (Exception ex) when (attempt < 3 && IsRetryable(ex))
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            catch
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { }
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        throw new InvalidOperationException("Could not persist an AI booking confirmation after retries.");
    }

    public async Task<AiBookingConfirmationValidationResult> ValidateForAppointmentAsync(
        ValidateAiBookingConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ConfirmationId))
        {
            return AiBookingConfirmationValidationResult.Fail("MISSING_CONFIRMATION", "Thiếu mã xác nhận đặt lịch AI.");
        }

        var confirmation = await _dbContext.AiBookingConfirmations
            .FirstOrDefaultAsync(x => x.ConfirmationId == request.ConfirmationId.Trim(), cancellationToken);

        if (confirmation == null)
        {
            return AiBookingConfirmationValidationResult.Fail("CONFIRMATION_NOT_FOUND", "Mã xác nhận AI không tồn tại hoặc đã hết hạn.");
        }

        if (confirmation.UserId != request.UserId ||
            !string.Equals(confirmation.SessionId, request.SessionId.Trim(), StringComparison.Ordinal) ||
            !string.Equals(confirmation.DraftId, request.DraftId.Trim(), StringComparison.Ordinal))
        {
            return AiBookingConfirmationValidationResult.Fail("CONFIRMATION_SCOPE_MISMATCH", "Mã xác nhận AI không thuộc phiên hoặc bản nháp hiện tại.");
        }

        if (confirmation.DraftVersion != request.DraftVersion ||
            !string.Equals(confirmation.ContextSnapshotId, Normalize(request.ContextSnapshotId), StringComparison.Ordinal) ||
            confirmation.SpecialtyId != request.SpecialtyId ||
            confirmation.DoctorId != request.DoctorId ||
            confirmation.SlotId != request.SlotId ||
            confirmation.SlotDate != request.SlotDate ||
            confirmation.StartTime != request.StartTime ||
            confirmation.EndTime != request.EndTime ||
            !string.Equals(confirmation.ReasonHash, AiBookingConfirmationHasher.Compute(request.Reason), StringComparison.Ordinal))
        {
            return AiBookingConfirmationValidationResult.Fail("CONFIRMATION_PAYLOAD_MISMATCH", "Mã xác nhận AI không khớp với thông tin đặt lịch hiện tại.");
        }

        if (confirmation.UsedAtUtc.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                string.Equals(confirmation.UsedIdempotencyKey, request.IdempotencyKey.Trim(), StringComparison.Ordinal))
            {
                return AiBookingConfirmationValidationResult.Valid(confirmation.UsedAppointmentId);
            }

            return AiBookingConfirmationValidationResult.Fail("CONFIRMATION_ALREADY_USED", "Mã xác nhận AI đã được sử dụng. Vui lòng tạo lượt xác nhận mới.");
        }

        if (confirmation.RevokedAtUtc.HasValue)
        {
            return AiBookingConfirmationValidationResult.Fail("CONFIRMATION_REVOKED", "Mã xác nhận AI đã bị thu hồi do bản nháp thay đổi hoặc bị hủy.");
        }

        if (confirmation.ExpiresAtUtc <= _dateTimeProvider.UtcNow)
        {
            return AiBookingConfirmationValidationResult.Fail("CONFIRMATION_EXPIRED", "Mã xác nhận AI đã hết hạn. Vui lòng xác nhận lại thông tin mới nhất.");
        }

        return AiBookingConfirmationValidationResult.Valid();
    }

    public async Task MarkUsedAsync(
        string confirmationId,
        long appointmentId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var confirmation = await _dbContext.AiBookingConfirmations
            .FirstOrDefaultAsync(x => x.ConfirmationId == confirmationId.Trim(), cancellationToken);

        if (confirmation == null)
        {
            throw new InvalidOperationException("AI confirmation was not found while marking it used.");
        }

        if (confirmation.UsedAtUtc.HasValue && confirmation.UsedAppointmentId != appointmentId)
        {
            throw new InvalidOperationException("AI confirmation was already used for another appointment.");
        }

        confirmation.UsedAtUtc ??= _dateTimeProvider.UtcNow;
        confirmation.UsedAppointmentId ??= appointmentId;
        confirmation.UsedIdempotencyKey ??= string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
    }

    public async Task RevokeForDraftAsync(
        Guid userId,
        string sessionId,
        string draftId,
        CancellationToken cancellationToken = default)
    {
        var cleanSessionId = Normalize(sessionId);
        var cleanDraftId = Normalize(draftId);
        if (cleanSessionId == null || cleanDraftId == null)
        {
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var pending = await _dbContext.AiBookingConfirmations
                .Where(x => x.UserId == userId && x.SessionId == cleanSessionId && x.DraftId == cleanDraftId && !x.UsedAtUtc.HasValue && !x.RevokedAtUtc.HasValue)
                .ToListAsync(cancellationToken);

            var now = _dateTimeProvider.UtcNow;
            foreach (var item in pending) item.RevokedAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); } catch { }
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var cutoff = nowUtc.Subtract(RetentionAfterTerminalState);
        var batch = await _dbContext.AiBookingConfirmations
            .Where(x => (x.ExpiresAtUtc <= cutoff) ||
                        (x.RevokedAtUtc.HasValue && x.RevokedAtUtc.Value <= cutoff) ||
                        (x.UsedAtUtc.HasValue && x.UsedAtUtc.Value <= cutoff))
            .OrderBy(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
        if (batch.Count == 0) return;
        _dbContext.AiBookingConfirmations.RemoveRange(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Purged {Count} retained AI booking confirmations", batch.Count);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsRetryable(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException!)
        {
            var type = current.GetType().FullName ?? string.Empty;
            if (type.Contains("SqliteException", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("SqlException", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

public static class AiBookingConfirmationHasher
{
    public static string Compute(string reason)
    {
        var normalized = (reason ?? string.Empty).Trim();
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized)));
    }
}
