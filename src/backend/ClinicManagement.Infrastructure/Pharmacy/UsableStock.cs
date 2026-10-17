using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Pharmacy;

public sealed class MedicineWithUsableStock
{
    public Medicine Medicine { get; init; } = null!;
    public int Quantity { get; init; }
}

public static class UsableStock
{
    // Keep the correlated sum in the query so EF can project, filter and count in SQL.
    public static IQueryable<MedicineWithUsableStock> WithUsableStock(this IQueryable<Medicine> medicines,
        AppDbContext db, DateOnly today) => medicines.Select(medicine => new MedicineWithUsableStock
        {
            Medicine = medicine,
            Quantity = medicine.StockQuantity - (db.MedicineBatches
                .Where(batch => batch.MedicineId == medicine.Id && batch.ExpiryDate < today && batch.DisposedAt == null)
                .Sum(batch => (int?)batch.QuantityRemaining) ?? 0)
        });
}
