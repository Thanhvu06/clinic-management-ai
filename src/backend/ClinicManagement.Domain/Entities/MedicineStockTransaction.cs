using ClinicManagement.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicManagement.Domain.Entities;

public class MedicineStockTransaction
{
    public long Id { get; set; }
    public long MedicineId { get; set; }
    
    public MedicineStockTransactionType Type { get; set; }
    public int QuantityChange { get; set; }
    public int BalanceAfter { get; set; }
    
    public long? PrescriptionId { get; set; }
    public string? Reason { get; set; }
    
    public Guid ActorUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("MedicineId")]
    public Medicine? Medicine { get; set; }
    
    [ForeignKey("PrescriptionId")]
    public Prescription? Prescription { get; set; }
}
