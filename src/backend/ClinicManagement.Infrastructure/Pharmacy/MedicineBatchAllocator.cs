using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Pharmacy;

public record MedicineBatchDeduction(long? MedicineBatchId, int Quantity);

// Callers own the stock transaction and SaveChanges, including the medicine rowversion check.
public class MedicineBatchAllocator(AppDbContext db, IDateTimeProvider clock)
{
    public async Task<List<MedicineBatchDeduction>> Deduct(Medicine medicine, int quantity,
        MedicineStockTransactionType type, long? prescriptionId, string reason, Guid actor)
    {
        if (quantity <= 0) throw new BusinessException("INVALID_QUANTITY", "Số lượng thuốc phải lớn hơn 0.");
        var today = clock.VietnamToday;
        var unbatchedFirst = type == MedicineStockTransactionType.Adjustment;
        var allowExpired = unbatchedFirst;
        var batches = await db.MedicineBatches.Where(batch => batch.MedicineId == medicine.Id)
            .OrderBy(batch => batch.ExpiryDate).ThenBy(batch => batch.Id).ToListAsync();
        var unbatched = medicine.StockQuantity - batches.Sum(batch => batch.QuantityRemaining);
        if (unbatched < 0)
            throw new BusinessException("INVALID_BATCH_STOCK", "Tồn kho lô không khớp tổng tồn kho thuốc.");
        var available = medicine.StockQuantity - (allowExpired ? 0 : batches.Where(batch => batch.ExpiryDate < today).Sum(batch => batch.QuantityRemaining));
        if (available < quantity)
            throw new BusinessException("INSUFFICIENT_MEDICINE_STOCK",
                $"Thuốc '{medicine.Name}' không đủ tồn kho còn hạn! Yêu cầu: {quantity}, hiện còn dùng được: {available}.");

        var result = new List<MedicineBatchDeduction>();
        var remaining = quantity;
        void Take(MedicineBatch? batch, int count)
        {
            if (count == 0) return;
            if (batch != null) batch.QuantityRemaining -= count;
            medicine.StockQuantity -= count;
            medicine.UpdatedAt = clock.UtcNow;
            remaining -= count;
            db.MedicineStockTransactions.Add(new MedicineStockTransaction
            {
                MedicineId = medicine.Id, MedicineBatchId = batch?.Id, Type = type,
                QuantityChange = -count, BalanceAfter = medicine.StockQuantity,
                PrescriptionId = prescriptionId, ActorUserId = actor, Reason = reason, CreatedAt = clock.UtcNow
            });
            result.Add(new MedicineBatchDeduction(batch?.Id, count));
        }

        if (unbatchedFirst) Take(null, Math.Min(remaining, unbatched));
        foreach (var batch in batches.Where(batch => batch.QuantityRemaining > 0 && (allowExpired || batch.ExpiryDate >= today)))
        {
            Take(batch, Math.Min(remaining, batch.QuantityRemaining));
            if (remaining == 0) break;
        }
        if (!unbatchedFirst) Take(null, Math.Min(remaining, unbatched));
        return result;
    }

    public async Task Return(Medicine medicine, long prescriptionId, string reason, Guid actor,
        CancellationToken cancellationToken = default)
    {
        var transactions = await db.MedicineStockTransactions
            .Where(transaction => transaction.PrescriptionId == prescriptionId && transaction.MedicineId == medicine.Id &&
                (transaction.Type == MedicineStockTransactionType.Reservation || transaction.Type == MedicineStockTransactionType.ReservationCancelled))
            .ToListAsync(cancellationToken);
        transactions.AddRange(db.ChangeTracker.Entries<MedicineStockTransaction>()
            .Where(entry => entry.State == EntityState.Added && entry.Entity.PrescriptionId == prescriptionId && entry.Entity.MedicineId == medicine.Id &&
                (entry.Entity.Type == MedicineStockTransactionType.Reservation || entry.Entity.Type == MedicineStockTransactionType.ReservationCancelled))
            .Select(entry => entry.Entity));
        foreach (var group in transactions.GroupBy(transaction => transaction.MedicineBatchId).OrderBy(group => group.Key))
        {
            var quantity = -group.Sum(transaction => transaction.QuantityChange);
            if (quantity <= 0) continue;
            if (group.Key.HasValue)
            {
                var batch = await db.MedicineBatches.SingleAsync(batch => batch.Id == group.Key.Value && batch.MedicineId == medicine.Id, cancellationToken);
                batch.QuantityRemaining += quantity;
                // Reserved units were not disposed; returning them reopens the batch.
                batch.DisposedAt = null;
                batch.DisposeReason = null;
            }
            medicine.StockQuantity += quantity;
            medicine.UpdatedAt = clock.UtcNow;
            db.MedicineStockTransactions.Add(new MedicineStockTransaction
            {
                MedicineId = medicine.Id, MedicineBatchId = group.Key, Type = MedicineStockTransactionType.ReservationCancelled,
                QuantityChange = quantity, BalanceAfter = medicine.StockQuantity,
                PrescriptionId = prescriptionId, ActorUserId = actor, Reason = reason, CreatedAt = clock.UtcNow
            });
        }
    }

    public async Task EnsureReservationsUsableAsync(long prescriptionId)
    {
        var expiredReservations = await db.MedicineStockTransactions
            .Where(transaction => transaction.PrescriptionId == prescriptionId && transaction.MedicineBatchId != null &&
                transaction.MedicineBatch!.ExpiryDate < clock.VietnamToday &&
                (transaction.Type == MedicineStockTransactionType.Reservation || transaction.Type == MedicineStockTransactionType.ReservationCancelled))
            .GroupBy(transaction => transaction.MedicineBatchId)
            .AnyAsync(group => group.Sum(transaction => transaction.QuantityChange) < 0);
        if (expiredReservations)
            throw new BusinessException("INSUFFICIENT_MEDICINE_STOCK", "Lô thuốc đã giữ chỗ đã hết hạn, không đủ tồn kho còn hạn để cấp phát. Vui lòng hủy giữ chỗ và chọn lô còn hạn.");
    }
}
