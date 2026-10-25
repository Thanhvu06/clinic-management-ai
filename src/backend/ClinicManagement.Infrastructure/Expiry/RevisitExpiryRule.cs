using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class RevisitExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var cutoff = Clock.VietnamToday.AddDays(-Options.RevisitDays);
        var items = await Db.RevisitRequests.Include(r => r.Patient).Include(r => r.Doctor).Include(r => r.OriginalAppointment)
            .Where(r => r.Status == RevisitRequestStatus.PendingPatientResponse && r.SuggestedDate <= cutoff)
            .OrderBy(r => r.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var r in items)
        {
            r.Status = RevisitRequestStatus.Expired;
            var code = $"TK{r.Id} / {r.OriginalAppointment.AppointmentCode}";
            Audit(nameof(RevisitRequest), r.Id, code, "Hết hạn");
            Notify(r.Patient.UserId, NotificationType.Appointment, nameof(RevisitRequest), r.Id, code, "Đề xuất tái khám hết hạn", "/patient/revisit");
            Notify(r.Doctor.UserId, NotificationType.Appointment, nameof(RevisitRequest), r.Id, code, "Đề xuất tái khám hết hạn", "/doctor/appointments");
        }
        return items.Count;
    }
}
