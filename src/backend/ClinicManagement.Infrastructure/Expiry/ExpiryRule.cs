using System.Data;
using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public abstract class ExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options) : IExpiryRule
{
    protected AppDbContext Db => db;
    protected IDateTimeProvider Clock => clock;
    protected ExpiryRulesOptions Options => options.Value;
    protected abstract Task<int> ApplyBatchAsync(CancellationToken ct);

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            var count = await ApplyBatchAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            total += count;
            if (count == 0) return total;
        }
    }

    protected void Audit(string entity, long id, string code, string reason)
    {
        db.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = null, Action = "AutomaticExpiry", EntityName = entity, EntityId = id.ToString(),
            Description = $"Hệ thống: {reason} — {code}", CreatedAt = clock.UtcNow
        });
    }

    protected void Notify(Guid? userId, NotificationType type, string entity, long id, string code, string title, string route)
    {
        if (userId == null || userId == Guid.Empty) return;
        var key = $"expiry-{entity}-{id}-{userId}";
        if (db.Notifications.Local.Any(n => n.DedupeKey == key)) return;
        db.Notifications.Add(new Notification
        {
            UserId = userId.Value, Type = type, Title = title, Message = $"{title}: {code}.", Route = route,
            RelatedEntityType = entity, RelatedEntityId = id.ToString(), CreatedAtUtc = clock.UtcNow,
            DedupeKey = key
        });
    }

    protected async Task NotifyReceptionAsync(long? facilityId, string entity, long id, string code, string title, CancellationToken ct, string route = "/reception/appointments")
    {
        var users = await (from u in db.Users
                           join ur in db.UserRoles on u.Id equals ur.UserId
                           join role in db.Roles on ur.RoleId equals role.Id
                           where u.IsActive && role.Name == "Receptionist" &&
                               (!facilityId.HasValue || db.StaffFacilityAssignments.Any(a => a.UserId == u.Id &&
                                   a.FacilityId == facilityId && a.IsActive && a.Role == "Receptionist"))
                           select u.Id).Distinct().ToListAsync(ct);
        foreach (var user in users)
            Notify(user, NotificationType.Appointment, entity, id, code, title, route);
    }

    protected async Task ChangeAppointmentAsync(Appointment appointment, AppointmentStatus status, string reason, CancellationToken ct)
    {
        var old = appointment.Status;
        appointment.Status = status;
        db.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id, OldStatus = old, NewStatus = status,
            Action = status == AppointmentStatus.NoShow ? AppointmentHistoryAction.NoShow : AppointmentHistoryAction.Cancelled,
            Note = reason, PerformedByUserId = null, CreatedAt = clock.UtcNow
        });
        var slot = await db.AppointmentSlots.FindAsync([appointment.AppointmentSlotId], ct);
        if (slot != null && !await db.Appointments.AnyAsync(a => a.Id != appointment.Id && a.AppointmentSlotId == slot.Id &&
                AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status), ct)) slot.IsBooked = false;
        Audit(nameof(Appointment), appointment.Id, appointment.AppointmentCode, reason);
        var patient = await db.Patients.FindAsync([appointment.PatientId], ct);
        var doctor = await db.Doctors.FindAsync([appointment.DoctorId], ct);
        Notify(patient?.UserId, NotificationType.Appointment, nameof(Appointment), appointment.Id, appointment.AppointmentCode, reason, "/patient/appointments");
        Notify(doctor?.UserId, NotificationType.Appointment, nameof(Appointment), appointment.Id, appointment.AppointmentCode, reason, "/doctor/appointments");
        await NotifyReceptionAsync(appointment.FacilityId, nameof(Appointment), appointment.Id, appointment.AppointmentCode, reason, ct);
    }
}
