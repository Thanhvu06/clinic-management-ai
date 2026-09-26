using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A short-lived, server-owned write intent. It stores only allowlisted
/// identifiers and normalized arguments; prompts and model output never enter this record.
/// </summary>
public class AiPendingToolAction
{
    public Guid ActionId { get; set; }
    public Guid UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? DraftId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ToolVersion { get; set; } = "1.0";
    public string RequestHash { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string NormalizedArgumentsJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? ExecutedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public AiPendingToolActionState State { get; set; } = AiPendingToolActionState.PendingConfirmation;
    public Guid? ExecutionLeaseId { get; set; }
    public DateTime? ExecutionLeaseExpiresAtUtc { get; set; }
    public int ExecutionAttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? IdempotencyKeyHash { get; set; }
    public string? ExecutionResultReference { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
