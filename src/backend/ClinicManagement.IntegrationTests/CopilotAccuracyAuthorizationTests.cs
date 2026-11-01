using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public class CopilotAccuracyAuthorizationTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly (string Tool, AiActorRole Role, string Args)[] Tools =
    [
        ("reception.get_pending_change_requests", AiActorRole.Receptionist, "{}"),
        ("reception.get_unpaid_invoices", AiActorRole.Receptionist, "{}"),
        ("technician.get_service_guidance", AiActorRole.DiagnosticTechnician, "{\"query\":\"glucose\"}"),
        ("pharmacist.get_medicine_stock", AiActorRole.Pharmacist, "{\"query\":\"paracetamol\"}"),
        ("pharmacist.get_medicine_batches", AiActorRole.Pharmacist, "{\"status\":\"expiring\"}"),
        ("admin.get_revenue_report", AiActorRole.Admin, "{\"period\":\"last_7_days\"}"),
        ("admin.get_active_doctors", AiActorRole.Admin, "{}"),
        ("admin.get_pending_leave_requests", AiActorRole.Admin, "{}")
    ];

    public static IEnumerable<object[]> ForeignRoles => Tools.SelectMany(tool => Enum.GetValues<AiActorRole>()
        .Where(role => role != tool.Role).Select(role => new object[] { tool.Tool, role, tool.Args }));
    public static IEnumerable<object[]> ForgedScopes => Tools.SelectMany(tool => new[] { "actorId", "facilityId" }
        .Select(property => new object[] { tool.Tool, tool.Role, tool.Args, property }));

    [Fact]
    public void AddedToolDefinitions_AreReadOnlyAndRestrictedToOneRequiredRole()
    {
        foreach (var tool in Tools)
        {
            var definition = Assert.Single(AiRoleToolCatalog.DeterministicReadDefinitions, d => d.Name == tool.Tool);
            Assert.Equal(tool.Role, Assert.Single(definition.AllowedRoles));
            Assert.Equal(AiToolAccessMode.RoleRestricted, definition.AccessMode);
            Assert.Equal(AiToolRiskLevel.Low, definition.RiskLevel);
            Assert.Equal(AiToolConfirmationRequirement.None, definition.Confirmation);
            Assert.DoesNotContain(definition.Capabilities, c => c.ToString().StartsWith("Prepare") || c.ToString().StartsWith("Execute"));
            Assert.True(AiPlannerPolicy.IsAllowed(tool.Tool));
        }
    }

    [Theory]
    [MemberData(nameof(ForeignRoles))]
    public async Task OtherRoles_CannotCallAnyAddedReadTool(string tool, AiActorRole role, string args)
    {
        var client = await CreateAuthenticatedClientAsync(Email(role));
        var response = await client.PostAsJsonAsync("/api/v1/ai/tools/execute", new { toolName = tool, toolVersion = "1.0", argumentsJson = args });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ForgedScopes))]
    public async Task AddedReadTools_RejectClientSuppliedAuthority(string tool, AiActorRole role, string args, string property)
    {
        var client = await CreateAuthenticatedClientAsync(Email(role));
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args)!;
        arguments[property] = JsonSerializer.SerializeToElement("untrusted");
        var response = await client.PostAsJsonAsync("/api/v1/ai/tools/execute", new { toolName = tool, toolVersion = "1.0", argumentsJson = JsonSerializer.Serialize(arguments) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AiToolExecutionResult>())!;
        Assert.Equal("FORBIDDEN_TOOL_ARGUMENT", result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("last_0_days")]
    [InlineData("last_367_days")]
    [InlineData("last_999999999999_days")]
    public async Task RevenueReport_RejectsInvalidPeriods(string period)
    {
        var client = await CreateAuthenticatedClientAsync("admin@test.com");
        var response = await client.PostAsJsonAsync("/api/v1/ai/tools/execute", new { toolName = "admin.get_revenue_report", toolVersion = "1.0", argumentsJson = JsonSerializer.Serialize(new { period }) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("xóa tài khoản bác sĩ Khải")]
    [InlineData("khóa tài khoản bác sĩ Khải")]
    [InlineData("sửa thông tin bác sĩ Khải")]
    public async Task AdminWrites_OnlyNavigate_WithoutMutatingUsers(string message)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.IsActive, u.FullName }).ToListAsync();
        var client = await CreateAuthenticatedClientAsync("admin@test.com");
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("WRITE_NOT_SUPPORTED", data.GetProperty("errorCode").GetString());
        Assert.Empty(data.GetProperty("executedToolNames").EnumerateArray());
        Assert.Equal("/admin/users", data.GetProperty("navigationRoute").GetString());
        Assert.Equal("NotCalled", data.GetProperty("providerStatus").GetString());
        var after = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.IsActive, u.FullName }).ToListAsync();
        Assert.Equal(before, after);
    }

    private static string Email(AiActorRole role) => role switch
    {
        AiActorRole.Admin => "admin@test.com", AiActorRole.Doctor => "doc@test.com", AiActorRole.Receptionist => "rec@test.com",
        AiActorRole.DiagnosticTechnician => "tech@test.com", AiActorRole.Pharmacist => "pharm@test.com", _ => "pat1@test.com"
    };
}
