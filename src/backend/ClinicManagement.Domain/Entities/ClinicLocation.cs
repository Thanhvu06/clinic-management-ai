using System;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Domain.Entities;

public class ClinicLocation
{
    public long Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string Address { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string OpeningHours { get; set; } = "07:00–17:00 (thứ 2 – thứ 7) · Chủ nhật nghỉ";

    [StringLength(1000)]
    public string? Description { get; set; }

    public string? ServicesJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
