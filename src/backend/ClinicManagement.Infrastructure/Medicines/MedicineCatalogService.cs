using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Medicines;

public class MedicineCatalogService(AppDbContext db)
{
    public Task<List<MedicineCategoryDto>> GetCategoriesAsync() => db.MedicineCategories.AsNoTracking()
        .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new MedicineCategoryDto {
            Id = c.Id, Name = c.Name, Description = c.Description, SortOrder = c.SortOrder,
            IsActive = c.IsActive, CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt
        }).ToListAsync();

    public async Task<MedicineCategoryDto> SaveCategoryAsync(long? id, SaveMedicineCategoryDto dto)
    {
        var name = dto.Name.Trim();
        if (name.Length == 0) throw new BusinessException("INVALID_CATEGORY_NAME", "Tên nhóm thuốc là bắt buộc.");
        if (await db.MedicineCategories.AnyAsync(c => c.Name == name && (!id.HasValue || c.Id != id.Value)))
            throw new BusinessException("DUPLICATE_CATEGORY_NAME", "Tên nhóm thuốc đã tồn tại.");
        var category = id.HasValue ? await db.MedicineCategories.FindAsync(id.Value)
            ?? throw new NotFoundException("Nhóm thuốc không tồn tại.") : new MedicineCategory();
        category.Name = name;
        category.Description = dto.Description;
        category.SortOrder = dto.SortOrder;
        if (id.HasValue) category.UpdatedAt = DateTime.UtcNow;
        else db.MedicineCategories.Add(category);
        await db.SaveChangesAsync();
        return ToDto(category);
    }

    public async Task<MedicineCategoryDto> ToggleCategoryAsync(long id)
    {
        var category = await db.MedicineCategories.FindAsync(id) ?? throw new NotFoundException("Nhóm thuốc không tồn tại.");
        if (category.IsActive && await db.Medicines.AnyAsync(m => m.CategoryId == id && m.IsActive))
            throw new BusinessException("CATEGORY_HAS_ACTIVE_MEDICINES", "Không thể tắt nhóm đang có thuốc được sử dụng.");
        category.IsActive = !category.IsActive;
        category.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToDto(category);
    }

    private static MedicineCategoryDto ToDto(MedicineCategory c) => new() {
        Id = c.Id, Name = c.Name, Description = c.Description, SortOrder = c.SortOrder,
        IsActive = c.IsActive, CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt
    };

    private IQueryable<Medicine> PublicQuery() => db.Medicines.AsNoTracking()
        .Where(m => m.IsActive && (m.CategoryId == null || m.Category!.IsActive));

    public async Task<PagedResult<PublicMedicineDto>> GetPublicMedicinesAsync(string? search, long? categoryId, string? type, int page, int pageSize)
    {
        if (type != null && type != "rx" && type != "otc")
            throw new BusinessException("INVALID_MEDICINE_TYPE", "Loại thuốc phải là rx hoặc otc.");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = PublicQuery();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(m => m.Name.ToLower().Contains(term) || m.Code.ToLower().Contains(term)
                || (m.ActiveIngredient != null && m.ActiveIngredient.ToLower().Contains(term)));
        }
        if (categoryId.HasValue) query = query.Where(m => m.CategoryId == categoryId);
        if (type != null) query = query.Where(m => m.IsPrescriptionRequired == (type == "rx"));
        var count = await query.CountAsync();
        var offset = Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var items = await query.OrderBy(m => m.Name).ThenBy(m => m.Id).Skip((int)offset).Take(pageSize)
            .Select(MedicineProjections.Public).ToListAsync();
        return new(items, count, page, pageSize);
    }

    public async Task<PublicMedicineDto> GetPublicMedicineAsync(long id) =>
        await PublicQuery().Where(m => m.Id == id).Select(MedicineProjections.Public).FirstOrDefaultAsync()
        ?? throw new NotFoundException("Thuốc không tồn tại.");

    public Task<List<PublicMedicineCategoryDto>> GetPublicCategoriesAsync() => db.MedicineCategories.AsNoTracking()
        .Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
        .Select(c => new PublicMedicineCategoryDto(c.Id, c.Name, c.Description, c.SortOrder)).ToListAsync();
}
