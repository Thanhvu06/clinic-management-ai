using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientVisitConfiguration : IEntityTypeConfiguration<PatientVisit>
{
    public void Configure(EntityTypeBuilder<PatientVisit> builder)
    {
        builder.ToTable("PatientVisits");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedOnAdd();

        builder.Property(v => v.VisitCode)
            .IsRequired()
            .HasMaxLength(50);
        builder.HasIndex(v => v.VisitCode)
            .IsUnique();

        builder.HasIndex(v => v.AppointmentId)
            .IsUnique()
            .HasFilter("[AppointmentId] IS NOT NULL");

        builder.HasIndex(v => new { v.VisitDate, v.FacilityId, v.DepartmentId, v.QueueNumber })
            .IsUnique();

        builder.Property(v => v.ArrivalType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(v => v.Priority)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(v => v.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(v => v.ChiefComplaint)
            .HasMaxLength(1000);

        builder.Property(v => v.CancellationReason)
            .HasMaxLength(500);

        builder.Property(v => v.RowVersion)
            .IsRowVersion();

        builder.HasOne(v => v.Patient)
            .WithMany(p => p.PatientVisits)
            .HasForeignKey(v => v.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Appointment)
            .WithOne(a => a.PatientVisit)
            .HasForeignKey<PatientVisit>(v => v.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Facility)
            .WithMany()
            .HasForeignKey(v => v.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Department)
            .WithMany()
            .HasForeignKey(v => v.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Room)
            .WithMany()
            .HasForeignKey(v => v.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.AssignedDoctor)
            .WithMany()
            .HasForeignKey(v => v.AssignedDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.HealthPackageRegistration)
            .WithMany(r => r.PatientVisits)
            .HasForeignKey(v => v.HealthPackageRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => v.HealthPackageRegistrationId)
            .HasFilter("[HealthPackageRegistrationId] IS NOT NULL");

        builder.HasIndex(v => new { v.FacilityId, v.DepartmentId, v.VisitDate, v.Status });
        builder.HasIndex(v => new { v.AssignedDoctorId, v.VisitDate, v.Status });
    }
}
