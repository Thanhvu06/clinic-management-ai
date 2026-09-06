using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", t =>
        {
            t.HasCheckConstraint("CK_Invoices_SingleSource", "([AppointmentId] IS NOT NULL AND [HealthPackageRegistrationId] IS NULL) OR ([AppointmentId] IS NULL AND [HealthPackageRegistrationId] IS NOT NULL)");
            t.HasCheckConstraint("CK_Invoices_Subtotal_NonNegative", "[Subtotal] >= 0");
            t.HasCheckConstraint("CK_Invoices_TotalAmount_NonNegative", "[TotalAmount] >= 0");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceCode)
            .IsRequired()
            .HasMaxLength(50);
        builder.HasIndex(i => i.InvoiceCode)
            .IsUnique();

        builder.Property(i => i.Subtotal)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.TotalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.CancellationReason)
            .HasMaxLength(500);

        builder.Property(i => i.RowVersion)
            .IsRowVersion();

        builder.HasIndex(i => i.PatientId);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.CreatedAtUtc);
        builder.HasIndex(i => new { i.PatientId, i.CreatedAtUtc });

        builder.HasIndex(i => i.AppointmentId)
            .IsUnique()
            .HasFilter("[AppointmentId] IS NOT NULL AND [Status] <> 3");

        builder.HasIndex(i => i.HealthPackageRegistrationId)
            .IsUnique()
            .HasFilter("[HealthPackageRegistrationId] IS NOT NULL AND [Status] <> 3");

        builder.HasOne(i => i.Patient)
            .WithMany()
            .HasForeignKey(i => i.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Appointment)
            .WithMany()
            .HasForeignKey(i => i.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.HealthPackageRegistration)
            .WithMany()
            .HasForeignKey(i => i.HealthPackageRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Items)
            .WithOne(item => item.Invoice)
            .HasForeignKey(item => item.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Payments)
            .WithOne(p => p.Invoice)
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
