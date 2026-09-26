using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public sealed class AiPendingToolActionConfiguration : IEntityTypeConfiguration<AiPendingToolAction>
{
    public void Configure(EntityTypeBuilder<AiPendingToolAction> builder)
    {
        builder.HasKey(x => x.ActionId);
        builder.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DraftId).HasMaxLength(128);
        builder.Property(x => x.ToolName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ToolVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResourceId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NormalizedArgumentsJson).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.IdempotencyKeyHash).HasMaxLength(128);
        builder.Property(x => x.ExecutionResultReference).HasMaxLength(256);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
        builder.HasIndex(x => new { x.UserId, x.ResourceType, x.ResourceId, x.ToolName });
        builder.HasIndex(x => new { x.UserId, x.ResourceType, x.ResourceId })
            .IsUnique()
            .HasFilter("[ExecutedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL");
    }
}
