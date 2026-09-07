using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DiagnosticResultConfiguration : IEntityTypeConfiguration<DiagnosticResult>
{
    public void Configure(EntityTypeBuilder<DiagnosticResult> builder)
    {
        builder.ToTable("DiagnosticResults");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        builder.HasOne(r => r.DiagnosticOrderItem)
            .WithOne(i => i.Result)
            .HasForeignKey<DiagnosticResult>(r => r.DiagnosticOrderItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.DiagnosticOrderItemId).IsUnique();

        builder.Property(r => r.ResultText).HasMaxLength(4000).IsRequired();
        builder.Property(r => r.Conclusion).HasMaxLength(2000);
        builder.Property(r => r.ReferenceRange).HasMaxLength(200);
        builder.Property(r => r.Unit).HasMaxLength(50);

        builder.Property(r => r.ResultedAtUtc).IsRequired();
        builder.Property(r => r.ResultedByUserId).IsRequired();

        builder.Property(r => r.RowVersion).IsRowVersion();
    }
}