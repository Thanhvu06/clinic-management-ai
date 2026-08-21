using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DoctorSpecialtyConfiguration : IEntityTypeConfiguration<DoctorSpecialty>
{
    public void Configure(EntityTypeBuilder<DoctorSpecialty> builder)
    {
        builder.ToTable("DoctorSpecialties");

        builder.HasKey(ds => new { ds.DoctorId, ds.SpecialtyId });

        builder.Property(ds => ds.IsPrimary)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasOne(ds => ds.Doctor)
            .WithMany(d => d.DoctorSpecialties)
            .HasForeignKey(ds => ds.DoctorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(ds => ds.Specialty)
            .WithMany(s => s.DoctorSpecialties)
            .HasForeignKey(ds => ds.SpecialtyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(ds => ds.DoctorId)
            .IsUnique()
            .HasFilter("[IsPrimary] = 1");
    }
}
