using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.HealthPackages;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DemoRevenueDevelopmentSeed
{
    internal static Patient? FindPatient(IReadOnlyList<Patient> patients, HealthPackage package, DateOnly date,
        IReadOnlyList<HealthPackageRegistration> purchases, int offset) => patients
        .Where(p => HealthPackageEligibility.Allows(package, p.Gender)
            && (package.Code != "PKG03" || DemoPatientDevelopmentSeed.Allows(p, "SP02", date))
            && !purchases.Any(r => r.PatientId == p.Id && r.HealthPackageId == package.Id
                && r.Status != HealthPackageRegistrationStatus.Cancelled && Math.Abs(r.PreferredDate.DayNumber - date.DayNumber) < 30))
        .OrderBy(p => purchases.Count(r => r.PatientId == p.Id))
        .ThenBy(p => (p.Id + offset) % patients.Count).FirstOrDefault();

    internal static Patient PickPatient(IReadOnlyList<Patient> patients, HealthPackage package, DateOnly date,
        IReadOnlyList<HealthPackageRegistration> purchases, int offset) => FindPatient(patients, package, date, purchases, offset)
        ?? throw new InvalidOperationException("Không còn bệnh nhân phù hợp để mua gói trong 30 ngày.");
}
