using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
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
            .HasConversion<string>()
            .HasDefaultValue(DiagnosticItemStatus.Ordered);

        builder.Property(i => i.RowVersion).IsRowVersion();
    }
}