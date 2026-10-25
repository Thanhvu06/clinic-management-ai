using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class PrescriptionExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    public static bool IsExpired(Prescription p, IDateTimeProvider clock, ExpiryRulesOptions options) =>
        p.Status == PrescriptionStatus.Expired || p.Status == PrescriptionStatus.Issued && p.CreatedAt <= clock.UtcNow.AddDays(-options.PrescriptionDays);

    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var cutoff = Clock.UtcNow.AddDays(-Options.PrescriptionDays);
        var items = await Db.Prescriptions.Include(p => p.Patient).Where(p => p.Status == PrescriptionStatus.Issued && p.CreatedAt <= cutoff)
            .OrderBy(p => p.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var p in items)
        {
            p.Status = PrescriptionStatus.Expired;
            var code = BusinessDisplay.PrescriptionCode(p, Clock);
            Audit(nameof(Prescription), p.Id, code, "Hết hạn lĩnh thuốc");
            Notify(p.Patient?.UserId, NotificationType.Prescription, nameof(Prescription), p.Id, code, "Hết hạn lĩnh thuốc, cần kê lại", "/patient/prescriptions");
        }
        return items.Count;
    }
}
