using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments", t => 
        {
            t.HasCheckConstraint("CK_Appointment_TimeRange", "[StartTime] < [EndTime]");
            t.HasCheckConstraint("CK_Appointment_Duration", "DATEDIFF(MINUTE, [StartTime], [EndTime]) = 30");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.AppointmentCode).IsRequired();
        builder.HasIndex(a => a.AppointmentCode).IsUnique();

        builder.HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.Doctor)
            .WithMany(d => d.Appointments)
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.Specialty)
            .WithMany(s => s.Appointments)
            .HasForeignKey(a => a.SpecialtyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.AppointmentSlot)
            .WithMany(slot => slot.Appointments)
            .HasForeignKey(a => a.AppointmentSlotId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(a => a.AppointmentDate).HasColumnType("date");
        builder.Property(a => a.StartTime).HasColumnType("time");
        builder.Property(a => a.EndTime).HasColumnType("time");

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(AppointmentStatus.Pending);

        builder.Property(a => a.Reason).HasMaxLength(500);
    }
}
