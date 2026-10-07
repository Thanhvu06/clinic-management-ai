using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Application.Medicines.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Medicines;

public class MedicineService : IMedicineService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public MedicineService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<List<ActiveMedicineDto>> GetActiveMedicinesAsync()
    {
        return await _dbContext.Medicines
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new ActiveMedicineDto
            {
                Id = m.Id,
                Code = m.Code,
                Name = m.Name,
                Unit = m.Unit,
                StockQuantity = m.StockQuantity,
                ImageUrl = m.ImagePath == null ? null : "/media/medicines/" + m.ImagePath,
                ReorderLevel = m.ReorderLevel,
                UnitPrice = m.UnitPrice,
                IsPrescriptionRequired = m.IsPrescriptionRequired,
                Strength = m.Strength
            })
            .ToListAsync();
    }

    public async Task<PagedResult<MedicineDto>> GetMedicinesAsync(string? search, bool? isActive, int page, int pageSize, long? categoryId = null, bool? isPrescriptionRequired = null)
    {
        var query = _dbContext.Medicines.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(m => m.Code.ToLower().Contains(s) || m.Name.ToLower().Contains(s)
                || (m.ActiveIngredient != null && m.ActiveIngredient.ToLower().Contains(s)));
        }

        if (isActive.HasValue)
        {
            query = query.Where(m => m.IsActive == isActive.Value);
        }

        if (categoryId.HasValue) query = query.Where(m => m.CategoryId == categoryId);
        if (isPrescriptionRequired.HasValue) query = query.Where(m => m.IsPrescriptionRequired == isPrescriptionRequired);
        query = query.OrderBy(m => m.Name).ThenBy(m => m.Id);

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MedicineProjections.Admin)
            .ToListAsync();

        return new PagedResult<MedicineDto>(items, totalItems, page, pageSize);
    }

    public async Task<MedicineDto> GetMedicineByIdAsync(long id)
    {
        var m = await _dbContext.Medicines.AsNoTracking().Where(x => x.Id == id).Select(MedicineProjections.Admin).FirstOrDefaultAsync();
        if (m == null) throw new NotFoundException("Thuốc không tồn tại.");

        return m;
    }

    public async Task<MedicineDto> CreateMedicineAsync(CreateMedicineDto dto)
    {
        var codeExists = await _dbContext.Medicines.AnyAsync(m => m.Code == dto.Code.Trim());
        if (codeExists) throw new BusinessException("DUPLICATE_CODE", "Mã thuốc này đã tồn tại trong danh mục.");

        var medicine = new Medicine
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Unit = dto.Unit.Trim(),
            StockQuantity = dto.StockQuantity,
            ReorderLevel = dto.ReorderLevel,
            UnitPrice = dto.UnitPrice,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        await ApplyCatalogFieldsAsync(medicine, dto);
        _dbContext.Medicines.Add(medicine);
        await _dbContext.SaveChangesAsync();

        if (dto.StockQuantity > 0)
        {
            var currentUserId = _currentUserService.UserId ?? Guid.Empty;
            _dbContext.MedicineStockTransactions.Add(new MedicineStockTransaction
            {
                MedicineId = medicine.Id,
                Type = MedicineStockTransactionType.Initial,
                QuantityChange = dto.StockQuantity,
                BalanceAfter = dto.StockQuantity,
                Reason = "Khởi tạo tồn kho ban đầu",
                ActorUserId = currentUserId,
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }

        return await GetMedicineByIdAsync(medicine.Id);
    }

    public async Task<MedicineDto> UpdateMedicineAsync(long id, UpdateMedicineDto dto)
    {
        var medicine = await _dbContext.Medicines.FirstOrDefaultAsync(m => m.Id == id);
        if (medicine == null) throw new NotFoundException("Thuốc không tồn tại.");

        medicine.Name = dto.Name.Trim();
        await ApplyCatalogFieldsAsync(medicine, dto);
        medicine.Unit = dto.Unit.Trim();
        medicine.ReorderLevel = dto.ReorderLevel;
        medicine.UnitPrice = dto.UnitPrice;
        medicine.IsActive = dto.IsActive;
        medicine.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return await GetMedicineByIdAsync(medicine.Id);
    }

    public async Task ToggleMedicineStatusAsync(long id)
    {
        var medicine = await _dbContext.Medicines.FirstOrDefaultAsync(m => m.Id == id);
        if (medicine == null) throw new NotFoundException("Thuốc không tồn tại.");

        medicine.IsActive = !medicine.IsActive;
        medicine.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
    }

    private async Task ApplyCatalogFieldsAsync(Medicine medicine, MedicineCatalogInput dto)
    {
        if (dto.SuppliedFields.Contains(nameof(dto.CategoryId)))
        {
            if (dto.CategoryId.HasValue && !await _dbContext.MedicineCategories.AnyAsync(c => c.Id == dto.CategoryId && c.IsActive))
                throw new BusinessException("INVALID_MEDICINE_CATEGORY", "Nhóm thuốc không tồn tại hoặc đã ngưng sử dụng.");
            medicine.CategoryId = dto.CategoryId;
        }
        if (dto.SuppliedFields.Contains(nameof(dto.ActiveIngredient))) medicine.ActiveIngredient = dto.ActiveIngredient;
        if (dto.SuppliedFields.Contains(nameof(dto.Strength))) medicine.Strength = dto.Strength;
        if (dto.SuppliedFields.Contains(nameof(dto.DosageForm))) medicine.DosageForm = dto.DosageForm;
        if (dto.SuppliedFields.Contains(nameof(dto.Manufacturer))) medicine.Manufacturer = dto.Manufacturer;
        if (dto.SuppliedFields.Contains(nameof(dto.IsPrescriptionRequired))) medicine.IsPrescriptionRequired = dto.IsPrescriptionRequired;
        if (dto.SuppliedFields.Contains(nameof(dto.Description))) medicine.Description = dto.Description;
        if (dto.SuppliedFields.Contains(nameof(dto.StorageInstructions))) medicine.StorageInstructions = dto.StorageInstructions;
        // Image paths are storage-owned: changing them requires the image endpoints.
        if (dto.SuppliedFields.Contains(nameof(dto.ImagePath)) && dto.ImagePath != medicine.ImagePath)
            throw new BusinessException("IMAGE_UPLOAD_REQUIRED", "Vui lòng dùng chức năng tải lên hoặc xóa ảnh thuốc.");
    }
}
