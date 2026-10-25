using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class PendingAppointmentExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var now = Clock.VietnamNow;
        var date = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        var items = await Db.Appointments.Where(a => a.Status == AppointmentStatus.Pending &&
                (a.AppointmentDate < date || a.AppointmentDate == date && a.StartTime <= time) && a.PatientVisit == null)
            .OrderBy(a => a.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var a in items) await ChangeAppointmentAsync(a, AppointmentStatus.Cancelled, "Quá hạn xác nhận", ct);
        return items.Count;
    }
}
