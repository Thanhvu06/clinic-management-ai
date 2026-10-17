using System.Linq.Expressions;
using ClinicManagement.Infrastructure.Pharmacy;
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
        Indications = m.Indications,
        DosageAndAdministration = m.DosageAndAdministration,
        Contraindications = m.Contraindications,
        SideEffects = m.SideEffects,
        Precautions = m.Precautions,
        OverdoseManagement = m.OverdoseManagement,
        StorageInstructions = m.StorageInstructions, ImagePath = m.ImagePath,
        ImageUrl = m.ImagePath == null ? null : "/media/medicines/" + m.ImagePath
    };

    public static readonly Expression<Func<MedicineWithUsableStock, PublicMedicineDto>> Public = stock => new PublicMedicineDto
    {
        Id = stock.Medicine.Id, Code = stock.Medicine.Code, Name = stock.Medicine.Name, Unit = stock.Medicine.Unit, UnitPrice = stock.Medicine.UnitPrice,
        ActiveIngredient = stock.Medicine.ActiveIngredient, Strength = stock.Medicine.Strength, DosageForm = stock.Medicine.DosageForm,
        CategoryName = stock.Medicine.Category == null ? null : stock.Medicine.Category.Name,
        IsPrescriptionRequired = stock.Medicine.IsPrescriptionRequired, Description = stock.Medicine.Description,
        Indications = stock.Medicine.Indications,
        DosageAndAdministration = stock.Medicine.DosageAndAdministration,
        Contraindications = stock.Medicine.Contraindications,
        SideEffects = stock.Medicine.SideEffects,
        Precautions = stock.Medicine.Precautions,
        OverdoseManagement = stock.Medicine.OverdoseManagement,
        StorageInstructions = stock.Medicine.StorageInstructions,
        ImageUrl = stock.Medicine.ImagePath == null ? null : "/media/medicines/" + stock.Medicine.ImagePath,
        Availability = stock.Quantity <= 0 ? "out_of_stock" : stock.Quantity <= stock.Medicine.ReorderLevel ? "low" : "in_stock"
    };
}
