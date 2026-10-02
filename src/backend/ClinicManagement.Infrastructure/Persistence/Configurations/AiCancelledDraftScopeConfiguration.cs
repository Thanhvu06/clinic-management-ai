using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AiCancelledDraftScopeConfiguration : IEntityTypeConfiguration<AiCancelledDraftScope>
{
    public void Configure(EntityTypeBuilder<AiCancelledDraftScope> builder)
    {
        builder.ToTable("AiCancelledDraftScopes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        builder.Property(c => c.SessionId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.DraftId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.CancelledAtUtc)
            .IsRequired();

        builder.Property(c => c.ExpiresAtUtc)
            .IsRequired();

        builder.HasIndex(c => new { c.UserId, c.SessionId, c.DraftId })
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL");

        builder.HasIndex(c => c.ExpiresAtUtc);
    }
}
