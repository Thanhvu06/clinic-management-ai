using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence.Seeds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class Icd10CodeConfiguration : IEntityTypeConfiguration<Icd10Code>
{
    public void Configure(EntityTypeBuilder<Icd10Code> builder)
    {
        builder.ToTable("Icd10Codes");
        builder.HasKey(code => code.Code);
        builder.Property(code => code.Code).HasMaxLength(10);
        builder.Property(code => code.NameVi).HasMaxLength(300).IsRequired();
        builder.Property(code => code.SearchKey).HasMaxLength(400).IsRequired();
        builder.Property(code => code.ChapterCode).HasMaxLength(10).IsRequired();
        builder.Property(code => code.IsActive).IsRequired();
        builder.HasData(Icd10CatalogSeed.Codes);
    }
}
