using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DoctorWorkScheduleConfiguration : IEntityTypeConfiguration<DoctorWorkSchedule>
{
    public void Configure(EntityTypeBuilder<DoctorWorkSchedule> builder)
    {
        builder.ToTable("DoctorWorkSchedules", t => 
            t.HasCheckConstraint("CK_DoctorWorkSchedule_TimeRange", "[StartTime] < [EndTime]"));

        builder.HasKey(ws => ws.Id);
        builder.Property(ws => ws.Id).ValueGeneratedOnAdd();

        builder.HasOne(ws => ws.Doctor)
            .WithMany(d => d.WorkSchedules)
            .HasForeignKey(ws => ws.DoctorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(ws => ws.WorkDate).HasColumnType("date");
        builder.Property(ws => ws.StartTime).HasColumnType("time");
        builder.Property(ws => ws.EndTime).HasColumnType("time");
        
        builder.Property(ws => ws.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
    }
}
