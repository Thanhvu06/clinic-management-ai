using System;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Domain.Entities;

public class HealthPackage
{
    public long Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string TargetAudience { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public string? IncludedServicesJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
