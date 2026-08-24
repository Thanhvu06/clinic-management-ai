using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Domain.Entities;

public class Medicine
{
    public long Id { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;
    
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [StringLength(50)]
    public string Unit { get; set; } = string.Empty;
    
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;
    
    [Timestamp]
    public byte[]? RowVersion { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    public ICollection<PrescriptionItem> PrescriptionItems { get; set; } = new List<PrescriptionItem>();
    public ICollection<MedicineStockTransaction> StockTransactions { get; set; } = new List<MedicineStockTransaction>();
}
