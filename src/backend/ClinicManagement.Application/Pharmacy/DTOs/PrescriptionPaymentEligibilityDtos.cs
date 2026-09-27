using System.Collections.Generic;

namespace ClinicManagement.Application.Pharmacy.DTOs;

public sealed class PrescriptionPaymentEligibilityDto
{
    public long PrescriptionId { get; init; }
    public string PaymentStatus { get; init; } = "unverifiable";
    public bool IsFullyPaid { get; init; }
    public List<PrescriptionItemPaymentStatusDto> Items { get; init; } = new();
}

public sealed class PrescriptionItemPaymentStatusDto
{
    public long MedicineId { get; init; }
    public string MedicineName { get; init; } = string.Empty;
    public int RequiredQuantity { get; init; }
    public int PaidQuantity { get; init; }
    public bool IsPaidInFull { get; init; }
}
