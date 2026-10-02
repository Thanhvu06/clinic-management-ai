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

        builder.Property(m => m.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(m => m.RowVersion).IsRowVersion();
    }
}
