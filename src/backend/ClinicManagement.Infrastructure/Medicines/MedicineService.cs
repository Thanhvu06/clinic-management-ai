using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Pharmacy;
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
    private readonly IDateTimeProvider _dateTimeProvider;

    public MedicineService(AppDbContext dbContext, ICurrentUserService currentUserService, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<List<ActiveMedicineDto>> GetActiveMedicinesAsync()
    {
        return await _dbContext.Medicines
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .WithUsableStock(_dbContext, _dateTimeProvider.VietnamToday)
            .Select(stock => new ActiveMedicineDto
            {
                Id = stock.Medicine.Id,
                Code = stock.Medicine.Code,
                Name = stock.Medicine.Name,
                Unit = stock.Medicine.Unit,
                StockQuantity = stock.Medicine.StockQuantity,
                UsableStock = stock.Quantity,
                ImageUrl = stock.Medicine.ImagePath == null ? null : "/media/medicines/" + stock.Medicine.ImagePath,
                ReorderLevel = stock.Medicine.ReorderLevel,
                UnitPrice = stock.Medicine.UnitPrice,
                IsPrescriptionRequired = stock.Medicine.IsPrescriptionRequired,
                Strength = stock.Medicine.Strength
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

        await PopulateUsableStocksAsync(items);
        return new PagedResult<MedicineDto>(items, totalItems, page, pageSize);
    }

    public async Task<MedicineDto> GetMedicineByIdAsync(long id)
    {
        var m = await _dbContext.Medicines.AsNoTracking().Where(x => x.Id == id).Select(MedicineProjections.Admin).FirstOrDefaultAsync();
        if (m == null) throw new NotFoundException("Thuốc không tồn tại.");

        await PopulateUsableStocksAsync(new[] { m });
        return m;
    }

    private async Task PopulateUsableStocksAsync(IReadOnlyCollection<MedicineDto> medicines)
    {
        var ids = medicines.Select(medicine => medicine.Id).ToList();
        var stocks = await _dbContext.Medicines.AsNoTracking().Where(medicine => ids.Contains(medicine.Id))
            .WithUsableStock(_dbContext, _dateTimeProvider.VietnamToday)
            .ToDictionaryAsync(stock => stock.Medicine.Id, stock => stock.Quantity);
        foreach (var medicine in medicines) medicine.UsableStock = stocks.GetValueOrDefault(medicine.Id);
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
        if (dto.SuppliedFields.Contains(nameof(dto.Indications))) medicine.Indications = dto.Indications;
        if (dto.SuppliedFields.Contains(nameof(dto.DosageAndAdministration))) medicine.DosageAndAdministration = dto.DosageAndAdministration;
        if (dto.SuppliedFields.Contains(nameof(dto.Contraindications))) medicine.Contraindications = dto.Contraindications;
        if (dto.SuppliedFields.Contains(nameof(dto.SideEffects))) medicine.SideEffects = dto.SideEffects;
        if (dto.SuppliedFields.Contains(nameof(dto.Precautions))) medicine.Precautions = dto.Precautions;
        if (dto.SuppliedFields.Contains(nameof(dto.OverdoseManagement))) medicine.OverdoseManagement = dto.OverdoseManagement;
        // Image paths are storage-owned: changing them requires the image endpoints.
        if (dto.SuppliedFields.Contains(nameof(dto.ImagePath)) && dto.ImagePath != medicine.ImagePath)
            throw new BusinessException("IMAGE_UPLOAD_REQUIRED", "Vui lòng dùng chức năng tải lên hoặc xóa ảnh thuốc.");
    }
}
