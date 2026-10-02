using System;

namespace ClinicManagement.Domain.Entities;

public class AiCancelledDraftScope
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public long? FacilityId { get; set; }
    public DateTime CancelledAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
