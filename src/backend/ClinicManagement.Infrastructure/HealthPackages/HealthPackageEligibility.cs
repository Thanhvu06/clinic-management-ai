using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.HealthPackages;

internal static class HealthPackageEligibility
{
    internal static bool AllowsAge(HealthPackage package, DateOnly? birthDate, DateOnly date)
    {
        if (birthDate is not { } dob || dob > date) return false;
        var child = dob > date.AddYears(-16);
        return package.Code == "PKG03" ? child : !child;
    }

    internal static bool Allows(HealthPackage package, Patient patient, DateOnly date) =>
        Allows(package, patient.Gender) && AllowsAge(package, patient.DateOfBirth, date);

    internal static bool Allows(HealthPackage package, Gender? gender)
    {
        var audience = package.TargetAudience.ToLowerInvariant();
        if (audience.Contains("nữ giới") || audience.Contains("phụ nữ")) return gender == Gender.Female;
        if (audience.Contains("nam giới")) return gender == Gender.Male;
        return true;
    }
}
