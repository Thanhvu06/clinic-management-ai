using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class ConfirmedAppointmentExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options,
    IHostEnvironment? environment)
    : ExpiryRule(db, clock, options)
{
    public ConfirmedAppointmentExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
        : this(db, clock, options, null) { }

    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var cutoff = Clock.VietnamNow.AddMinutes(-Options.AppointmentGraceMinutes);
        var date = DateOnly.FromDateTime(cutoff);
        var time = TimeOnly.FromDateTime(cutoff);
        var items = await Db.Appointments.Include(a => a.Patient).Where(a => a.Status == AppointmentStatus.Confirmed && a.PatientVisit == null &&
                (a.AppointmentDate < date || a.AppointmentDate == date && a.StartTime <= time))
            .OrderBy(a => a.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var a in items)
        {
            var demo = environment?.IsDevelopment() == true && a.Patient.Email == "patient@cliniccare.local";
            await ChangeAppointmentAsync(a, demo ? AppointmentStatus.Cancelled : AppointmentStatus.NoShow,
                demo ? DevelopmentPatientBookingSeed.CancellationReason : "Vắng mặt", ct);
        }
        return items.Count;
    }
}
