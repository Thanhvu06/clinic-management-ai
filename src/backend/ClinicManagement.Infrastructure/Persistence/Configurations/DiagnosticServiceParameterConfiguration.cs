using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DiagnosticServiceParameterConfiguration : IEntityTypeConfiguration<DiagnosticServiceParameter>
{
    public void Configure(EntityTypeBuilder<DiagnosticServiceParameter> builder)
    {
        builder.ToTable("DiagnosticServiceParameters");
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.DiagnosticServiceId, p.Code }).IsUnique();
        builder.Property(p => p.Code).HasMaxLength(30).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Unit).HasMaxLength(30).IsRequired();
        builder.Property(p => p.RefLow).HasPrecision(18, 4);
        builder.Property(p => p.RefHigh).HasPrecision(18, 4);
        builder.Property(p => p.RefLowFemale).HasPrecision(18, 4);
        builder.Property(p => p.RefHighFemale).HasPrecision(18, 4);
        builder.Property(p => p.CriticalLow).HasPrecision(18, 4);
        builder.Property(p => p.CriticalHigh).HasPrecision(18, 4);
        builder.Property(p => p.DeltaAbsoluteLimit).HasPrecision(18, 4);
        builder.Property(p => p.DeltaPercentLimit).HasPrecision(9, 2);
        builder.HasOne(p => p.DiagnosticService).WithMany(s => s.Parameters)
            .HasForeignKey(p => p.DiagnosticServiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
