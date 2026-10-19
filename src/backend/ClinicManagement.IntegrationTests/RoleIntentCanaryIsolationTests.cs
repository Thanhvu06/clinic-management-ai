using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.AI.LiveCanary;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class RoleIntentCanaryIsolationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FactoryCanDisableRoleIntentWithoutChangingItsDefault(bool enabled)
    {
        using var factory = enabled
            ? new SyntheticCanaryFactory(new CanaryProviderAttemptBudget(12))
            : new SyntheticCanaryFactory(new CanaryProviderAttemptBudget(12), roleIntentEnabled: false);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(enabled, scope.ServiceProvider.GetRequiredService<IOptions<RoleIntentModelOptions>>().Value.Enabled);
        Assert.Equal(enabled, scope.ServiceProvider.GetRequiredService<RoleIntentModel>().IsAvailable);
    }

    [Fact]
    public async Task V3RoutesFutureAppointmentsToAppointmentsWithoutCallingProvider()
    {
        const string message = "Các buổi khám tôi đã đặt trong thời gian tới có những gì?";
        var budget = new CanaryProviderAttemptBudget(12);
        using var factory = new SyntheticCanaryFactory(budget);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await factory.SeedAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var router = scope.ServiceProvider.GetRequiredService<HybridIntentRouter>();
            var analysis = new AiConversationPipeline(new VietnameseIntentClassifier(), new AiSafetyGuard()).Analyze(message);
            var decision = router.Plan(new AiCopilotPlanningContext { Role = AiActorRole.Patient, NormalizedMessage = analysis.NormalizedText, Analysis = analysis });
            Assert.Equal("model", router.LastRoute!.Source);
            Assert.Equal("MyAppointments", router.LastRoute.Label);
            Assert.Equal("patient.get_my_appointments", Assert.Single(decision.ToolCalls).Name);
            Assert.False(decision.RequiresProvider);
        }
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = "canary.patient@synthetic.invalid", password = "Canary@12345" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new
        {
            message, conversationId = "conv_role_intent_v3", sessionId = "sess_role_intent_v3",
            currentRoute = "/patient/appointments", locale = "vi-VN", timezone = "Asia/Ho_Chi_Minh"
        });
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(AiProviderStatusContract.NotCalled, data.GetProperty("providerState").GetString());
        Assert.Equal(0, budget.Consumed);
    }
}
