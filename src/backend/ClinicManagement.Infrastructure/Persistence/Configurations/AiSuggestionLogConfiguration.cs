using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AiSuggestionLogConfiguration : IEntityTypeConfiguration<AiSuggestionLog>
{
    public void Configure(EntityTypeBuilder<AiSuggestionLog> builder)
    {
        builder.ToTable("AiSuggestionLogs");

        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).ValueGeneratedOnAdd();

        builder.HasOne(log => log.Patient)
            .WithMany(p => p.AiSuggestionLogs)
            .HasForeignKey(log => log.PatientId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(log => log.SelectedSpecialty)
            .WithMany()
            .HasForeignKey(log => log.SelectedSpecialtyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(log => log.InputText).IsRequired();
        builder.Property(log => log.SuggestedSpecialtiesJson).IsRequired();
        builder.Property(log => log.Provider).IsRequired();
        builder.Property(log => log.PromptVersion).IsRequired();
        
        builder.Property(log => log.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2");
    }
}
