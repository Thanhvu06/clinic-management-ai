using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AppointmentChangeRequestConfiguration : IEntityTypeConfiguration<AppointmentChangeRequest>
{
    public void Configure(EntityTypeBuilder<AppointmentChangeRequest> builder)
    {
        builder.ToTable("AppointmentChangeRequests");

        builder.HasKey(acr => acr.Id);
        builder.Property(acr => acr.Id).ValueGeneratedOnAdd();

        builder.HasOne(acr => acr.Appointment)
            .WithMany(a => a.ChangeRequests)
            .HasForeignKey(acr => acr.AppointmentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(acr => acr.RequestedSlot)
            .WithMany()
            .HasForeignKey(acr => acr.RequestedSlotId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(acr => acr.RequestedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(acr => acr.ProcessedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(acr => acr.RequestType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(acr => acr.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(AppointmentChangeRequestStatus.Pending);

        builder.Property(acr => acr.OriginalAppointmentStatus)
            .HasConversion<string>();

        builder.Property(acr => acr.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(acr => acr.ProcessedAt)
            .HasColumnType("datetime2");

        builder.HasIndex(acr => acr.AppointmentId)
            .IsUnique()
            .HasFilter("[Status] = 'Pending'");
    }
}
