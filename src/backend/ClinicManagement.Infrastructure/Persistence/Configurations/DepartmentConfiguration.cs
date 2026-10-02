using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();

        builder.Property(d => d.Code).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();

        builder.HasIndex(d => new { d.FacilityId, d.Code }).IsUnique();

        builder.Property(d => d.DepartmentType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasOne(d => d.Facility)
            .WithMany(f => f.Departments)
            .HasForeignKey(d => d.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Building)
            .WithMany(b => b.Departments)
            .HasForeignKey(d => d.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.HeadOfDepartmentDoctor)
            .WithMany()
            .HasForeignKey(d => d.HeadOfDepartmentDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Specialty)
            .WithMany()
            .HasForeignKey(d => d.SpecialtyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.SpecialtyId);
    }
}
