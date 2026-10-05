using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class MedicineConfiguration : IEntityTypeConfiguration<Medicine>
{
    public void Configure(EntityTypeBuilder<Medicine> builder)
    {
        builder.ToTable("Medicines");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        builder.Property(m => m.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(m => m.Code).IsUnique();

        builder.Property(m => m.Name).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Unit).HasMaxLength(50).IsRequired();

        builder.Property(m => m.UnitPrice).HasPrecision(18, 2);
        builder.Property(m => m.ActiveIngredient).HasMaxLength(200);
        builder.Property(m => m.Strength).HasMaxLength(100);
        builder.Property(m => m.DosageForm).HasMaxLength(100);
        builder.Property(m => m.Manufacturer).HasMaxLength(200);
        builder.Property(m => m.Description).HasMaxLength(1000);
        builder.Property(m => m.StorageInstructions).HasMaxLength(500);
        builder.Property(m => m.ImagePath).HasMaxLength(300);
        builder.Property(m => m.IsPrescriptionRequired).HasDefaultValue(true).IsRequired();
        builder.HasOne(m => m.Category).WithMany(c => c.Medicines)
            .HasForeignKey(m => m.CategoryId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(m => m.RowVersion).IsRowVersion();
    }
}
