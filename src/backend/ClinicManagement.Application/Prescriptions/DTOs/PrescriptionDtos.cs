using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Prescriptions.DTOs;

public class CreatePrescriptionDto
{
    public string? Notes { get; set; }
    
    [Required]
    [MinLength(1, ErrorMessage = "Đơn thuốc phải có ít nhất 1 loại thuốc.")]
    public List<CreatePrescriptionItemDto> Items { get; set; } = new();
}

public class CreatePrescriptionItemDto
{
    [Required]
    public long MedicineId { get; set; }

    [Range(1, 10000, ErrorMessage = "Số lượng thuốc phải lớn hơn 0.")]
    public int Quantity { get; set; }

    [Required]
    [StringLength(100)]
    public string Dosage { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Frequency { get; set; } = string.Empty;

    public int? DurationDays { get; set; }

    [StringLength(500)]
    public string? Instructions { get; set; }
}

public class PrescriptionDetailDto
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public long DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DispensedAt { get; set; }
    public List<PrescriptionDetailItemDto> Items { get; set; } = new();
}

public class PrescriptionDetailItemDto
{
    public long MedicineId { get; set; }
    public string MedicineCode { get; set; } = string.Empty;
    public string MedicineName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int AvailableStock { get; set; }
    public bool IsActive { get; set; } = true;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int? DurationDays { get; set; }
    public string? Instructions { get; set; }
}
