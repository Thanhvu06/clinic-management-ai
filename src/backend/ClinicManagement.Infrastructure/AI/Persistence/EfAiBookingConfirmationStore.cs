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

namespace ClinicManagement.Infrastructure.AI.Persistence;

public sealed class EfAiBookingConfirmationStore : IAiBookingConfirmationStore
{
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
        var now = _dateTimeProvider.UtcNow;
        var confirmation = new AiBookingConfirmation
        {
            ConfirmationId = $"conf_{Guid.NewGuid():N}",
            UserId = request.UserId,
            SessionId = request.SessionId.Trim(),
            DraftId = request.DraftId.Trim(),
            DraftVersion = request.DraftVersion,
            ContextSnapshotId = string.IsNullOrWhiteSpace(request.ContextSnapshotId) ? null : request.ContextSnapshotId.Trim(),
            SpecialtyId = request.SpecialtyId,
            DoctorId = request.DoctorId,
            SlotId = request.SlotId,
            SlotDate = request.SlotDate,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            ReasonHash = AiBookingConfirmationHasher.Compute(request.Reason),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(request.Ttl ?? TimeSpan.FromMinutes(15))
        };

        await RevokeForDraftAsync(request.UserId, request.SessionId, request.DraftId, cancellationToken);
        _dbContext.AiBookingConfirmations.Add(confirmation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AiBookingConfirmationDto
        {
            ConfirmationId = confirmation.ConfirmationId,
            ExpiresAtUtc = confirmation.ExpiresAtUtc
        };
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

        var pending = await _dbContext.AiBookingConfirmations
            .Where(x => x.UserId == userId && x.SessionId == cleanSessionId && x.DraftId == cleanDraftId && !x.UsedAtUtc.HasValue && !x.RevokedAtUtc.HasValue)
            .ToListAsync(cancellationToken);

        var now = _dateTimeProvider.UtcNow;
        foreach (var item in pending)
        {
            item.RevokedAtUtc = now;
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
