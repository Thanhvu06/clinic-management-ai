using System;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Specialty> Specialties { get; set; } = null!;
    public DbSet<Doctor> Doctors { get; set; } = null!;
    public DbSet<DoctorSpecialty> DoctorSpecialties { get; set; } = null!;
    public DbSet<Patient> Patients { get; set; } = null!;
    public DbSet<DoctorWorkSchedule> DoctorWorkSchedules { get; set; } = null!;
    public DbSet<DoctorLeaveRequest> DoctorLeaveRequests { get; set; } = null!;
    public DbSet<AppointmentSlot> AppointmentSlots { get; set; } = null!;
    public DbSet<Appointment> Appointments { get; set; } = null!;
    public DbSet<AppointmentHistory> AppointmentHistories { get; set; } = null!;
    public DbSet<AppointmentChangeRequest> AppointmentChangeRequests { get; set; } = null!;
    public DbSet<VisitSummary> VisitSummaries { get; set; } = null!;
    public DbSet<AppointmentVitalSigns> AppointmentVitalSigns { get; set; } = null!;
    public DbSet<RevisitRequest> RevisitRequests { get; set; } = null!;
    public DbSet<AiSuggestionLog> AiSuggestionLogs { get; set; } = null!;
    public DbSet<SystemAuditLog> SystemAuditLogs { get; set; } = null!;
    
    // Pharmacy
    public DbSet<Medicine> Medicines { get; set; } = null!;
    public DbSet<Prescription> Prescriptions { get; set; } = null!;
    public DbSet<PrescriptionItem> PrescriptionItems { get; set; } = null!;
    public DbSet<MedicineStockTransaction> MedicineStockTransactions { get; set; } = null!;

    // Health Packages
    public DbSet<HealthPackage> HealthPackages { get; set; } = null!;
    public DbSet<HealthPackageRegistration> HealthPackageRegistrations { get; set; } = null!;

    // Clinic Locations
    public DbSet<ClinicLocation> ClinicLocations { get; set; } = null!;

    // Notifications
    public DbSet<Notification> Notifications { get; set; } = null!;

    // Billing
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceItem> InvoiceItems { get; set; } = null!;
    public DbSet<Payment> Payments { get; set; } = null!;

    // Diagnostics
    public DbSet<DiagnosticService> DiagnosticServices { get; set; } = null!;
    public DbSet<DiagnosticOrder> DiagnosticOrders { get; set; } = null!;
    public DbSet<DiagnosticOrderItem> DiagnosticOrderItems { get; set; } = null!;
    public DbSet<DiagnosticResult> DiagnosticResults { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        builder.Entity<PrescriptionItem>()
            .HasKey(pi => new { pi.PrescriptionId, pi.MedicineId });

        builder.Entity<Prescription>(b =>
        {
            b.Property(p => p.RowVersion)
                .IsRowVersion();
        });

        builder.Entity<ClinicLocation>(b =>
        {
            b.HasIndex(l => l.Code).IsUnique();
            b.HasIndex(l => l.IsActive);
        });

        builder.Entity<HealthPackageRegistration>(b =>
        {
            b.HasIndex(r => r.RegistrationCode).IsUnique();
            b.HasIndex(r => r.PatientId);
            b.HasIndex(r => r.Status);
            b.HasOne(r => r.HealthPackage)
                .WithMany()
                .HasForeignKey(r => r.HealthPackageId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(r => r.Patient)
                .WithMany()
                .HasForeignKey(r => r.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
        });
            
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.IsConcurrencyToken && property.ClrType == typeof(byte[]))
                    {
                        property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                    }
                }

                var checkConstraints = System.Linq.Enumerable.ToList(entityType.GetCheckConstraints());
                foreach (var constraint in checkConstraints)
                {
                    if (constraint.Name != null && constraint.Sql != null && (constraint.Sql.Contains("DATEDIFF") || constraint.Sql.Contains("MINUTE")))
                    {
                        entityType.RemoveCheckConstraint(constraint.Name);
                    }
                }
            }
        }
    }

    public override System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                {
                    var rowVersionProp = entry.Properties.FirstOrDefault(p => p.Metadata.IsConcurrencyToken && p.Metadata.ClrType == typeof(byte[]));
                    if (rowVersionProp != null)
                    {
                        rowVersionProp.CurrentValue = Guid.NewGuid().ToByteArray();
                    }
                }
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
