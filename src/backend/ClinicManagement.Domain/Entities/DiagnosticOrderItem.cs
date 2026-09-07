using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class DiagnosticOrderItem
{
    public long Id { get; set; }
    public long DiagnosticOrderId { get; set; }
    public long DiagnosticServiceId { get; set; }

    public DiagnosticItemStatus Status { get; set; } = DiagnosticItemStatus.Ordered;

    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

    public DiagnosticOrder DiagnosticOrder { get; set; } = null!;
    public DiagnosticService DiagnosticService { get; set; } = null!;
    public DiagnosticResult? Result { get; set; }
}