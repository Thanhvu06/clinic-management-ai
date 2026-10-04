using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// S2: server-owned codes for read tools that have no chip. Typed text can
/// only select a code; the tool and any fixed argument come from the server.
/// </summary>
[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class TypedSuggestionCodeTests : IntegrationTestBase
{
    private const string ChatUrl = "/api/v1/ai/copilot/chat";
    private const string SuggestionsUrl = "/api/v1/ai/copilot/suggestions";

    private static readonly (string Email, string Role, string Code, string Tool, string Card, string? Period)[] Cases =
    {
        ("rec@test.com", "Receptionist", "receptionist.upcoming_appointments", "reception.get_upcoming_appointments", "reception_appointments", null),
        ("rec@test.com", "Receptionist", "receptionist.pending_payments", "reception.get_pending_payments", "reception_pending_payments", null),
        ("doc@test.com", "Doctor", "doctor.today_appointments", "doctor.get_my_appointments_today", "doctor_appointments_today", null),
        ("tech@test.com", "DiagnosticTechnician", "technician.completed_today", "technician.get_completed_today", "technician_completed_today", null),
        ("pharm@test.com", "Pharmacist", "pharmacist.low_stock", "pharmacist.get_low_stock", "pharmacy_low_stock", null),
        ("admin@test.com", "Admin", "admin.revenue_today", "admin.get_revenue_summary", "admin_revenue_summary", "today"),
        ("admin@test.com", "Admin", "admin.revenue_this_month", "admin.get_revenue_summary", "admin_revenue_summary", "this_month")
    };

    private static readonly string[] Emails = { "pat1@test.com", "rec@test.com", "doc@test.com", "tech@test.com", "pharm@test.com", "admin@test.com" };

    public TypedSuggestionCodeTests(CustomWebApplicationFactory factory) : base(factory) { }

    public static IEnumerable<object[]> EachCase => Cases.Select(c => new object[] { c.Code });

    [Theory]
    [MemberData(nameof(EachCase))]
    public async Task Typed_code_runs_its_read_tool_locally_with_server_fixed_arguments(string code)
    {
        await EnsureAdminRevenueScopeAsync();
        var item = Cases.Single(c => c.Code == code);
        var client = await CreateAuthenticatedClientAsync(item.Email);
        var data = await ChatAsync(client, new
        {
            message = "doanh thu thang truoc lich hen tuan nay", suggestionCode = code, sessionId = Session(),
            arguments = new { period = "last_month", facilityId = long.MaxValue },
            toolCalls = new[] { new { name = "patient.get_my_bills", arguments = new { } } }
        });

        Assert.False(data.GetProperty("providerWasCalled").GetBoolean());
        Assert.Equal(0, data.GetProperty("providerAttemptCount").GetInt32());
        Assert.Equal("Deterministic", data.GetProperty("plannerMode").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("errorCode").ValueKind);
        Assert.Equal(new[] { item.Tool }, data.GetProperty("executedToolNames").EnumerateArray().Select(x => x.GetString()));
        var card = Assert.Single(data.GetProperty("cards").EnumerateArray());
        Assert.Equal(item.Card, card.GetProperty("type").GetString());
        Assert.Equal("Dữ liệu đã kiểm chứng", card.GetProperty("title").GetString());
        if (item.Period is not null)
            Assert.Equal(item.Period, card.GetProperty("data").GetProperty("period").GetString());
    }

    [Theory]
    [MemberData(nameof(EachCase))]
    public async Task Typed_code_from_any_other_role_is_rejected_like_existing_codes(string code)
    {
        await EnsureAdminRevenueScopeAsync();
        var item = Cases.Single(c => c.Code == code);
        var owner = await ChatAsync(await CreateAuthenticatedClientAsync(item.Email), new { message = "x", suggestionCode = code, sessionId = Session() });
        Assert.Equal(new[] { item.Tool }, owner.GetProperty("executedToolNames").EnumerateArray().Select(x => x.GetString()));
        foreach (var email in Emails.Where(e => e != item.Email))
        {
            var client = await CreateAuthenticatedClientAsync(email);
            var data = await ChatAsync(client, new { message = "x", suggestionCode = code, sessionId = Session() });
            Assert.Equal("PLANNER_TOOL_NOT_ALLOWED", data.GetProperty("errorCode").GetString());
            Assert.Empty(data.GetProperty("executedToolNames").EnumerateArray());
            Assert.Empty(data.GetProperty("cards").EnumerateArray());
        }
    }

    [Fact]
    public async Task Suggestions_endpoint_lists_typed_codes_separately_and_keeps_existing_chips()
    {
        foreach (var email in Emails)
        {
            var client = await CreateAuthenticatedClientAsync(email);
            var response = await client.PostAsJsonAsync(SuggestionsUrl, new { });
            var json = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, json);
            var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
            var expected = Cases.Where(c => c.Email == email).Select(c => c.Code).ToArray();
            var chips = data.GetProperty("suggestions").EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToArray();
            var typed = data.GetProperty("typedSuggestions").EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToArray();
            Assert.Equal(expected, typed);
            Assert.Empty(chips.Intersect(typed));
        }
    }

    private async Task EnsureAdminRevenueScopeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facilityId = await db.Facilities.Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        if (!await db.StaffFacilityAssignments.AnyAsync(x => x.UserId == AdminId && x.Role == "Admin" && x.IsActive))
        {
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = AdminId, FacilityId = facilityId, Role = "Admin", IsActive = true });
            await db.SaveChangesAsync();
        }
    }

    private static string Session() => $"sess_typed_{Guid.NewGuid():N}";

    private static async Task<JsonElement> ChatAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(ChatUrl, body);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Clone();
    }
}
