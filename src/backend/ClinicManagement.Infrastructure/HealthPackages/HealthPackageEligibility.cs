using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.HealthPackages;

internal static class HealthPackageEligibility
{
    internal static bool Allows(HealthPackage package, Gender? gender)
    {
        var audience = package.TargetAudience.ToLowerInvariant();
        if (audience.Contains("nữ giới") || audience.Contains("phụ nữ")) return gender == Gender.Female;
        if (audience.Contains("nam giới")) return gender == Gender.Male;
        return true;
    }
}
