using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

public static class MedicineBatchDevelopmentSeed
{
    public static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid actor, bool demoEnabled)
    {
        if (!demoEnabled) return;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var batches = await db.MedicineBatches.OrderBy(batch => batch.Id).ToListAsync();
        var usedNumbers = batches.Select(batch => batch.BatchNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sequence = 1;
        string NextBatchNumber()
        {
            while (sequence <= 999)
            {
                var number = $"LOT-{clock.VietnamNow:yyMM}-{sequence++:D3}";
                if (usedNumbers.Add(number)) return number;
            }
            throw new InvalidOperationException("Không còn số lô trống cho tháng hiện tại.");
        }
        var legacy = batches.Where(batch => batch.BatchNumber.StartsWith("DEMO-", StringComparison.Ordinal)).ToList();
        foreach (var batch in legacy) batch.BatchNumber = NextBatchNumber();
        var legacyIds = legacy.Select(batch => batch.Id).ToList();
        foreach (var entry in await db.MedicineStockTransactions.Where(entry => entry.MedicineBatchId.HasValue && legacyIds.Contains(entry.MedicineBatchId.Value) && entry.Reason == "Lô hết hạn minh họa dữ liệu demo").ToListAsync())
            entry.Reason = "Nhập lô thuốc hết hạn";
        if (batches.Count > 0)
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return;
        }
        var medicines = await db.Medicines.Where(medicine => medicine.StockQuantity > 0).OrderBy(medicine => medicine.Id).ToListAsync();
        var index = 0;
        var receivedAt = RealisticDevelopmentSeed.At(clock, clock.VietnamToday.AddDays(-30), 8);
        foreach (var medicine in medicines)
        {
            var first = medicine.StockQuantity / 2;
            db.MedicineBatches.AddRange(
                Batch(medicine, index < 3 ? clock.VietnamToday.AddDays(20 + index * 15) : clock.VietnamToday.AddMonths(6 + index % 19), first),
                Batch(medicine, clock.VietnamToday.AddMonths(6 + (index + 9) % 19), medicine.StockQuantity - first));
            index++;
        }
        if (medicines.FirstOrDefault(medicine => medicine.StockQuantity < int.MaxValue) is { } demoMedicine)
        {
            var quantity = Math.Min(5, int.MaxValue - demoMedicine.StockQuantity);
            var expired = Batch(demoMedicine, clock.VietnamToday.AddDays(-15), quantity);
            db.MedicineBatches.Add(expired);
            demoMedicine.StockQuantity += quantity;
            demoMedicine.UpdatedAt = receivedAt;
            db.MedicineStockTransactions.Add(new MedicineStockTransaction
            {
                MedicineId = demoMedicine.Id, MedicineBatch = expired, Type = MedicineStockTransactionType.StockIn,
                QuantityChange = quantity, BalanceAfter = demoMedicine.StockQuantity, ActorUserId = actor,
                Reason = "Nhập lô thuốc hết hạn", CreatedAt = receivedAt
            });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        MedicineBatch Batch(Medicine medicine, DateOnly expiry, int quantity) => new()
        {
            Medicine = medicine, BatchNumber = NextBatchNumber(), ExpiryDate = expiry, QuantityReceived = quantity,
            QuantityRemaining = quantity, ReceivedAt = receivedAt, CreatedByUserId = actor
        };
    }
}
