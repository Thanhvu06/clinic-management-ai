using System;

namespace ClinicManagement.Domain.Entities;

public class DiagnosticResult
{
    public long Id { get; set; }
    public long DiagnosticOrderItemId { get; set; }

    public string ResultText { get; set; } = string.Empty;
    public string? Conclusion { get; set; }
    public string? ReferenceRange { get; set; }
    public string? Unit { get; set; }

    public DateTime ResultedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ResultedByUserId { get; set; }

    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

    public DiagnosticOrderItem DiagnosticOrderItem { get; set; } = null!;
}