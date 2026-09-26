using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        builder.Property(r => r.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Scope)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.RequestHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.ResponseBody)
            .IsRequired();

        builder.HasIndex(r => new { r.Key, r.Scope })
            .IsUnique();

        builder.HasIndex(r => r.ExpiresAtUtc);
    }
}
