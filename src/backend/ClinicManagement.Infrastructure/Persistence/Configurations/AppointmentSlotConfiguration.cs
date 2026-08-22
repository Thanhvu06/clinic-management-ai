using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AppointmentSlotConfiguration : IEntityTypeConfiguration<AppointmentSlot>
{
    public void Configure(EntityTypeBuilder<AppointmentSlot> builder)
    {
        builder.ToTable("AppointmentSlots", t => 
        {
            t.HasCheckConstraint("CK_AppointmentSlot_TimeRange", "[StartTime] < [EndTime]");
            t.HasCheckConstraint("CK_AppointmentSlot_Duration", "DATEDIFF(MINUTE, [StartTime], [EndTime]) = 30");
        });

        builder.HasKey(slot => slot.Id);
        builder.Property(slot => slot.Id).ValueGeneratedOnAdd();

        builder.HasOne(slot => slot.Doctor)
            .WithMany(d => d.AppointmentSlots)
            .HasForeignKey(slot => slot.DoctorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(slot => slot.SlotDate).HasColumnType("date");
        builder.Property(slot => slot.StartTime).HasColumnType("time");
        builder.Property(slot => slot.EndTime).HasColumnType("time");

        builder.Property(slot => slot.IsBooked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(slot => new { slot.DoctorId, slot.SlotDate, slot.StartTime })
            .IsUnique();
    }
}
