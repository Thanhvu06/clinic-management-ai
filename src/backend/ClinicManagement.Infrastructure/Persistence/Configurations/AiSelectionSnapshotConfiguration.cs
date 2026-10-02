using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AiSelectionSnapshotConfiguration : IEntityTypeConfiguration<AiSelectionSnapshot>
{
    public void Configure(EntityTypeBuilder<AiSelectionSnapshot> builder)
    {
        builder.ToTable("AiSelectionSnapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.SnapshotId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.SessionId)
            .HasMaxLength(128);

        builder.Property(s => s.DraftId)
            .HasMaxLength(128);

        builder.Property(s => s.SlotDate)
            .HasMaxLength(32);

        builder.Property(s => s.DoctorIdsJson)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(s => s.SlotIdsJson)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();

        builder.Property(s => s.ExpiresAtUtc)
            .IsRequired();

        builder.HasIndex(s => s.SnapshotId)
            .IsUnique();

        builder.HasIndex(s => s.ExpiresAtUtc);

        builder.HasIndex(s => new { s.UserId, s.SessionId, s.DraftId });
    }
}
