using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Pharmacy.DTOs;

public class CreateMedicineBatchDto
{
    [Range(1, long.MaxValue)] public long MedicineId { get; set; }
    [Required, MaxLength(50)] public string BatchNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal? UnitCost { get; set; }
}

public class DisposeMedicineBatchDto
{
    [Required, MinLength(5), MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public record MedicineBatchDto(long Id, long MedicineId, string MedicineCode, string MedicineName,
    string Unit, string BatchNumber, DateOnly ExpiryDate, int QuantityReceived, int QuantityRemaining,
    int DaysToExpiry, string Status);
