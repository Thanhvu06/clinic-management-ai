using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StaffFacilityAssignmentConfiguration : IEntityTypeConfiguration<StaffFacilityAssignment>
{
    public void Configure(EntityTypeBuilder<StaffFacilityAssignment> builder)
    {
        builder.ToTable("StaffFacilityAssignments");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.FacilityId).IsRequired();
        builder.Property(s => s.Role).HasMaxLength(100).IsRequired();

        builder.HasIndex(s => new { s.UserId, s.FacilityId, s.Role })
            .IsUnique();

        builder.HasOne(s => s.Facility)
            .WithMany()
            .HasForeignKey(s => s.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Department)
            .WithMany()
            .HasForeignKey(s => s.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
