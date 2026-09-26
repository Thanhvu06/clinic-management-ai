using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.Application.AI.Interfaces;

public class CreateAiBookingConfirmationRequest
{
    public Guid UserId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string DraftId { get; init; } = string.Empty;
    public int DraftVersion { get; init; }
    public string? ContextSnapshotId { get; init; }
    public long SpecialtyId { get; init; }
    public long DoctorId { get; init; }
    public long SlotId { get; init; }
    public DateOnly SlotDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public string Reason { get; init; } = string.Empty;
    public TimeSpan? Ttl { get; init; }
}

public class ValidateAiBookingConfirmationRequest
{
    public string ConfirmationId { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string DraftId { get; init; } = string.Empty;
    public int DraftVersion { get; init; }
    public string? ContextSnapshotId { get; init; }
    public long SpecialtyId { get; init; }
    public long DoctorId { get; init; }
    public long SlotId { get; init; }
    public DateOnly SlotDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? IdempotencyKey { get; init; }
}

public class AiBookingConfirmationValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public long? ExistingAppointmentId { get; init; }

    public static AiBookingConfirmationValidationResult Valid(long? existingAppointmentId = null) => new()
    {
        IsValid = true,
        ExistingAppointmentId = existingAppointmentId
    };

    public static AiBookingConfirmationValidationResult Fail(string code, string message) => new()
    {
        IsValid = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}

public class AiBookingConfirmationDto
{
    public string ConfirmationId { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
}

public interface IAiBookingConfirmationStore
{
    Task<AiBookingConfirmationDto> CreateAsync(CreateAiBookingConfirmationRequest request, CancellationToken cancellationToken = default);
    Task<AiBookingConfirmationValidationResult> ValidateForAppointmentAsync(ValidateAiBookingConfirmationRequest request, CancellationToken cancellationToken = default);
    Task MarkUsedAsync(string confirmationId, long appointmentId, string? idempotencyKey, CancellationToken cancellationToken = default);
    Task RevokeForDraftAsync(Guid userId, string sessionId, string draftId, CancellationToken cancellationToken = default);
}
