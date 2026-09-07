using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DiagnosticServiceConfiguration : IEntityTypeConfiguration<DiagnosticService>
{
    public void Configure(EntityTypeBuilder<DiagnosticService> builder)
    {
        builder.ToTable("DiagnosticServices");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();

        builder.Property(s => s.Category)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.PreparationInstructions).HasMaxLength(1000);
        builder.Property(s => s.IsActive).IsRequired().HasDefaultValue(true);
    }
}