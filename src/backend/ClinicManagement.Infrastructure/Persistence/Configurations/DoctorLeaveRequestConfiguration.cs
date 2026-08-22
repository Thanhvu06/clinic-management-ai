using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DoctorLeaveRequestConfiguration : IEntityTypeConfiguration<DoctorLeaveRequest>
{
    public void Configure(EntityTypeBuilder<DoctorLeaveRequest> builder)
    {
        builder.ToTable("DoctorLeaveRequests", t => 
            t.HasCheckConstraint("CK_DoctorLeaveRequest_DateTimeRange", "[StartDateTime] < [EndDateTime]"));

        builder.HasKey(lr => lr.Id);
        builder.Property(lr => lr.Id).ValueGeneratedOnAdd();

        builder.HasOne(lr => lr.Doctor)
            .WithMany(d => d.LeaveRequests)
            .HasForeignKey(lr => lr.DoctorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(lr => lr.StartDateTime).HasColumnType("datetime2");
        builder.Property(lr => lr.EndDateTime).HasColumnType("datetime2");

        builder.Property(lr => lr.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(lr => lr.Reason).IsRequired();
    }
}
