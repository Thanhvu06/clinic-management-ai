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
    public DbSet<RevisitRequest> RevisitRequests { get; set; } = null!;
    public DbSet<AiSuggestionLog> AiSuggestionLogs { get; set; } = null!;
    public DbSet<SystemAuditLog> SystemAuditLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }
}
