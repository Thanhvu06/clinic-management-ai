using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class PackageRegistrationExpiryRule(AppDbContext db, IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options)
    : ExpiryRule(db, clock, options)
{
    protected override async Task<int> ApplyBatchAsync(CancellationToken ct)
    {
        var today = Clock.VietnamToday;
        var items = await Db.HealthPackageRegistrations.Include(r => r.Patient)
            .Where(r => r.Status == HealthPackageRegistrationStatus.Pending && r.PreferredDate < today)
            .OrderBy(r => r.Id).Take(Options.BatchSize).ToListAsync(ct);
        foreach (var r in items)
        {
            r.Status = HealthPackageRegistrationStatus.Cancelled;
            r.CancellationReason = "Quá hạn xác nhận";
            r.UpdatedAt = Clock.UtcNow;
            Audit(nameof(HealthPackageRegistration), r.Id, r.RegistrationCode, "Quá hạn xác nhận");
            Notify(r.Patient.UserId, NotificationType.HealthPackage, nameof(HealthPackageRegistration), r.Id, r.RegistrationCode, "Quá hạn xác nhận", "/patient/health-package-registrations");
        }
        return items.Count;
    }
}
