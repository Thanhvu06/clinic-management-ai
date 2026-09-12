using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DiagnosticOrderConfiguration : IEntityTypeConfiguration<DiagnosticOrder>
{
    public void Configure(EntityTypeBuilder<DiagnosticOrder> builder)
    {
        builder.ToTable("DiagnosticOrders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd();

        builder.Property(o => o.OrderCode).HasMaxLength(50).IsRequired();
        builder.HasIndex(o => o.OrderCode).IsUnique();

        builder.Property(o => o.ClinicalIndication).HasMaxLength(1000).IsRequired();
        builder.Property(o => o.Note).HasMaxLength(1000);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(o => o.OrderedAtUtc).IsRequired();

        builder.HasOne(o => o.Appointment)
            .WithMany()
            .HasForeignKey(o => o.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Patient)
            .WithMany()
            .HasForeignKey(o => o.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.OrderingDoctor)
            .WithMany()
            .HasForeignKey(o => o.OrderingDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.ReviewedByDoctor)
            .WithMany()
            .HasForeignKey(o => o.ReviewedByDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => new { o.PatientId, o.OrderedAtUtc });
        builder.HasIndex(o => new { o.OrderingDoctorId, o.Status });
        builder.HasIndex(o => new { o.Status, o.OrderedAtUtc });

        builder.Property(o => o.RowVersion).IsRowVersion();
    }
}