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
    public decimal? UnitPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ActiveIngredient { get; set; }
    public string? Strength { get; set; }
    public string? DosageForm { get; set; }
    public string? Manufacturer { get; set; }
    public long? CategoryId { get; set; }
    public MedicineCategory? Category { get; set; }
    public bool IsPrescriptionRequired { get; set; } = true;
    public string? Description { get; set; }
    public string? StorageInstructions { get; set; }
    public string? ImagePath { get; set; }
    
    [Timestamp]
    public byte[]? RowVersion { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    public ICollection<PrescriptionItem> PrescriptionItems { get; set; } = new List<PrescriptionItem>();
    public ICollection<MedicineStockTransaction> StockTransactions { get; set; } = new List<MedicineStockTransaction>();
}
