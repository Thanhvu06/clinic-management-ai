using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Payment
{
    public long Id { get; set; }
    public string PaymentCode { get; set; } = string.Empty;
    public long InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Note { get; set; }
    public Guid ReceivedByUserId { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public PaymentStatus Status { get; set; } = PaymentStatus.Succeeded;
    public byte[] RowVersion { get; set; } = null!;

    // Navigation property
    public Invoice Invoice { get; set; } = null!;
}
