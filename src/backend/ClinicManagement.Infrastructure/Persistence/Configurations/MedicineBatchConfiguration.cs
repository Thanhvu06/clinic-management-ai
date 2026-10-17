using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class MedicineBatchConfiguration : IEntityTypeConfiguration<MedicineBatch>
{
    public void Configure(EntityTypeBuilder<MedicineBatch> builder)
    {
        builder.ToTable("MedicineBatches", table =>
        {
            table.HasCheckConstraint("CK_MedicineBatch_QuantityRemaining", "[QuantityRemaining] >= 0");
            table.HasCheckConstraint("CK_MedicineBatch_QuantityReceived", "[QuantityReceived] >= 0");
        });
        builder.HasKey(batch => batch.Id);
        builder.Property(batch => batch.BatchNumber).HasMaxLength(50).IsRequired();
        builder.Property(batch => batch.ExpiryDate).HasColumnType("date");
        builder.Property(batch => batch.UnitCost).HasPrecision(18, 2);
        builder.Property(batch => batch.DisposeReason).HasMaxLength(500);
        builder.Property(batch => batch.RowVersion).IsRowVersion();
        builder.HasIndex(batch => new { batch.MedicineId, batch.BatchNumber, batch.ExpiryDate }).IsUnique();
        builder.HasOne(batch => batch.Medicine).WithMany().HasForeignKey(batch => batch.MedicineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<MedicineStockTransaction>().WithOne(transaction => transaction.MedicineBatch)
            .HasForeignKey(transaction => transaction.MedicineBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}
