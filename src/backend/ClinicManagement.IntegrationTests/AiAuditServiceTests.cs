using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AiAuditServiceTests : IntegrationTestBase
{
    public AiAuditServiceTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AuditMetadata_UsesAllowlist_DropsSecretsInvalidJsonAndOversizedValues()
    {
        using var scope = Factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAiAuditService>();
        var raw = JsonSerializer.Serialize(new
        {
            provider = "gemini",
            status = "degraded",
            retryCount = 2,
            prompt = "triệu chứng bí mật",
            response = "raw provider response",
            password = "Pass@123",
            token = "secret-token",
            nested = new { reason = "không được lưu" },
            idempotencyKey = "short-but-never-persisted"
        });

        var result = await audit.LogActionAsync(new AiAuditLogEntry
        {
            UserId = Patient1Id,
            SessionId = "sess_audit_allowlist",
            ActionType = "ProviderFailure",
            Outcome = "Degraded",
            MetadataJson = raw
        });

        Assert.True(result.Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = db.AiAuditLogs.OrderByDescending(x => x.Id).First(x => x.ActionType == "ProviderFailure");
        Assert.NotNull(stored.MetadataJson);
        using var document = JsonDocument.Parse(stored.MetadataJson!);
        Assert.Equal("gemini", document.RootElement.GetProperty("provider").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("retryCount").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("prompt", out _));
        Assert.False(document.RootElement.TryGetProperty("response", out _));
        Assert.False(document.RootElement.TryGetProperty("password", out _));
        Assert.False(document.RootElement.TryGetProperty("token", out _));
        Assert.False(document.RootElement.TryGetProperty("nested", out _));
        Assert.False(document.RootElement.TryGetProperty("idempotencyKey", out _));

        var invalidResult = await audit.LogActionAsync(new AiAuditLogEntry
        {
            UserId = Patient1Id,
            ActionType = "InvalidMetadata",
            Outcome = "Recorded",
            MetadataJson = "{\"provider\":\"gemini\""
        });

        Assert.True(invalidResult.Succeeded);
        var invalidStored = db.AiAuditLogs.OrderByDescending(x => x.Id).First(x => x.ActionType == "InvalidMetadata");
        Assert.Null(invalidStored.MetadataJson);
    }
}
