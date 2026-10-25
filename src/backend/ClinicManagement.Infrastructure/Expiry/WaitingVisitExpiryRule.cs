using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class WaitingVisitExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var today = Clock.VietnamToday;
        var todayStartUtc = Clock.ConvertVietnamToUtc(today.ToDateTime(TimeOnly.MinValue));
        var items = await Db.PatientVisits.Include(v => v.Patient).Include(v => v.AssignedDoctor).Include(v => v.Appointment)
            .Where(v => v.VisitDate < today && v.CheckedInAtUtc < todayStartUtc &&
                (v.Status == VisitStatus.Registered || v.Status == VisitStatus.CheckedIn || v.Status == VisitStatus.WaitingForDoctor))
            .OrderBy(v => v.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var v in items)
        {
            v.Status = VisitStatus.NoShow;
            v.CancellationReason = "Bỏ về";
            v.UpdatedAtUtc = Clock.UtcNow;
            Audit(nameof(PatientVisit), v.Id, v.VisitCode, "Vắng mặt / Bỏ về");
            Notify(v.Patient.UserId, NotificationType.Appointment, nameof(PatientVisit), v.Id, v.VisitCode, "Bỏ về", "/patient/appointments");
            Notify(v.AssignedDoctor?.UserId, NotificationType.Appointment, nameof(PatientVisit), v.Id, v.VisitCode, "Bỏ về", "/doctor/queue");
            await NotifyReceptionAsync(v.FacilityId, nameof(PatientVisit), v.Id, v.VisitCode, "Bỏ về", ct);
            if (v.Appointment is { } a && (a.Status == AppointmentStatus.CheckedIn || a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending))
                await ChangeAppointmentAsync(a, AppointmentStatus.NoShow, "Bỏ về", ct);
        }
        return items.Count;
    }
}
