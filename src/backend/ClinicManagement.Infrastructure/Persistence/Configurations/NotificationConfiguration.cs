using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedOnAdd();

        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.Route).HasMaxLength(250);
        builder.Property(n => n.RelatedEntityType).HasMaxLength(100);
        builder.Property(n => n.RelatedEntityId).HasMaxLength(100);
        builder.Property(n => n.DedupeKey).HasMaxLength(150);

        builder.Property(n => n.IsRead).IsRequired().HasDefaultValue(false);
        builder.Property(n => n.CreatedAtUtc).IsRequired();

        builder.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAtUtc });

        builder.HasIndex(n => n.DedupeKey)
            .IsUnique()
            .HasFilter("[DedupeKey] IS NOT NULL");
    }
}
