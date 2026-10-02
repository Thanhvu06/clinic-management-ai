using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DailyQueueSequenceConfiguration : IEntityTypeConfiguration<DailyQueueSequence>
{
    public void Configure(EntityTypeBuilder<DailyQueueSequence> builder)
    {
        builder.ToTable("DailyQueueSequences");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.HasIndex(s => new { s.FacilityId, s.DepartmentId, s.Date })
            .IsUnique();
    }
}
