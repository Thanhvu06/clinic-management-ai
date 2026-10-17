using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Pharmacy.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Pharmacy;

public class MedicineBatchService(AppDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
{
    public async Task<PagedResult<MedicineBatchDto>> ListAsync(string? status, string? search, int page, int pageSize)
    {
        var today = clock.VietnamToday;
        var expiring = today.AddDays(90);
        var query = db.MedicineBatches.AsNoTracking().Include(batch => batch.Medicine).AsQueryable();
        var filter = (status ?? "all").Trim().ToLowerInvariant();
        if (filter != "all") query = query.Where(batch => batch.QuantityRemaining > 0 && batch.DisposedAt == null);
        query = filter switch
        {
            "all" => query,
            "valid" => query.Where(batch => batch.ExpiryDate >= today),
            "expiring" => query.Where(batch => batch.ExpiryDate >= today && batch.ExpiryDate <= expiring),
            "expired" => query.Where(batch => batch.ExpiryDate < today),
            _ => throw new BusinessException("INVALID_BATCH_STATUS", "Trạng thái lô thuốc không hợp lệ.")
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var key = search.Trim().ToLowerInvariant();
            query = query.Where(batch => batch.Medicine.Code.ToLower().Contains(key) || batch.Medicine.Name.ToLower().Contains(key));
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var count = await query.CountAsync();
        var batches = await query.OrderBy(batch => batch.ExpiryDate).ThenBy(batch => batch.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<MedicineBatchDto>(batches.Select(Map).ToList(), count, page, pageSize);
    }

    public async Task<MedicineBatchDto> CreateAsync(CreateMedicineBatchDto request)
    {
        var number = request.BatchNumber?.Trim() ?? string.Empty;
        if (number.Length is 0 or > 50) throw new BusinessException("INVALID_BATCH_NUMBER", "Vui lòng nhập số lô tối đa 50 ký tự.");
        if (request.ExpiryDate <= clock.VietnamToday) throw new BusinessException("INVALID_EXPIRY_DATE", "Hạn dùng lô thuốc phải sau ngày hôm nay.");
        if (request.Quantity <= 0 || request.UnitCost < 0) throw new BusinessException("INVALID_QUANTITY", "Số lượng phải lớn hơn 0 và giá nhập không được âm.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var medicine = await db.Medicines.SingleOrDefaultAsync(medicine => medicine.Id == request.MedicineId)
                ?? throw new NotFoundException("Thuốc không tồn tại.");
            if (await db.MedicineBatches.AnyAsync(batch => batch.MedicineId == medicine.Id && batch.BatchNumber == number && batch.ExpiryDate == request.ExpiryDate))
                throw new BusinessException("BATCH_EXISTS", "Lô thuốc với số lô và hạn dùng này đã tồn tại.");
            if (request.Quantity > int.MaxValue - medicine.StockQuantity)
                throw new BusinessException("INVALID_QUANTITY", "Số lượng nhập vượt quá giới hạn tồn kho.");
            var batch = new MedicineBatch
            {
                Medicine = medicine, BatchNumber = number, ExpiryDate = request.ExpiryDate,
                QuantityReceived = request.Quantity, QuantityRemaining = request.Quantity, UnitCost = request.UnitCost,
                ReceivedAt = clock.UtcNow, CreatedByUserId = currentUser.UserId ?? Guid.Empty
            };
            medicine.StockQuantity += request.Quantity;
            medicine.UpdatedAt = clock.UtcNow;
            db.MedicineBatches.Add(batch);
            db.MedicineStockTransactions.Add(new MedicineStockTransaction
            {
                MedicineId = medicine.Id, MedicineBatch = batch, Type = MedicineStockTransactionType.StockIn,
                QuantityChange = request.Quantity, BalanceAfter = medicine.StockQuantity,
                ActorUserId = batch.CreatedByUserId, Reason = $"Nhập lô {number}", CreatedAt = clock.UtcNow
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Map(batch);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 }
            or SqliteException { SqliteExtendedErrorCode: 2067 or 1555 })
        {
            throw new BusinessException("BATCH_EXISTS", "Lô thuốc với số lô và hạn dùng này đã tồn tại.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("BATCH_CONFLICT", "Tồn kho đang được cập nhật. Vui lòng thử lại.");
        }
    }

    public async Task<MedicineBatchDto> DisposeAsync(long id, DisposeMedicineBatchDto request)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500) throw new BusinessException("REASON_REQUIRED", "Lý do hủy lô phải có từ 5 đến 500 ký tự.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var batch = await db.MedicineBatches.Include(batch => batch.Medicine).SingleOrDefaultAsync(batch => batch.Id == id)
            ?? throw new NotFoundException("Lô thuốc không tồn tại.");
        if (batch.DisposedAt != null) throw new BusinessException("BATCH_ALREADY_DISPOSED", "Lô thuốc đã được hủy.");
        if (batch.ExpiryDate >= clock.VietnamToday && batch.QuantityRemaining == 0)
            throw new BusinessException("EMPTY_BATCH", "Lô thuốc còn hạn đã hết tồn kho.");
        if (batch.Medicine.StockQuantity < batch.QuantityRemaining)
            throw new BusinessException("INVALID_BATCH_STOCK", "Tồn kho lô không khớp tổng tồn kho thuốc.");
        var quantity = batch.QuantityRemaining;
        batch.Medicine.StockQuantity -= quantity;
        batch.Medicine.UpdatedAt = clock.UtcNow;
        batch.QuantityRemaining = 0;
        batch.DisposedAt = clock.UtcNow;
        batch.DisposeReason = reason;
        db.MedicineStockTransactions.Add(new MedicineStockTransaction
        {
            MedicineId = batch.MedicineId, MedicineBatchId = batch.Id, Type = MedicineStockTransactionType.Disposal,
            QuantityChange = -quantity, BalanceAfter = batch.Medicine.StockQuantity,
            ActorUserId = currentUser.UserId ?? Guid.Empty, Reason = reason, CreatedAt = clock.UtcNow
        });
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("BATCH_CONFLICT", "Lô thuốc đang được cập nhật. Vui lòng thử lại.");
        }
        return Map(batch);
    }

    private MedicineBatchDto Map(MedicineBatch batch)
    {
        var days = batch.ExpiryDate.DayNumber - clock.VietnamToday.DayNumber;
        return new MedicineBatchDto(batch.Id, batch.MedicineId, batch.Medicine.Code, batch.Medicine.Name,
            batch.Medicine.Unit, batch.BatchNumber, batch.ExpiryDate, batch.QuantityReceived, batch.QuantityRemaining,
            days, batch.DisposedAt != null ? "Disposed" : days < 0 ? "Expired" : days <= 90 ? "Expiring" : "Valid");
    }
}
