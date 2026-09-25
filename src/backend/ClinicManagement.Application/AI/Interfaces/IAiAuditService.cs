using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.Application.AI.Interfaces;

public class AiAuditLogEntry
{
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
}

public interface IAiAuditService
{
    Task LogActionAsync(AiAuditLogEntry entry, CancellationToken cancellationToken = default);
}
