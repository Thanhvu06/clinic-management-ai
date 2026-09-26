using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AiSessionConfiguration : IEntityTypeConfiguration<AiSession>
{
    public void Configure(EntityTypeBuilder<AiSession> builder)
    {
        builder.ToTable("AiSessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.SessionId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.ActiveDraftId)
            .HasMaxLength(128);

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();

        builder.Property(s => s.LastActiveAtUtc)
            .IsRequired();

        builder.Property(s => s.ExpiresAtUtc)
            .IsRequired();

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        // A client session is scoped by account. Anonymous sessions use a
        // separate filtered unique index because SQL Server treats nullable
        // values in a composite unique index differently from SQLite.
        builder.HasIndex(s => new { s.SessionId, s.UserId })
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL");
        builder.HasIndex(s => s.SessionId)
            .IsUnique()
            .HasFilter("[UserId] IS NULL");

        builder.HasIndex(s => new { s.UserId, s.IsActive });

        builder.HasIndex(s => s.ExpiresAtUtc);
    }
}
