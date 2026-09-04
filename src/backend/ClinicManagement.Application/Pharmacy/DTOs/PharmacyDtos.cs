using System;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Pharmacy.DTOs;

public class PharmacyDashboardDto
{
    public int PendingPrescriptionsCount { get; set; }
    public int DispensedTodayCount { get; set; }
    public int LowStockCount { get; set; }
    public int TotalActiveMedicines { get; set; }
}

public class PharmacyPrescriptionListDto
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public DateOnly AppointmentDate { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DispensedAt { get; set; }
    public string? Notes { get; set; }
}

public class DispensePrescriptionResultDto
{
    public long PrescriptionId { get; set; }
    public DateTime DispensedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class StockTransactionDto
{
    public long Id { get; set; }
    public long MedicineId { get; set; }
    public string MedicineCode { get; set; } = string.Empty;
    public string MedicineName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int QuantityChange { get; set; }
    public int BalanceAfter { get; set; }
    public long? PrescriptionId { get; set; }
    public string? Reason { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AdjustStockDto
{
    [Required]
    public long MedicineId { get; set; }

    [Required]
    public MedicineStockTransactionType Type { get; set; } = MedicineStockTransactionType.StockIn;

    [Range(1, 100000, ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public int Quantity { get; set; }

    public string? Reason { get; set; }
}
