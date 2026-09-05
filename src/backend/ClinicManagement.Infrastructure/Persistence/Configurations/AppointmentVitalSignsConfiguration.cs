using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AppointmentVitalSignsConfiguration : IEntityTypeConfiguration<AppointmentVitalSigns>
{
    public void Configure(EntityTypeBuilder<AppointmentVitalSigns> builder)
    {
        builder.ToTable("AppointmentVitalSigns");

        builder.HasKey(vs => vs.Id);
        builder.Property(vs => vs.Id).ValueGeneratedOnAdd();

        builder.HasOne(vs => vs.Appointment)
            .WithOne(a => a.VitalSigns)
            .HasForeignKey<AppointmentVitalSigns>(vs => vs.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(vs => vs.AppointmentId).IsUnique();

        builder.Property(vs => vs.Temperature).HasPrecision(4, 1);
        builder.Property(vs => vs.Weight).HasPrecision(5, 2);
        builder.Property(vs => vs.Height).HasPrecision(5, 1);
        builder.Property(vs => vs.Bmi).HasPrecision(4, 1);

        builder.Property(vs => vs.RecordedAtUtc).IsRequired();
        builder.Property(vs => vs.RecordedByUserId).IsRequired();

        builder.Property(vs => vs.RowVersion).IsRowVersion();
    }
}
