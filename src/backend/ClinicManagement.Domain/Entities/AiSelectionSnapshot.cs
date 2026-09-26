using System;

namespace ClinicManagement.Domain.Entities;

public class AiSelectionSnapshot
{
    public long Id { get; set; }
    public string SnapshotId { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? DraftId { get; set; }
    public int? DraftVersion { get; set; }
    public long? FacilityId { get; set; }
    public long? SpecialtyId { get; set; }
    public long? DoctorId { get; set; }
    public string? SlotDate { get; set; }
    public string DoctorIdsJson { get; set; } = "[]";
    public string SlotIdsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
