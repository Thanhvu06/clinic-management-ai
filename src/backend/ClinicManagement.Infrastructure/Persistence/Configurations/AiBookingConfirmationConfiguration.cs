using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AiBookingConfirmationConfiguration : IEntityTypeConfiguration<AiBookingConfirmation>
{
    public void Configure(EntityTypeBuilder<AiBookingConfirmation> builder)
    {
        builder.ToTable("AiBookingConfirmations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConfirmationId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SessionId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.DraftId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ContextSnapshotId).HasMaxLength(64);
        builder.Property(x => x.ReasonHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.UsedIdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();

        builder.HasIndex(x => x.ConfirmationId).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.SessionId, x.DraftId, x.RevokedAtUtc });
        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasIndex(x => new { x.UserId, x.SessionId, x.DraftId })
            .IsUnique()
            .HasFilter("[UsedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL");
    }
}
