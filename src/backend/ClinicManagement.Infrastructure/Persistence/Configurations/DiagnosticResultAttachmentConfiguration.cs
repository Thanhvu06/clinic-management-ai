using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DiagnosticResultAttachmentConfiguration : IEntityTypeConfiguration<DiagnosticResultAttachment>
{
    public void Configure(EntityTypeBuilder<DiagnosticResultAttachment> builder)
    {
        builder.ToTable("DiagnosticResultAttachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.StoredFileName).HasMaxLength(64).IsRequired();
        builder.HasIndex(a => a.StoredFileName).IsUnique();
        builder.Property(a => a.OriginalFileName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(50).IsRequired();
        builder.HasIndex(a => a.DiagnosticOrderItemId);
        builder.HasOne(a => a.DiagnosticOrderItem).WithMany(i => i.Attachments)
            .HasForeignKey(a => a.DiagnosticOrderItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
