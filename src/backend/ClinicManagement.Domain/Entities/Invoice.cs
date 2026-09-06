using System;
using System.Collections.Generic;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Invoice
{
    public long Id { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public InvoiceSourceType SourceType { get; set; }
    public long? AppointmentId { get; set; }
    public long? HealthPackageRegistrationId { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public decimal Subtotal { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? PaidByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public HealthPackageRegistration? HealthPackageRegistration { get; set; }
    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
