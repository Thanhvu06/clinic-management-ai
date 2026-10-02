using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable("Buildings");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedOnAdd();

        builder.Property(b => b.Code).HasMaxLength(50).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();

        builder.HasIndex(b => new { b.FacilityId, b.Code }).IsUnique();

        builder.HasOne(b => b.Facility)
            .WithMany(f => f.Buildings)
            .HasForeignKey(b => b.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
