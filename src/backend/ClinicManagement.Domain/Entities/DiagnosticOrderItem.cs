using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class DiagnosticOrderItem
{
    public long Id { get; set; }
    public long DiagnosticOrderId { get; set; }
    public long DiagnosticServiceId { get; set; }

    public DiagnosticItemStatus Status { get; set; } = DiagnosticItemStatus.Ordered;
    public bool IsPackageCovered { get; set; }
    public long? PackageRegistrationId { get; set; }
    public SpecimenStatus SpecimenStatus { get; set; } = SpecimenStatus.NotRequired;
    public string? SpecimenBarcode { get; set; }
    public DateTime? CollectedAtUtc { get; set; }
    public Guid? CollectedByUserId { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public SpecimenRejectionReason? RejectionReason { get; set; }
    public string? RejectionNote { get; set; }
    public int RecollectCount { get; set; }

    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

    public DiagnosticOrder DiagnosticOrder { get; set; } = null!;
    public DiagnosticService DiagnosticService { get; set; } = null!;
    public HealthPackageRegistration? PackageRegistration { get; set; }
    public DiagnosticResult? Result { get; set; }
}
