using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.MedicalRecordNumber).HasMaxLength(50).IsRequired();
        builder.HasIndex(p => p.MedicalRecordNumber).IsUnique();

        builder.Property(p => p.FullName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.PhoneNumber).HasMaxLength(50);
        builder.Property(p => p.Email).HasMaxLength(150);
        builder.Property(p => p.NationalId).HasMaxLength(50);
        builder.HasIndex(p => p.NationalId);
        builder.Property(p => p.BhytNumber).HasMaxLength(50);
        builder.HasIndex(p => p.BhytNumber);
        builder.Property(p => p.BloodType).HasMaxLength(10);
        builder.Property(p => p.RhFactor).HasMaxLength(10);

        builder.HasIndex(p => p.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL");

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Patient>(p => p.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(p => p.PrimaryFacility)
            .WithMany()
            .HasForeignKey(p => p.PrimaryFacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.Gender)
            .HasConversion<string>();

        builder.Property(p => p.DateOfBirth)
            .HasColumnType("date");
    }
}
