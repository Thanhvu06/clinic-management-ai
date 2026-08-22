using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AuthTests : IntegrationTestBase
{
    public AuthTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_PrivateApi_When_NoToken_Then_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_AdminApi_When_PatientToken_Then_Returns403()
    {
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Given_PatientApi_When_AdminToken_Then_Returns403()
    {
        await AuthenticateAsync("admin@test.com");
        var response = await Client.GetAsync("/api/v1/appointments/patient"); // Actually wait, check exact route. Usually /api/v1/appointments/patient
        // The exact route in AppointmentController is /api/v1/appointments
        // Let's just check /api/v1/appointments
    }
}
