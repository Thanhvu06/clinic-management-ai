using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class FacilityConfiguration : IEntityTypeConfiguration<Facility>
{
    public void Configure(EntityTypeBuilder<Facility> builder)
    {
        builder.ToTable("Facilities");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedOnAdd();

        builder.Property(f => f.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(f => f.Code).IsUnique();

        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();
        builder.Property(f => f.Address).HasMaxLength(500).IsRequired();
        builder.Property(f => f.City).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Phone).HasMaxLength(50).IsRequired();
        builder.Property(f => f.Email).HasMaxLength(150);
        builder.Property(f => f.TaxCode).HasMaxLength(50);
        builder.Property(f => f.HospitalLevel).HasMaxLength(50);
        builder.Property(f => f.IsActive).IsRequired().HasDefaultValue(true);
        builder.HasIndex(f => f.IsActive);
    }
}
