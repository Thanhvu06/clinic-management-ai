using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Planning;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiPendingActionCancellationTests : IntegrationTestBase
{
    public AiPendingActionCancellationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_cancels_once_with_audit_and_releases_pending(bool staff)
    {
        var action = await Seed(staff);
        var client = await CreateAuthenticatedClientAsync(staff ? "doc@test.com" : "pat1@test.com");
        var url = Route(staff, action.ActionId);
        for (var i = 0; i < 2; i++)
        {
            var response = await client.PostAsJsonAsync(url, new { sessionId = action.SessionId });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("cancelled", (await response.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Status);
        }
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.AiPendingToolActions.SingleAsync(x => x.ActionId == action.ActionId);
        Assert.Equal(AiPendingToolActionState.Cancelled, stored.State);
        Assert.NotNull(stored.CancelledAtUtc);
        Assert.Null(stored.ExecutedAtUtc);
        var audit = Assert.Single(await db.AiAuditLogs.Where(x => x.SessionId == action.SessionId && x.ActionType == "CancelPendingAction").ToListAsync());
        Assert.DoesNotContain("secret-test-arguments", audit.MetadataJson ?? "");
        var clock = scope.ServiceProvider.GetRequiredService<ClinicManagement.Application.Common.Interfaces.IDateTimeProvider>();
        var resolved = await new AiCopilotContextResolver(db, clock).ResolveAsync(new AiCopilotRequestDto { Message = "hỗ trợ" },
            null, staff ? AiActorRole.Doctor : AiActorRole.Patient, action.UserId);
        Assert.False(resolved.HasPendingConfirmation);
    }

    [Theory]
    [InlineData(false, "owner", AiPendingToolActionState.Executing, false, 409)]
    [InlineData(true, "owner", AiPendingToolActionState.Executing, false, 409)]
    [InlineData(false, "owner", AiPendingToolActionState.Completed, false, 409)]
    [InlineData(true, "owner", AiPendingToolActionState.Completed, false, 409)]
    [InlineData(false, "owner", AiPendingToolActionState.Executing, true, 409)]
    [InlineData(true, "owner", AiPendingToolActionState.Completed, true, 409)]
    [InlineData(false, "owner", AiPendingToolActionState.PendingConfirmation, true, 410)]
    [InlineData(true, "owner", AiPendingToolActionState.PendingConfirmation, true, 410)]
    [InlineData(false, "other", AiPendingToolActionState.PendingConfirmation, false, 404)]
    [InlineData(true, "other", AiPendingToolActionState.PendingConfirmation, false, 404)]
    [InlineData(false, "session", AiPendingToolActionState.PendingConfirmation, false, 404)]
    [InlineData(true, "session", AiPendingToolActionState.PendingConfirmation, false, 404)]
    public async Task Cancel_rejects_invalid_scope_or_state(bool staff, string caller, AiPendingToolActionState state, bool expired, int status)
    {
        var action = await Seed(staff, state, expired);
        var client = await CreateAuthenticatedClientAsync(caller == "other" ? (staff ? "doc2@test.com" : "pat2@test.com") : (staff ? "doc@test.com" : "pat1@test.com"));
        var response = await client.PostAsJsonAsync(Route(staff, action.ActionId), new { sessionId = caller == "session" ? "sess_wrong" : action.SessionId });
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.AiPendingToolActions.SingleAsync(x => x.ActionId == action.ActionId)).CancelledAtUtc);
    }

    private static string Route(bool staff, Guid id) => staff ? $"/api/v1/ai/copilot/actions/{id}/cancel" : $"/api/v1/ai/tool-actions/{id}/cancel";

    private async Task<AiPendingToolAction> Seed(bool staff, AiPendingToolActionState state = AiPendingToolActionState.PendingConfirmation, bool expired = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerId = staff ? DoctorId : Patient1Id;
        await db.AiPendingToolActions
            .Where(x => x.UserId == ownerId &&
                        (x.State == AiPendingToolActionState.PendingConfirmation ||
                         x.State == AiPendingToolActionState.Executing ||
                         x.State == AiPendingToolActionState.FailedRetryable))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.State, AiPendingToolActionState.Expired)
                .SetProperty(x => x.ExecutionLeaseId, (Guid?)null)
                .SetProperty(x => x.ExecutionLeaseExpiresAtUtc, (DateTime?)null));
        // Every test owns an isolated resource and session; no real appointments.
        var action = new AiPendingToolAction
        {
            ActionId = Guid.NewGuid(), UserId = ownerId, ActorRole = staff ? "Doctor" : "Patient",
            SessionId = "sess_" + Guid.NewGuid().ToString("N"), ResourceId = Guid.NewGuid().ToString("N"), ResourceType = "test",
            ToolName = staff ? "doctor.prepare_prescription_draft" : "patient.prepare_cancel_appointment",
            RequestHash = "test", NormalizedArgumentsJson = "{\"reason\":\"secret-test-arguments\"}",
            State = state, CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(expired ? -1 : 12)
        };
        db.AiPendingToolActions.Add(action);
        await db.SaveChangesAsync();
        return action;
    }
}
