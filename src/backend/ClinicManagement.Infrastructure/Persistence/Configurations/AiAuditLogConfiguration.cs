using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AiAuditLogConfiguration : IEntityTypeConfiguration<AiAuditLog>
{
    public void Configure(EntityTypeBuilder<AiAuditLog> builder)
    {
        builder.ToTable("AiAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.ActionType)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(a => a.Outcome)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(a => a.ErrorCode)
            .HasMaxLength(64);

        builder.Property(a => a.SessionId)
            .HasMaxLength(128);

        builder.Property(a => a.DraftId)
            .HasMaxLength(128);

        builder.Property(a => a.CorrelationId)
            .HasMaxLength(128);

        builder.Property(a => a.MetadataJson)
            .HasMaxLength(2000);

        builder.Property(a => a.TimestampUtc)
            .IsRequired();

        builder.HasIndex(a => new { a.UserId, a.TimestampUtc });
        builder.HasIndex(a => new { a.SessionId, a.TimestampUtc });
        builder.HasIndex(a => a.ActionType);
    }
}
