using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class ChangeRequestExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var now = Clock.VietnamNow;
        var date = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        var items = await Db.AppointmentChangeRequests.Include(r => r.Appointment).ThenInclude(a => a.Patient)
            .Where(r => r.Status == AppointmentChangeRequestStatus.Pending &&
                (r.Appointment.AppointmentDate < date || r.Appointment.AppointmentDate == date && r.Appointment.StartTime <= time))
            .OrderBy(r => r.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var r in items)
        {
            r.Status = AppointmentChangeRequestStatus.Expired;
            r.ProcessedAt = Clock.UtcNow;
            r.ProcessedByUserId = null;
            var a = r.Appointment;
            var code = $"YC{r.Id} / {a.AppointmentCode}";
            Audit(nameof(AppointmentChangeRequest), r.Id, code, "Hết hạn xử lý");
            if (a.Status == AppointmentStatus.PendingReschedule || a.Status == AppointmentStatus.PendingCancellation)
            {
                var old = a.Status;
                a.Status = r.OriginalAppointmentStatus is AppointmentStatus.Pending or AppointmentStatus.Confirmed
                    ? r.OriginalAppointmentStatus.Value : AppointmentStatus.Confirmed;
                Db.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = a.Id, Action = AppointmentHistoryAction.RequestExpired,
                    OldStatus = old, NewStatus = a.Status, Note = "Hết hạn xử lý", PerformedByUserId = null, CreatedAt = Clock.UtcNow
                });
                Audit(nameof(Appointment), a.Id, a.AppointmentCode, "Hết hạn xử lý, khôi phục trạng thái lịch gốc");
            }
            Notify(a.Patient.UserId, NotificationType.AppointmentChangeRequest, nameof(AppointmentChangeRequest), r.Id, code, "Hết hạn xử lý", "/patient/appointments");
            await NotifyReceptionAsync(a.FacilityId, nameof(AppointmentChangeRequest), r.Id, code, "Hết hạn xử lý", ct, "/reception/change-requests");
        }
        return items.Count;
    }
}
