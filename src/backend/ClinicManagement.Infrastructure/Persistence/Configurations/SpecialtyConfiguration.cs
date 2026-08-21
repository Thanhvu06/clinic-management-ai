using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class SpecialtyConfiguration : IEntityTypeConfiguration<Specialty>
{
    public void Configure(EntityTypeBuilder<Specialty> builder)
    {
        builder.ToTable("Specialties");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.SpecialtyCode).IsRequired();
        builder.HasIndex(s => s.SpecialtyCode).IsUnique();

        builder.Property(s => s.Name).IsRequired();
        
        builder.Property(s => s.IsActive).IsRequired();
        
        builder.Property(s => s.AiEnabled).IsRequired().HasDefaultValue(false);
    }
}
