using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class HybridIntentApiTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("pat1@test.com", AiActorRole.Patient, "MyBills", "ViewAppointments")]
    [InlineData("rec@test.com", AiActorRole.Receptionist, "TodayAppointments", "ViewAppointments")]
    [InlineData("doc@test.com", AiActorRole.Doctor, "DoctorQueue", "QueueLookup")]
    [InlineData("tech@test.com", AiActorRole.DiagnosticTechnician, "TechnicianWorklist", "DiagnosticLookup")]
    [InlineData("pharm@test.com", AiActorRole.Pharmacist, "PrescriptionQueue", "PrescriptionLookup")]
    [InlineData("admin@test.com", AiActorRole.Admin, "DashboardMetrics", "AdminMetrics")]
    public async Task ModelReadRouteUsesAuthenticatedRoleAndExistingGateway(string email, AiActorRole role, string label, string intent)
    {
        var model = HybridIntentRouterTests.Fake(label, .99);
        using var app = Factory.WithWebHostBuilder(builder => { EnableModel(builder); builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRoleIntentModel>();
            services.AddSingleton(model.Object);
        }); });
        using var authenticated = await CreateAuthenticatedClientAsync(email);
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "ý chưa xác định", role = "Admin" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(role.ToString(), data.GetProperty("role").GetString());
        Assert.Equal(intent, data.GetProperty("intent").GetString());
        Assert.Equal("NotCalled", data.GetProperty("providerState").GetString());
        Assert.NotEmpty(data.GetProperty("cards").EnumerateArray());
        model.Verify(x => x.Predict(It.IsAny<string>(), role), Times.Once);
    }

    [Fact]
    public async Task BookingModelOnlyOffersWizardAndActionModelDoesNotWrite()
    {
        var model = HybridIntentRouterTests.Fake("StartBooking", .99);
        Factory.MockAiProvider.Setup(x => x.PlanRoleCopilotAsync(It.IsAny<ClinicManagement.Application.AI.Planning.AiRolePlannerProviderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RolePlannerResults.Failure("ProviderDisabled", "Disabled"));
        using var app = Factory.WithWebHostBuilder(builder => { EnableModel(builder); builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRoleIntentModel>();
            services.AddSingleton(model.Object);
        }); });
        using var authenticated = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = (await db.Appointments.CountAsync(), await db.Prescriptions.CountAsync(), await db.AiPendingToolActions.CountAsync());
        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "ý chưa xác định" });
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = body.RootElement.GetProperty("data");
            Assert.Equal("StartBooking", data.GetProperty("intent").GetString());
            Assert.Empty(data.GetProperty("cards").EnumerateArray());
            Assert.Contains(data.GetProperty("suggestions").EnumerateArray(), x => x.GetProperty("code").GetString() == "patient.start_booking");
            model.Setup(x => x.Predict(It.IsAny<string>(), It.IsAny<AiActorRole>())).Returns(new RoleIntentModelPrediction("ActionRequest", .99, new Dictionary<string, double> { ["ActionRequest"] = .99 }));
            var action = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "ý chưa xác định" });
            Assert.True(action.IsSuccessStatusCode, await action.Content.ReadAsStringAsync());
            var after = (await db.Appointments.CountAsync(), await db.Prescriptions.CountAsync(), await db.AiPendingToolActions.CountAsync());
            Assert.Equal(before, after);
        }
        finally { Factory.MockAiProvider.Reset(); }
    }

    [Fact]
    public async Task RealFrozenModelIsAvailableFromApiBuildOutput()
    {
        using var app = Factory.WithWebHostBuilder(EnableModel);
        using var scope = app.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<RoleIntentModel>();
        Assert.True(model.IsAvailable);
        Assert.Equal(.75, model.Threshold);
        using var authenticated = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "xin chào" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Greeting", body.RootElement.GetProperty("data").GetProperty("intent").GetString());
    }

    private static void EnableModel(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["RoleIntentModel:Enabled"] = "true" }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptArtifactKeepsAppAndOldGreetingAvailable(bool corrupt)
    {
        var directory = Path.Combine(Path.GetTempPath(), "clinic-api-missing-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (corrupt) File.WriteAllText(Path.Combine(directory, "role_intent_model_v2.zip"), "wrong checksum");
            using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["RoleIntentModel:Enabled"] = "true", ["RoleIntentModel:Directory"] = directory })));
            using var authenticated = await CreateAuthenticatedClientAsync("pat1@test.com");
            using var client = app.CreateClient();
            client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
            Assert.False(app.Services.GetRequiredService<RoleIntentModel>().IsAvailable);
            var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "xin chào" });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Greeting", body.RootElement.GetProperty("data").GetProperty("intent").GetString());
        }
        finally
        {
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe temporary path.");
            Directory.Delete(directory, true);
        }
    }
}
