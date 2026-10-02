using System;

namespace ClinicManagement.Domain.Entities;

public class AiAuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? DraftId { get; set; }
    public int? DraftVersion { get; set; }
    public long? FacilityId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? CorrelationId { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime TimestampUtc { get; set; }
}
