using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("InvoiceItems", t =>
        {
            t.HasCheckConstraint("CK_InvoiceItems_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_InvoiceItems_UnitPrice_NonNegative", "[UnitPrice] >= 0");
            t.HasCheckConstraint("CK_InvoiceItems_LineTotal_NonNegative", "[LineTotal] >= 0");
        });

        builder.HasKey(ii => ii.Id);

        builder.Property(ii => ii.ItemCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(ii => ii.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(ii => ii.Quantity)
            .IsRequired();

        builder.Property(ii => ii.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(ii => ii.LineTotal)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(ii => ii.ReferenceType)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(ii => ii.InvoiceId);
    }
}
