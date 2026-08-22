using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class SystemAuditLogConfiguration : IEntityTypeConfiguration<SystemAuditLog>
{
    public void Configure(EntityTypeBuilder<SystemAuditLog> builder)
    {
        builder.ToTable("SystemAuditLogs");

        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).ValueGeneratedOnAdd();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(log => log.Action).IsRequired();
        builder.Property(log => log.EntityName).IsRequired();
        builder.Property(log => log.EntityId).IsRequired();
        builder.Property(log => log.Description).IsRequired();

        builder.Property(log => log.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2");
    }
}
