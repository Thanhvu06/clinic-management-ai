using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ClinicManagement.Application.Medicines.DTOs;

// Track supplied fields so legacy PUT payloads preserve the added catalog data.
public class MedicineCatalogInput
{
    private string? _activeIngredient, _strength, _dosageForm, _manufacturer, _description, _storageInstructions, _imagePath;
    private long? _categoryId;
    private bool _isPrescriptionRequired = true;
    [JsonIgnore] public HashSet<string> SuppliedFields { get; } = new();
    [StringLength(200)] public string? ActiveIngredient { get => _activeIngredient; set { _activeIngredient = value; SuppliedFields.Add(nameof(ActiveIngredient)); } }
    [StringLength(100)] public string? Strength { get => _strength; set { _strength = value; SuppliedFields.Add(nameof(Strength)); } }
    [StringLength(100)] public string? DosageForm { get => _dosageForm; set { _dosageForm = value; SuppliedFields.Add(nameof(DosageForm)); } }
    [StringLength(200)] public string? Manufacturer { get => _manufacturer; set { _manufacturer = value; SuppliedFields.Add(nameof(Manufacturer)); } }
    public long? CategoryId { get => _categoryId; set { _categoryId = value; SuppliedFields.Add(nameof(CategoryId)); } }
    public bool IsPrescriptionRequired { get => _isPrescriptionRequired; set { _isPrescriptionRequired = value; SuppliedFields.Add(nameof(IsPrescriptionRequired)); } }
    [StringLength(1000)] public string? Description { get => _description; set { _description = value; SuppliedFields.Add(nameof(Description)); } }
    [StringLength(500)] public string? StorageInstructions { get => _storageInstructions; set { _storageInstructions = value; SuppliedFields.Add(nameof(StorageInstructions)); } }
    [StringLength(300)] public string? ImagePath { get => _imagePath; set { _imagePath = value; SuppliedFields.Add(nameof(ImagePath)); } }
}

public class MedicineCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SaveMedicineCategoryDto
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public class PublicMedicineDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ActiveIngredient { get; set; }
    public string? Strength { get; set; }
    public string? DosageForm { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public bool IsPrescriptionRequired { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
    public string? StorageInstructions { get; set; }
    public string Availability { get; set; } = string.Empty;
}

public record PublicMedicineCategoryDto(long Id, string Name, string? Description, int SortOrder);
