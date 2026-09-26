using System;

namespace ClinicManagement.Domain.Entities;

public class AiBookingConfirmation
{
    public long Id { get; set; }
    public string ConfirmationId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public int DraftVersion { get; set; }
    public string? ContextSnapshotId { get; set; }
    public long SpecialtyId { get; set; }
    public long DoctorId { get; set; }
    public long SlotId { get; set; }
    public DateOnly SlotDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string ReasonHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public long? UsedAppointmentId { get; set; }
    public string? UsedIdempotencyKey { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
