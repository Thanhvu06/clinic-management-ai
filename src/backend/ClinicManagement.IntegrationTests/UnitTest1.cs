using ClinicManagement.Application.Common.Constants;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class SystemConstantsTests
{
    [Fact]
    public void RoleNames_ShouldContainAllRequiredRoles()
    {
        // Assert all 5 core system roles exist
        Assert.Equal("Patient", RoleNames.Patient);
        Assert.Equal("Receptionist", RoleNames.Receptionist);
        Assert.Equal("Doctor", RoleNames.Doctor);
        Assert.Equal("Admin", RoleNames.Admin);
        Assert.Equal("Pharmacist", RoleNames.Pharmacist);
    }
}
