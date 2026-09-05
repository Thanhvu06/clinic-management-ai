using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class VisitSummaryConfiguration : IEntityTypeConfiguration<VisitSummary>
{
    public void Configure(EntityTypeBuilder<VisitSummary> builder)
    {
        builder.ToTable("VisitSummaries");

        builder.HasKey(vs => vs.Id);
        builder.Property(vs => vs.Id).ValueGeneratedOnAdd();

        builder.HasOne(vs => vs.Appointment)
            .WithOne(a => a.VisitSummary)
            .HasForeignKey<VisitSummary>(vs => vs.AppointmentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(vs => vs.Doctor)
            .WithMany(d => d.VisitSummaries)
            .HasForeignKey(vs => vs.DoctorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(vs => vs.ChiefComplaint).HasMaxLength(1000);
        builder.Property(vs => vs.ClinicalFindings).HasMaxLength(4000);
        builder.Property(vs => vs.Diagnosis).HasMaxLength(1000);
        builder.Property(vs => vs.DiagnosisCode).HasMaxLength(50);
        builder.Property(vs => vs.TreatmentPlan).HasMaxLength(4000);
        builder.Property(vs => vs.Summary).IsRequired().HasMaxLength(4000);
        builder.Property(vs => vs.FollowUpInstruction).HasMaxLength(1000);

        builder.Property(vs => vs.CreatedAtUtc).IsRequired();
        builder.Property(vs => vs.RowVersion).IsRowVersion();
    }
}
