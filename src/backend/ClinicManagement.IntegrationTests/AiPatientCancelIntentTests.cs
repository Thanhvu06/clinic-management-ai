using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiPatientCancelIntentTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    private const string InternalIdentifierPattern = @"(?i)(sessionid|draftid|[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}|(?<![0-9a-f])[0-9a-f]{32}(?![0-9a-f]))";

    public AiPatientCancelIntentTests(CustomWebApplicationFactory factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    [Theory]
    [InlineData("hủy lịch")]
    [InlineData("huỷ lịch hẹn")]
    [InlineData("tôi muốn hủy lịch")]
    public async Task Cancel_WithOwnedDraft_UsesExistingCancellation_WithoutAskingForIds(string message)
    {
        await ClearCancellationFixturesAsync();
        var own = await CreateDraftAsync(Patient1Id);
        var other = await CreateDraftAsync(Patient2Id);
        var appointment = await CreateAppointmentAsync(Patient1EntityId, 75, AppointmentStatus.Confirmed);
        AiBookingConfirmationDto confirmation;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var slot = await db.AppointmentSlots.SingleAsync(x => x.Id == SlotEntityId);
            confirmation = await scope.ServiceProvider.GetRequiredService<IAiBookingConfirmationStore>().CreateAsync(new()
            {
                UserId = Patient1Id, SessionId = own.SessionId!, DraftId = own.DraftId!, DraftVersion = 1,
                ContextSnapshotId = own.SnapshotId, SpecialtyId = SpecialtyEntityId, DoctorId = DoctorEntityId,
                SlotId = slot.Id, SlotDate = slot.SlotDate, StartTime = slot.StartTime, EndTime = slot.EndTime,
                Reason = "Kiểm thử hủy yêu cầu đặt lịch"
            });
        }

        // The client supplies its existing conversation identity, not a draft ID typed by the patient.
        var response = await ChatAsync(message, own.SessionId);
        Assert.Equal("DraftCancelled", response.DialogueOutcome);
        Assert.Equal("Đã hủy bản nháp đặt lịch hiện tại. Bạn có cần hỗ trợ gì khác không?", response.Message);
        Assert.Null(response.BookingDraft);
        AssertPublicText(response);

        using var afterScope = Factory.Services.CreateScope();
        var after = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await after.AiSelectionSnapshots.SingleAsync(x => x.SnapshotId == own.SnapshotId)).IsRevoked);
        Assert.False((await after.AiSelectionSnapshots.SingleAsync(x => x.SnapshotId == other.SnapshotId)).IsRevoked);
        Assert.Null((await after.AiSessions.SingleAsync(x => x.SessionId == own.SessionId)).ActiveDraftId);
        Assert.NotNull((await after.AiBookingConfirmations.SingleAsync(x => x.ConfirmationId == confirmation.ConfirmationId)).RevokedAtUtc);
        Assert.Equal(AppointmentStatus.Confirmed, (await after.Appointments.SingleAsync(x => x.Id == appointment.Id)).Status);
        Assert.Single(await after.AiAuditLogs.Where(x => x.SessionId == own.SessionId && x.ActionType == "CancelDraft").ToListAsync());
    }

    [Theory]
    [InlineData("hủy lịch")]
    [InlineData("huỷ lịch hẹn")]
    [InlineData("tôi muốn hủy lịch")]
    public async Task Cancel_WithoutDraft_WithOwnUpcomingAppointment_UsesExistingAppointmentsRoute(string message)
    {
        await ClearCancellationFixturesAsync();
        var own = await CreateAppointmentAsync(Patient1EntityId, 76, AppointmentStatus.Confirmed);
        await CreateAppointmentAsync(Patient2EntityId, 77, AppointmentStatus.Confirmed);
        var response = await ChatAsync(message, "sess_" + Guid.NewGuid().ToString("N"));

        Assert.Equal("NavigationInquiryResolved", response.DialogueOutcome);
        Assert.Equal("Bạn có lịch hẹn sắp tới. Vui lòng mở Lịch hẹn của tôi, chọn lịch hẹn cần hủy và gửi yêu cầu hủy.", response.Message);
        var action = Assert.Single(response.Actions);
        Assert.Equal(AiActionTypes.ViewMyAppointments, action.Type);
        Assert.Equal(SafeRoutes.Appointments, action.Payload?.TargetUrl);
        Assert.True(action.RequiresAuthentication);
        Assert.False(action.RequiresConfirmation);
        AssertPublicText(response);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.SingleAsync(x => x.Id == own.Id)).Status);
        Assert.Empty(await db.AiCancelledDraftScopes.ToListAsync());
    }

    [Theory]
    [InlineData("hủy lịch")]
    [InlineData("huỷ lịch hẹn")]
    [InlineData("tôi muốn hủy lịch")]
    public async Task Cancel_WithoutAnythingToCancel_ReturnsFriendlyEmptyResponse_WithoutForeignData(string message)
    {
        await ClearCancellationFixturesAsync();
        await CreateAppointmentAsync(Patient1EntityId, -10, AppointmentStatus.Confirmed);
        await CreateAppointmentAsync(Patient1EntityId, 78, AppointmentStatus.Cancelled);
        await CreateAppointmentAsync(Patient2EntityId, 79, AppointmentStatus.Confirmed);
        var other = await CreateDraftAsync(Patient2Id);
        var response = await ChatAsync(message, "sess_" + Guid.NewGuid().ToString("N"));

        Assert.Equal("NothingToCancel", response.DialogueOutcome);
        Assert.Equal("Hiện bạn chưa có lịch hẹn hay yêu cầu đặt lịch nào đang chờ để hủy.", response.Message);
        Assert.Empty(response.Actions);
        AssertPublicText(response);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.AiSelectionSnapshots.SingleAsync(x => x.SnapshotId == other.SnapshotId)).IsRevoked);
        Assert.Empty(await db.AiCancelledDraftScopes.ToListAsync());
    }

    [Fact]
    public async Task Cancel_WithSingleOwnedDraft_AndNoClientIdentifiers_ResolvesServerScope()
    {
        await ClearCancellationFixturesAsync();
        var own = await CreateDraftAsync(Patient1Id);
        var response = await ChatAsync("hủy lịch", null);
        Assert.Equal("DraftCancelled", response.DialogueOutcome);
        AssertPublicText(response);
        using var scope = Factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiSelectionSnapshots
            .SingleAsync(x => x.SnapshotId == own.SnapshotId)).IsRevoked);
    }

    [Fact]
    public async Task Cancel_WithMultipleOwnedDrafts_FailsClosed_WithFriendlyCopy()
    {
        await ClearCancellationFixturesAsync();
        var first = await CreateDraftAsync(Patient1Id);
        var second = await CreateDraftAsync(Patient1Id);
        var response = await ChatAsync("hủy lịch", first.SessionId);
        Assert.Equal("ClarificationRequired", response.DialogueOutcome);
        Assert.Contains("quay lại cuộc trò chuyện", response.Message);
        AssertPublicText(response);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.AiSelectionSnapshots.SingleAsync(x => x.SnapshotId == first.SnapshotId)).IsRevoked);
        Assert.False((await db.AiSelectionSnapshots.SingleAsync(x => x.SnapshotId == second.SnapshotId)).IsRevoked);
    }

    [Fact]
    public async Task Cancel_WithLegacyLocalSelections_ClearsOnlyClientDraft_PreservingSavedTabs()
    {
        await ClearCancellationFixturesAsync();
        var first = await CreateDraftAsync(Patient1Id);
        var second = await CreateDraftAsync(Patient1Id);
        await AuthenticateAsync("pat1@test.com");
        Factory.MockAiProvider.Invocations.Clear();
        var result = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "hủy lịch", PendingSpecialtyId = SpecialtyEntityId, PendingDoctorId = DoctorEntityId
        });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var response = (await result.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
        Assert.Equal("DraftCancelled", response.DialogueOutcome);
        Assert.Null(response.BookingDraft);
        AssertPublicText(response);
        Assert.Empty(Factory.MockAiProvider.Invocations);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await db.AiSelectionSnapshots.SingleAsync(x => x.SnapshotId == first.SnapshotId)).IsRevoked);
        Assert.False((await db.AiSelectionSnapshots.SingleAsync(x => x.SnapshotId == second.SnapshotId)).IsRevoked);
    }

    [Fact]
    public async Task Cancel_WithExpiredDraft_DoesNotCancelOrClaimSuccess()
    {
        await ClearCancellationFixturesAsync();
        var expired = await CreateDraftAsync(Patient1Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AiSessions.Where(x => x.SessionId == expired.SessionId)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            await db.AiSelectionSnapshots.Where(x => x.SnapshotId == expired.SnapshotId)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }
        var response = await ChatAsync("hủy lịch", null);
        Assert.Equal("NothingToCancel", response.DialogueOutcome);
        AssertPublicText(response);
    }

    private async Task<AiChatResponseDto> ChatAsync(string message, string? sessionId)
    {
        await AuthenticateAsync("pat1@test.com");
        Factory.MockAiProvider.Invocations.Clear();
        var result = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = message, SessionId = sessionId, DraftVersion = 1
        });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var response = (await result.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
        _output.WriteLine($"Input: {message}; reply: {response.Message}; outcome: {response.DialogueOutcome}");
        Assert.Empty(Factory.MockAiProvider.Invocations);
        Assert.Equal("NotCalled", response.ProviderStatus);
        Assert.Equal(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
        return response;
    }

    private static void AssertPublicText(AiChatResponseDto response)
    {
        var publicText = string.Join("\n", new[] { response.Message, response.Reply, response.ClarificationPrompt, response.SafetyNotice }
            .Concat(response.Actions.SelectMany(x => new[] { x.Label, x.Description }))
            .Concat(response.ToolResults.Select(x => x.DisplayText)));
        Assert.DoesNotMatch(InternalIdentifierPattern, publicText);
    }

    private async Task ClearCancellationFixturesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // This factory owns a temporary test database; no production data is used.
        await db.AiBookingConfirmations.ExecuteDeleteAsync();
        await db.AiSelectionSnapshots.ExecuteDeleteAsync();
        await db.AiCancelledDraftScopes.ExecuteDeleteAsync();
        await db.AiSessions.ExecuteDeleteAsync();
        await db.AiAuditLogs.ExecuteDeleteAsync();
        await db.Appointments.ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, AppointmentStatus.Cancelled));
    }

    private async Task<AiSelectionSnapshotDto> CreateDraftAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        var sessionId = "sess_" + Guid.NewGuid().ToString("N");
        var draftId = "draft_" + Guid.NewGuid().ToString("N");
        Assert.True((await store.TouchSessionAsync(sessionId, userId, draftId, 1, null)).IsAccepted);
        return await store.CreateSnapshotAsync(new()
        {
            UserId = userId, SessionId = sessionId, DraftId = draftId, DraftVersion = 1,
            SpecialtyId = SpecialtyEntityId, DoctorIds = new() { DoctorEntityId }, SlotIds = new() { SlotEntityId }
        });
    }

    private async Task<Appointment> CreateAppointmentAsync(long patientId, int days, AppointmentStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var slot = await db.AppointmentSlots.SingleAsync(x => x.Id == SlotEntityId);
        var appointment = new Appointment
        {
            AppointmentCode = "CANCEL-" + Guid.NewGuid().ToString("N"), PatientId = patientId,
            DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = slot.Id,
            AppointmentDate = clock.VietnamToday.AddDays(days), StartTime = slot.StartTime, EndTime = slot.EndTime,
            Reason = "Kiểm thử phân biệt lịch hẹn và nháp", Status = status
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }
}
