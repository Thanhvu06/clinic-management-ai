using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DiagnosticOrderItemConfiguration : IEntityTypeConfiguration<DiagnosticOrderItem>
{
    public void Configure(EntityTypeBuilder<DiagnosticOrderItem> builder)
    {
        builder.ToTable("DiagnosticOrderItems");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        builder.HasOne(i => i.DiagnosticOrder)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.DiagnosticOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.DiagnosticService)
            .WithMany()
            .HasForeignKey(i => i.DiagnosticServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.DiagnosticOrderId, i.DiagnosticServiceId }).IsUnique();

        builder.Property(i => i.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(i => i.IsPackageCovered)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasOne(i => i.PackageRegistration)
            .WithMany()
            .HasForeignKey(i => i.PackageRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.RowVersion).IsRowVersion();
        builder.Property(i => i.SpecimenStatus).HasConversion<string>().HasMaxLength(30)
            .HasDefaultValue(ClinicManagement.Domain.Enums.SpecimenStatus.NotRequired);
        builder.Property(i => i.SpecimenBarcode).HasMaxLength(40);
        builder.HasIndex(i => i.SpecimenBarcode).IsUnique().HasFilter("[SpecimenBarcode] IS NOT NULL");
        builder.Property(i => i.RejectionReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.RejectionNote).HasMaxLength(500);
        builder.Property(i => i.RecollectCount).HasDefaultValue(0);
    }
}
