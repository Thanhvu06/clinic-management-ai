using System;

namespace ClinicManagement.Domain.Entities;

public class AiSession
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? ActiveDraftId { get; set; }
    public int? ActiveDraftVersion { get; set; }
    public long? FacilityId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastActiveAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
