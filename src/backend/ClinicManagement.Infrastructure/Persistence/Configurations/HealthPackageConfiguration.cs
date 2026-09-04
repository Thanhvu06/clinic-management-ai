using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class HealthPackageConfiguration : IEntityTypeConfiguration<HealthPackage>
{
    public void Configure(EntityTypeBuilder<HealthPackage> builder)
    {
        builder.ToTable("HealthPackages");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedOnAdd();

        builder.Property(h => h.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(h => h.Code).IsUnique();

        builder.Property(h => h.Name).HasMaxLength(200).IsRequired();
        builder.Property(h => h.TargetAudience).HasMaxLength(100).IsRequired();
        builder.Property(h => h.Description).IsRequired();
        builder.Property(h => h.Price).HasPrecision(18, 2).IsRequired();
        builder.Property(h => h.ImageUrl).HasMaxLength(500);
        builder.Property(h => h.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(h => h.CreatedAt).IsRequired();
    }
}
