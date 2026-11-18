using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class ConfirmedAppointmentExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var cutoff = Clock.VietnamNow.AddMinutes(-Options.AppointmentGraceMinutes);
        var date = DateOnly.FromDateTime(cutoff);
        var time = TimeOnly.FromDateTime(cutoff);
        var items = await Db.Appointments.Where(a => a.Status == AppointmentStatus.Confirmed && a.PatientVisit == null &&
                (a.AppointmentDate < date || a.AppointmentDate == date && a.StartTime <= time))
            .OrderBy(a => a.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var a in items) await ChangeAppointmentAsync(a, AppointmentStatus.NoShow, "Vắng mặt", ct);
        return items.Count;
    }
}
