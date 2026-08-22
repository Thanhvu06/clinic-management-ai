using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AdminTests : IntegrationTestBase
{
    public AdminTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_NonAdminUser_When_AccessAdminApi_Then_Forbidden()
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
