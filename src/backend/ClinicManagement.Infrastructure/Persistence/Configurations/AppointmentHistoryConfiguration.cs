using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AppointmentHistoryConfiguration : IEntityTypeConfiguration<AppointmentHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentHistory> builder)
    {
        builder.ToTable("AppointmentHistory");

        builder.HasKey(ah => ah.Id);
        builder.Property(ah => ah.Id).ValueGeneratedOnAdd();

        builder.HasOne(ah => ah.Appointment)
            .WithMany(a => a.Histories)
            .HasForeignKey(ah => ah.AppointmentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(ah => ah.PerformedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(ah => ah.Action)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(ah => ah.OldStatus)
            .HasConversion<string>();

        builder.Property(ah => ah.NewStatus)
            .HasConversion<string>();

        builder.Property(ah => ah.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2");
    }
}
