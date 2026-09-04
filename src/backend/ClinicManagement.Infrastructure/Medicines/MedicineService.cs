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
                StockQuantity = m.StockQuantity
            })
            .ToListAsync();
    }

    public async Task<PagedResult<MedicineDto>> GetMedicinesAsync(string? search, bool? isActive, int page, int pageSize)
    {
        var query = _dbContext.Medicines.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(m => m.Code.ToLower().Contains(s) || m.Name.ToLower().Contains(s));
        }

        if (isActive.HasValue)
        {
            query = query.Where(m => m.IsActive == isActive.Value);
        }

        query = query.OrderBy(m => m.Name);

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MedicineDto
            {
                Id = m.Id,
                Code = m.Code,
                Name = m.Name,
                Unit = m.Unit,
                StockQuantity = m.StockQuantity,
                ReorderLevel = m.ReorderLevel,
                IsActive = m.IsActive,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt
            })
            .ToListAsync();

        return new PagedResult<MedicineDto>(items, totalItems, page, pageSize);
    }

    public async Task<MedicineDto> GetMedicineByIdAsync(long id)
    {
        var m = await _dbContext.Medicines.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) throw new NotFoundException("Thuốc không tồn tại.");

        return new MedicineDto
        {
            Id = m.Id,
            Code = m.Code,
            Name = m.Name,
            Unit = m.Unit,
            StockQuantity = m.StockQuantity,
            ReorderLevel = m.ReorderLevel,
            IsActive = m.IsActive,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt
        };
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
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

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
        medicine.Unit = dto.Unit.Trim();
        medicine.ReorderLevel = dto.ReorderLevel;
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
}
