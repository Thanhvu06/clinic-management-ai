using System.Linq.Expressions;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Medicines;

internal static class MedicineProjections
{
    public static readonly Expression<Func<Medicine, MedicineDto>> Admin = m => new MedicineDto
    {
        Id = m.Id, Code = m.Code, Name = m.Name, Unit = m.Unit, StockQuantity = m.StockQuantity,
        ReorderLevel = m.ReorderLevel, UnitPrice = m.UnitPrice, IsActive = m.IsActive,
        CreatedAt = m.CreatedAt, UpdatedAt = m.UpdatedAt, ActiveIngredient = m.ActiveIngredient,
        Strength = m.Strength, DosageForm = m.DosageForm, Manufacturer = m.Manufacturer,
        CategoryId = m.CategoryId, CategoryName = m.Category == null ? null : m.Category.Name,
        IsPrescriptionRequired = m.IsPrescriptionRequired, Description = m.Description,
        StorageInstructions = m.StorageInstructions, ImagePath = m.ImagePath,
        ImageUrl = m.ImagePath == null ? null : "/media/medicines/" + m.ImagePath
    };

    public static readonly Expression<Func<Medicine, PublicMedicineDto>> Public = m => new PublicMedicineDto
    {
        Id = m.Id, Code = m.Code, Name = m.Name, Unit = m.Unit, UnitPrice = m.UnitPrice,
        ActiveIngredient = m.ActiveIngredient, Strength = m.Strength, DosageForm = m.DosageForm,
        CategoryName = m.Category == null ? null : m.Category.Name,
        IsPrescriptionRequired = m.IsPrescriptionRequired, Description = m.Description,
        StorageInstructions = m.StorageInstructions,
        ImageUrl = m.ImagePath == null ? null : "/media/medicines/" + m.ImagePath,
        Availability = m.StockQuantity <= 0 ? "out_of_stock" : m.StockQuantity <= m.ReorderLevel ? "low" : "in_stock"
    };
}
