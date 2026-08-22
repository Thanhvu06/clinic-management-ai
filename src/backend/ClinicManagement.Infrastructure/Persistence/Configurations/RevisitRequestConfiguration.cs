using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class RevisitRequestConfiguration : IEntityTypeConfiguration<RevisitRequest>
{
    public void Configure(EntityTypeBuilder<RevisitRequest> builder)
    {
        builder.ToTable("RevisitRequests");

        builder.HasKey(rr => rr.Id);
        builder.Property(rr => rr.Id).ValueGeneratedOnAdd();

        builder.HasOne(rr => rr.OriginalAppointment)
            .WithOne(a => a.RevisitRequest)
            .HasForeignKey<RevisitRequest>(rr => rr.AppointmentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(rr => rr.NewAppointment)
            .WithOne(a => a.SourceRevisitRequest)
            .HasForeignKey<RevisitRequest>(rr => rr.NewAppointmentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(rr => rr.Patient)
            .WithMany(p => p.RevisitRequests)
            .HasForeignKey(rr => rr.PatientId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(rr => rr.Doctor)
            .WithMany(d => d.RevisitRequests)
            .HasForeignKey(rr => rr.DoctorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(rr => rr.SuggestedDate)
            .HasColumnType("date");

        builder.Property(rr => rr.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(RevisitRequestStatus.PendingPatientResponse);
    }
}
