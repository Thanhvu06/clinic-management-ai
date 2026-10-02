using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", t =>
        {
            t.HasCheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PaymentCode)
            .IsRequired()
            .HasMaxLength(50);
        builder.HasIndex(p => p.PaymentCode)
            .IsUnique();

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.ReferenceCode)
            .HasMaxLength(100);

        builder.Property(p => p.Note)
            .HasMaxLength(500);

        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        builder.HasIndex(p => p.InvoiceId);
        builder.HasIndex(p => p.ReceivedAtUtc);
        builder.HasIndex(p => p.Status);
    }
}
