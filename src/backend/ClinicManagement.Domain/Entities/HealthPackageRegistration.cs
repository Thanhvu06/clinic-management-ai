using System;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class HealthPackageRegistration
{
    public long Id { get; set; }

    [Required]
    [StringLength(50)]
    public string RegistrationCode { get; set; } = string.Empty;

    public long HealthPackageId { get; set; }
    public long PatientId { get; set; }

    public DateOnly PreferredDate { get; set; }

    [Required]
    [StringLength(20)]
    public string ContactPhone { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Note { get; set; }

    [StringLength(1000)]
    public string? AdminNotes { get; set; }

    [StringLength(500)]
    public string? CancellationReason { get; set; }

    public HealthPackageRegistrationStatus Status { get; set; } = HealthPackageRegistrationStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public HealthPackage HealthPackage { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
}
