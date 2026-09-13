using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class MrnSequenceConfiguration : IEntityTypeConfiguration<MrnSequence>
{
    public void Configure(EntityTypeBuilder<MrnSequence> builder)
    {
        builder.ToTable("MrnSequences");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        builder.HasIndex(m => m.Year).IsUnique();

        builder.Property(m => m.RowVersion)
            .IsRowVersion();
    }
}
