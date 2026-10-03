using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicManagement.IntegrationTests;

public sealed class HybridIntentCalibrationApiTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData(true, .8, "patient.get_my_bills")]
    [InlineData(true, .799, "patient.get_my_appointments")]
    [InlineData(false, .99, "patient.get_my_appointments")]
    public async Task ConfiguredOverrideUsesExistingAuthenticatedReadGateway(bool enabled, double score, string expectedTool)
    {
        var model = HybridIntentRouterTests.Fake("MyBills", score);
        using var app = Factory.WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["RoleIntentModel:Enabled"] = "true", ["RoleIntentModel:Threshold"] = "0.75",
                ["RoleIntentModel:OverrideEnabled"] = enabled.ToString(), ["RoleIntentModel:OverrideThreshold"] = "0.8" }));
            builder.ConfigureServices(services => { services.RemoveAll<IRoleIntentModel>(); services.AddSingleton(model.Object); });
        });
        using var authenticated = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "Xem lịch hẹn của tôi", role = "Admin" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(AiActorRole.Patient.ToString(), data.GetProperty("role").GetString());
        Assert.Equal("NotCalled", data.GetProperty("providerState").GetString());
        Assert.Equal(expectedTool, Assert.Single(data.GetProperty("executedToolNames").EnumerateArray()).GetString());
    }
}
