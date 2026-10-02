using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiRateLimitIntegrationTests : IntegrationTestBase
{
    public AiRateLimitIntegrationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Ai_endpoint_partition_keeps_user_b_isolated_when_user_a_exceeds_the_limit()
    {
        using var lowLimitFactory = CreateLowLimitFactory();
        using var userA = lowLimitFactory.CreateClient();
        using var userB = lowLimitFactory.CreateClient();
        await LoginAsync(userA, "rec@test.com");
        await LoginAsync(userB, "doc@test.com");

        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync("/api/v1/ai/copilot/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync("/api/v1/ai/copilot/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await userA.GetAsync("/api/v1/ai/copilot/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userB.GetAsync("/api/v1/ai/copilot/catalog")).StatusCode);
    }

    [Fact]
    public async Task Copilot_chat_uses_the_partitioned_endpoint_policy()
    {
        using var lowLimitFactory = CreateLowLimitFactory();
        using var client = lowLimitFactory.CreateClient();
        await LoginAsync(client, "doc@test.com");

        var request = new { message = "Tình hình hôm nay thế nào?", sessionId = $"rate-chat-{Guid.NewGuid():N}" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = request.message, sessionId = $"rate-chat-{Guid.NewGuid():N}" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = request.message, sessionId = $"rate-chat-{Guid.NewGuid():N}" })).StatusCode);
    }

    [Fact]
    public async Task Legacy_chat_still_uses_its_dedicated_ai_chat_policy()
    {
        using var lowLimitFactory = CreateLowLimitFactory();
        using var userA = lowLimitFactory.CreateClient();
        using var userB = lowLimitFactory.CreateClient();
        await LoginAsync(userA, "pat1@test.com");
        await LoginAsync(userB, "pat2@test.com");

        var request = new { message = "Xin chào", sessionId = $"rate-legacy-{Guid.NewGuid():N}" };
        Assert.Equal(HttpStatusCode.OK, (await userA.PostAsJsonAsync("/api/v1/ai/chat", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userA.PostAsJsonAsync("/api/v1/ai/chat", new { message = request.message, sessionId = $"rate-legacy-{Guid.NewGuid():N}" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await userA.PostAsJsonAsync("/api/v1/ai/chat", new { message = request.message, sessionId = $"rate-legacy-{Guid.NewGuid():N}" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userB.PostAsJsonAsync("/api/v1/ai/chat", new { message = request.message, sessionId = $"rate-legacy-{Guid.NewGuid():N}" })).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_on_one_server_share_the_ip_partition()
    {
        using var lowLimitFactory = CreateLowLimitFactory();
        using var first = lowLimitFactory.CreateClient();
        using var second = lowLimitFactory.CreateClient();
        var request = new { symptomDescription = "đau đầu kéo dài nhiều ngày" };

        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_clients_on_different_remote_ips_use_different_partitions()
    {
        using var factory = CreateIpAwareLowLimitFactory();
        using var ipA = factory.CreateClient();
        using var ipB = factory.CreateClient();
        ipA.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "203.0.113.10");
        ipB.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "203.0.113.11");
        var request = new { symptomDescription = "đau đầu kéo dài nhiều ngày" };

        Assert.Equal(HttpStatusCode.OK, (await ipA.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ipA.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ipA.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ipB.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
    }

    [Fact]
    public async Task Forwarded_headers_do_not_split_the_same_remote_ip_partition()
    {
        using var factory = CreateIpAwareLowLimitFactory();
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        first.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "203.0.113.20");
        first.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");
        second.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "203.0.113.20");
        second.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.21");
        var request = new { symptomDescription = "đau đầu kéo dài nhiều ngày" };

        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request)).StatusCode);
    }

    [Fact]
    public async Task Same_authenticated_account_uses_one_partition_across_remote_ips()
    {
        using var factory = CreateIpAwareLowLimitFactory();
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        first.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "203.0.113.30");
        second.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "203.0.113.31");
        await LoginAsync(first, "rec@test.com");
        await LoginAsync(second, "rec@test.com");

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/v1/ai/copilot/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/ai/copilot/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.GetAsync("/api/v1/ai/copilot/catalog")).StatusCode);
    }

    [Fact]
    public void Anonymous_partition_uses_remote_ip_and_not_untrusted_forwarded_headers()
    {
        var first = new DefaultHttpContext();
        first.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        first.Request.Headers["X-Forwarded-For"] = "198.51.100.10";

        var second = new DefaultHttpContext();
        second.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.11");
        second.Request.Headers["X-Forwarded-For"] = "198.51.100.10";

        Assert.Equal("ip:192.0.2.10", AiRateLimitPartitioning.GetPartitionKey(first));
        Assert.Equal("ip:192.0.2.11", AiRateLimitPartitioning.GetPartitionKey(second));
        Assert.NotEqual(AiRateLimitPartitioning.GetPartitionKey(first), AiRateLimitPartitioning.GetPartitionKey(second));
    }

    private WebApplicationFactory<Program> CreateLowLimitFactory() => Factory
        .WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"{AiRateLimitOptions.SectionName}:TestingPermitLimit", "2");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AiRateLimitOptions.SectionName}:TestingPermitLimit"] = "2"
            }));
        });

    private WebApplicationFactory<Program> CreateIpAwareLowLimitFactory() => CreateLowLimitFactory()
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>()));

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, downstream) =>
            {
                var header = context.Request.Headers["X-Test-Remote-Ip"].ToString();
                if (IPAddress.TryParse(header, out var ip))
                    context.Connection.RemoteIpAddress = ip;
                await downstream();
            });
            next(app);
        };
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Pass@123" });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", payload.GetProperty("data").GetProperty("accessToken").GetString());
    }
}
