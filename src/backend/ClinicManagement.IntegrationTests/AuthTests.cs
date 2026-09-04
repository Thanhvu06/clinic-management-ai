using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AuthTests : IntegrationTestBase
{
    public AuthTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Theory]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/pharmacy/dashboard")]
    [InlineData("/api/v1/doctor/appointments")]
    [InlineData("/api/v1/appointments/my")]
    [InlineData("/api/v1/reception/stats")]
    public async Task Given_ProtectedEndpoints_When_NoToken_Then_Returns401(string url)
    {
        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/pharmacy/dashboard")]
    [InlineData("/api/v1/doctor/appointments")]
    [InlineData("/api/v1/reception/stats")]
    public async Task Given_PatientToken_When_AccessingRestrictedEndpoints_Then_Returns403(string url)
    {
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/pharmacy/dashboard")]
    [InlineData("/api/v1/appointments/my")]
    public async Task Given_DoctorToken_When_AccessingNonDoctorEndpoints_Then_Returns403(string url)
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/doctor/appointments")]
    [InlineData("/api/v1/appointments/my")]
    public async Task Given_PharmacistToken_When_AccessingNonPharmacyEndpoints_Then_Returns403(string url)
    {
        await AuthenticateAsync("pharm@test.com");
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
