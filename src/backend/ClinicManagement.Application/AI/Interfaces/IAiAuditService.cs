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

public class AiAuditWriteResult
{
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }

    public static AiAuditWriteResult Success() => new() { Succeeded = true };
    public static AiAuditWriteResult Failed(string errorCode) => new() { Succeeded = false, ErrorCode = errorCode };
}

public interface IAiAuditService
{
    Task<AiAuditWriteResult> LogActionAsync(AiAuditLogEntry entry, CancellationToken cancellationToken = default);
}
