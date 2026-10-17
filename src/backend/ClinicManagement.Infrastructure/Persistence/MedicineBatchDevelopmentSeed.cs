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
        if (await db.MedicineBatches.AnyAsync()) return;
        var medicines = await db.Medicines.Where(medicine => medicine.StockQuantity > 0).OrderBy(medicine => medicine.Id).ToListAsync();
        foreach (var medicine in medicines)
        {
            var first = medicine.StockQuantity / 2;
            db.MedicineBatches.AddRange(
                Batch(medicine, $"DEMO-{medicine.Id}-60", clock.VietnamToday.AddDays(60), first),
                Batch(medicine, $"DEMO-{medicine.Id}-400", clock.VietnamToday.AddDays(400), medicine.StockQuantity - first));
        }
        if (medicines.FirstOrDefault(medicine => medicine.StockQuantity < int.MaxValue) is { } demoMedicine)
        {
            var quantity = Math.Min(5, int.MaxValue - demoMedicine.StockQuantity);
            var expired = Batch(demoMedicine, $"DEMO-{demoMedicine.Id}-EXPIRED", clock.VietnamToday.AddDays(-15), quantity);
            db.MedicineBatches.Add(expired);
            demoMedicine.StockQuantity += quantity;
            demoMedicine.UpdatedAt = clock.UtcNow;
            db.MedicineStockTransactions.Add(new MedicineStockTransaction
            {
                MedicineId = demoMedicine.Id, MedicineBatch = expired, Type = MedicineStockTransactionType.StockIn,
                QuantityChange = quantity, BalanceAfter = demoMedicine.StockQuantity, ActorUserId = actor,
                Reason = "Lô hết hạn minh họa dữ liệu demo", CreatedAt = clock.UtcNow
            });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        MedicineBatch Batch(Medicine medicine, string number, DateOnly expiry, int quantity) => new()
        {
            Medicine = medicine, BatchNumber = number, ExpiryDate = expiry, QuantityReceived = quantity,
            QuantityRemaining = quantity, ReceivedAt = clock.UtcNow, CreatedByUserId = actor
        };
    }
}
