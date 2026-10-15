using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DiagnosticResultValueConfiguration : IEntityTypeConfiguration<DiagnosticResultValue>
{
    public void Configure(EntityTypeBuilder<DiagnosticResultValue> builder)
    {
        builder.ToTable("DiagnosticResultValues");
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => new { v.DiagnosticResultId, v.DiagnosticServiceParameterId }).IsUnique();
        builder.Property(v => v.ParameterCode).HasMaxLength(30).IsRequired();
        builder.Property(v => v.ParameterName).HasMaxLength(150).IsRequired();
        builder.Property(v => v.Unit).HasMaxLength(30).IsRequired();
        builder.Property(v => v.NumericValue).HasPrecision(18, 4);
        builder.Property(v => v.RefLow).HasPrecision(18, 4);
        builder.Property(v => v.RefHigh).HasPrecision(18, 4);
        builder.Property(v => v.CriticalLow).HasPrecision(18, 4);
        builder.Property(v => v.CriticalHigh).HasPrecision(18, 4);
        builder.Property(v => v.PreviousNumericValue).HasPrecision(18, 4);
        builder.Property(v => v.PreviousOrderCode).HasMaxLength(50);
        builder.Property(v => v.DeltaChange).HasPrecision(18, 4);
        builder.Property(v => v.DeltaPercent).HasPrecision(9, 2);
        builder.Property(v => v.IsDeltaFailure).HasDefaultValue(false);
        builder.HasOne(v => v.DiagnosticResult).WithMany(r => r.Values)
            .HasForeignKey(v => v.DiagnosticResultId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(v => v.DiagnosticServiceParameter).WithMany()
            .HasForeignKey(v => v.DiagnosticServiceParameterId).OnDelete(DeleteBehavior.Restrict);
    }
}
