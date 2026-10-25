using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class DoctorLeaveExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var today = Clock.VietnamToday.ToDateTime(TimeOnly.MinValue);
        var items = await Db.DoctorLeaveRequests.Include(r => r.Doctor)
            .Where(r => r.Status == DoctorLeaveRequestStatus.Pending && r.StartDateTime < today)
            .OrderBy(r => r.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var r in items)
        {
            r.Status = DoctorLeaveRequestStatus.Expired;
            r.AdminNote = "Hết hạn";
            var code = $"NP{r.Id}";
            Audit(nameof(DoctorLeaveRequest), r.Id, code, "Hết hạn");
            Notify(r.Doctor.UserId, NotificationType.LeaveRequest, nameof(DoctorLeaveRequest), r.Id, code, "Đơn nghỉ phép hết hạn", "/doctor/leave-requests");
        }
        return items.Count;
    }
}
