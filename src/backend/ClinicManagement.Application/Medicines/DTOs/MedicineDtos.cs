using System;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Medicines.DTOs;

public class MedicineDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? ActiveIngredient { get; set; }
    public string? Strength { get; set; }
    public string? DosageForm { get; set; }
    public string? Manufacturer { get; set; }
    public long? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public bool IsPrescriptionRequired { get; set; }
    public string? Description { get; set; }
    public string? StorageInstructions { get; set; }
    public string? ImagePath { get; set; }
    public string? ImageUrl { get; set; }
}

public class ActiveMedicineDto
{
    public string? ImageUrl { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsPrescriptionRequired { get; set; }
    public string? Strength { get; set; }
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public decimal? UnitPrice { get; set; }
}

public class CreateMedicineDto : MedicineCatalogInput
{
    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Unit { get; set; } = string.Empty;

    public int StockQuantity { get; set; } = 0;
    public int ReorderLevel { get; set; } = 10;
    public decimal? UnitPrice { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateMedicineDto : MedicineCatalogInput
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Unit { get; set; } = string.Empty;

    public int ReorderLevel { get; set; } = 10;
    public decimal? UnitPrice { get; set; }
    public bool IsActive { get; set; } = true;
}
