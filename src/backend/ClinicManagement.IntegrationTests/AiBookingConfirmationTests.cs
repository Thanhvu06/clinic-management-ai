using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AiBookingConfirmationTests : IntegrationTestBase
{
    public AiBookingConfirmationTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ValidAiConfirmation_CreatesAppointment_AndSameKeyRetryReturnsSameAppointment()
    {
        await AuthenticateAsync("pat1@test.com");
        var fixture = await PrepareConfirmationAsync("confirm-valid");
        var idempotencyKey = $"ai-confirm-{Guid.NewGuid():N}";
        var request = BuildRequest(fixture, idempotencyKey);

        var first = await Client.PostAsJsonAsync("/api/v1/appointments", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<ApiResponse<AppointmentDto>>();
        Assert.NotNull(firstBody?.Data);

        // Simulates a lost response: the same confirmation and idempotency key
        // must return the already-created appointment without a second insert.
        var retry = await Client.PostAsJsonAsync("/api/v1/appointments", request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var retryBody = await retry.Content.ReadFromJsonAsync<ApiResponse<AppointmentDto>>();
        Assert.Equal(firstBody!.Data!.Id, retryBody?.Data?.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, db.Appointments.Count(a => a.AppointmentSlotId == fixture.SlotId));
        var persistedConfirmation = db.AiBookingConfirmations.Single(x => x.ConfirmationId == fixture.ConfirmationId);
        Assert.Equal(firstBody.Data.Id, persistedConfirmation.UsedAppointmentId);
    }

    [Fact]
    public async Task AiConfirmation_RejectsPayloadMismatch_CancelledDraft_ExpiredAndOtherUser()
    {
        await AuthenticateAsync("pat1@test.com");
        var fixture = await PrepareConfirmationAsync("confirm-reject");

        var altered = BuildRequest(fixture, $"ai-altered-{Guid.NewGuid():N}");
        altered.Reason = "Một lý do khám khác hoàn toàn so với bản đã xác nhận";
        var alteredResponse = await Client.PostAsJsonAsync("/api/v1/appointments", altered);
        Assert.Equal(HttpStatusCode.Conflict, alteredResponse.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var snapshotStore = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            var confirmationStore = scope.ServiceProvider.GetRequiredService<IAiBookingConfirmationStore>();
            await snapshotStore.InvalidateDraftSnapshotsForCancelAsync(fixture.DraftId, fixture.SessionId, Patient1Id);
            await confirmationStore.RevokeForDraftAsync(Patient1Id, fixture.SessionId, fixture.DraftId);
        }

        var cancelledResponse = await Client.PostAsJsonAsync("/api/v1/appointments", BuildRequest(fixture, $"ai-cancelled-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Conflict, cancelledResponse.StatusCode);

        var expired = await PrepareConfirmationAsync("confirm-expired", TimeSpan.FromMinutes(-1));
        var expiredResponse = await Client.PostAsJsonAsync("/api/v1/appointments", BuildRequest(expired, $"ai-expired-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Conflict, expiredResponse.StatusCode);

        await AuthenticateAsync("pat2@test.com");
        var otherUserResponse = await Client.PostAsJsonAsync("/api/v1/appointments", BuildRequest(expired, $"ai-other-user-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Conflict, otherUserResponse.StatusCode);
    }

    [Fact]
    public async Task ConcurrentAiConfirmationRequests_CreateAtMostOneAppointment()
    {
        await AuthenticateAsync("pat1@test.com");
        var fixture = await PrepareConfirmationAsync("confirm-concurrent");
        var idempotencyKey = $"ai-concurrent-{Guid.NewGuid():N}";

        var first = Client.PostAsJsonAsync("/api/v1/appointments", BuildRequest(fixture, idempotencyKey));
        var second = Client.PostAsJsonAsync("/api/v1/appointments", BuildRequest(fixture, idempotencyKey));
        var responses = await Task.WhenAll(first, second);

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, db.Appointments.Count(a => a.AppointmentSlotId == fixture.SlotId));
    }

    [Fact]
    public async Task OrdinaryChatTurn_PersistsConfirmationAction_AndAppointmentIsVisibleAcrossActors()
    {
        await AuthenticateAsync("pat1@test.com");
        Factory.MockAiProvider
            .Setup(x => x.ChatWithAiAsync(
                It.IsAny<string>(),
                It.IsAny<List<ChatMessageDto>>(),
                It.IsAny<List<WhitelistItemDto>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult
            {
                Reply = "Tôi đã đối chiếu đủ thông tin lịch khám.",
                PrimaryIntent = AiChatIntentTypes.ProvideReason,
                ExtractedReason = "Đau đầu kéo dài nhiều ngày cần được bác sĩ kiểm tra",
                Urgency = "ROUTINE",
                IsClear = true
            });

        var date = GetFutureWorkingDate(140);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(13, 0), new TimeOnly(13, 30));
        var sessionId = $"sess_ordinary_{Guid.NewGuid():N}";
        var draftId = $"draft_ordinary_{Guid.NewGuid():N}";
        var reason = "Đau đầu kéo dài nhiều ngày cần được bác sĩ kiểm tra";

        var chatResponse = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tôi muốn đặt lịch theo thông tin đã chọn",
            SessionId = sessionId,
            DraftId = draftId,
            DraftVersion = 1,
            PendingSpecialtyId = SpecialtyEntityId,
            PendingDoctorId = DoctorEntityId,
            PendingSlotId = slot.Id,
            PendingSlotDate = date.ToString("yyyy-MM-dd"),
            Reason = reason
        });

        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        var chatBody = await chatResponse.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>();
        Assert.NotNull(chatBody?.Data);
        Assert.Equal("PendingConfirmation", chatBody!.Data!.DialogueOutcome);
        var action = chatBody.Data.Actions.Single(x => x.Type == AiActionTypes.ConfirmBooking);
        Assert.False(string.IsNullOrWhiteSpace(action.Payload?.ConfirmationId));
        Assert.Equal(sessionId, action.Payload?.SessionId);
        Assert.Equal(draftId, action.Payload?.DraftId);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AiBookingConfirmations.AnyAsync(x => x.ConfirmationId == action.Payload!.ConfirmationId));
        }

        var idempotencyKey = $"ai-ordinary-{Guid.NewGuid():N}";
        var createPayload = new CreateAppointmentRequest
        {
            DoctorId = action.Payload!.DoctorId!.Value,
            SpecialtyId = action.Payload.SpecialtyId!.Value,
            AppointmentSlotId = action.Payload.SlotId!.Value,
            Reason = action.Payload.Reason!,
            ConfirmationId = action.Payload.ConfirmationId,
            ContextSnapshotId = action.Payload.ContextSnapshotId,
            SessionId = action.Payload.SessionId,
            DraftId = action.Payload.DraftId,
            DraftVersion = action.Payload.DraftVersion,
            IdempotencyKey = idempotencyKey
        };
        var first = await Client.PostAsJsonAsync("/api/v1/appointments", createPayload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<ApiResponse<AppointmentDto>>();
        Assert.NotNull(firstBody?.Data);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Appointments.CountAsync(x => x.Id == firstBody!.Data!.Id));
        }

        var patientView = await Client.GetStringAsync("/api/v1/appointments/my");
        Assert.Contains(firstBody!.Data!.AppointmentCode, patientView, StringComparison.Ordinal);

        var receptionClient = await CreateAuthenticatedClientAsync("rec@test.com");
        var receptionView = await receptionClient.GetAsync($"/api/v1/reception/appointments/{firstBody.Data.Id}");
        Assert.Equal(HttpStatusCode.OK, receptionView.StatusCode);
        var receptionJson = await receptionView.Content.ReadAsStringAsync();
        Assert.Contains(firstBody.Data.AppointmentCode, receptionJson, StringComparison.Ordinal);

        var retry = await Client.PostAsJsonAsync("/api/v1/appointments", createPayload);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var retryBody = await retry.Content.ReadFromJsonAsync<ApiResponse<AppointmentDto>>();
        Assert.Equal(firstBody.Data.Id, retryBody?.Data?.Id);
    }

    [Fact]
    public async Task ConcurrentConfirmationCreation_LeavesOnePendingAndOnlyWinnerCanBook()
    {
        await AuthenticateAsync("pat1@test.com");
        var fixture = await PrepareConfirmationAsync("confirm-active-race");
        var request = new CreateAiBookingConfirmationRequest
        {
            UserId = Patient1Id,
            SessionId = fixture.SessionId,
            DraftId = fixture.DraftId,
            DraftVersion = 1,
            ContextSnapshotId = fixture.SnapshotId,
            SpecialtyId = SpecialtyEntityId,
            DoctorId = DoctorEntityId,
            SlotId = fixture.SlotId,
            SlotDate = fixture.SlotDate,
            StartTime = fixture.StartTime,
            EndTime = fixture.EndTime,
            Reason = fixture.Reason
        };

        async Task<AiBookingConfirmationDto> CreateInIndependentContextAsync()
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAiBookingConfirmationStore>().CreateAsync(request);
        }

        var created = await Task.WhenAll(CreateInIndependentContextAsync(), CreateInIndependentContextAsync());
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.AiBookingConfirmations
                .Where(x => x.UserId == Patient1Id && x.SessionId == fixture.SessionId && x.DraftId == fixture.DraftId && !x.UsedAtUtc.HasValue && !x.RevokedAtUtc.HasValue)
                .ToListAsync();
            Assert.Single(pending);
            Assert.Contains(pending[0].ConfirmationId, created.Select(x => x.ConfirmationId));
            Assert.Equal(1, await db.AiBookingConfirmations.CountAsync(x => x.ConfirmationId == fixture.ConfirmationId && x.RevokedAtUtc.HasValue));
        }

        AiBookingConfirmationDto winner;
        // Resolve the winner from a fresh context rather than relying on task order.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            winner = created.Single(x => db.AiBookingConfirmations.Any(y => y.ConfirmationId == x.ConfirmationId && !y.RevokedAtUtc.HasValue && !y.UsedAtUtc.HasValue));
        }

        var key = $"ai-active-race-{Guid.NewGuid():N}";
        var booked = await Client.PostAsJsonAsync("/api/v1/appointments", BuildRequest(fixture with { ConfirmationId = winner.ConfirmationId }, key));
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var bookedBody = await booked.Content.ReadFromJsonAsync<ApiResponse<AppointmentDto>>();
        Assert.NotNull(bookedBody?.Data);

        using var finalScope = Factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await finalDb.Appointments.CountAsync(x => x.AppointmentSlotId == fixture.SlotId));
        var oldIds = created.Select(x => x.ConfirmationId).Where(x => x != winner.ConfirmationId).ToList();
        foreach (var oldId in oldIds)
        {
            Assert.True(await finalDb.AiBookingConfirmations.AnyAsync(x => x.ConfirmationId == oldId && x.RevokedAtUtc.HasValue));
        }
    }

    [Fact]
    public async Task ConfirmationCleanup_UsesRetentionForExpiredRevokedAndUsedRows()
    {
        await AuthenticateAsync("pat1@test.com");
        var expired = await PrepareConfirmationAsync("confirm-cleanup-expired", TimeSpan.FromMinutes(-1));
        var used = await PrepareConfirmationAsync("confirm-cleanup-used");

        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAiBookingConfirmationStore>();
            await store.MarkUsedAsync(used.ConfirmationId, 9001, "cleanup-key");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SaveChangesAsync();
            await store.PurgeExpiredAsync(DateTime.UtcNow.AddHours(23));
            Assert.True(await db.AiBookingConfirmations.AnyAsync(x => x.ConfirmationId == used.ConfirmationId));
            await store.PurgeExpiredAsync(DateTime.UtcNow.AddHours(26));
        }

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.AiBookingConfirmations.AnyAsync(x => x.ConfirmationId == expired.ConfirmationId));
        Assert.False(await verifyDb.AiBookingConfirmations.AnyAsync(x => x.ConfirmationId == used.ConfirmationId));
    }

    [Fact]
    public async Task HttpEndToEnd_ChatConfirmation_CreatesAppointment_AndAppointmentIsVisibleToPatient()
    {
        await AuthenticateAsync("pat1@test.com");
        var date = GetFutureWorkingDate(85);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(11, 0), new TimeOnly(11, 30));
        var sessionId = $"sess_e2e_{Guid.NewGuid():N}";
        var draftId = $"draft_e2e_{Guid.NewGuid():N}";
        var reason = "Đau đầu kéo dài nhiều ngày cần được bác sĩ kiểm tra";

        var chatResponse = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "chốt",
            SessionId = sessionId,
            DraftId = draftId,
            DraftVersion = 1,
            PendingSpecialtyId = SpecialtyEntityId,
            PendingDoctorId = DoctorEntityId,
            PendingSlotId = slot.Id,
            PendingSlotDate = date.ToString("yyyy-MM-dd"),
            Reason = reason
        });
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        var chatBody = await chatResponse.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>();
        Assert.Equal("PendingConfirmation", chatBody?.Data?.DialogueOutcome);
        Assert.NotNull(chatBody?.Data?.BookingDraft?.ConfirmationId);

        var confirmAction = chatBody!.Data!.Actions.Single(x => x.Type == AiActionTypes.ConfirmBooking);
        var createResponse = await Client.PostAsJsonAsync("/api/v1/appointments", new CreateAppointmentRequest
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = reason,
            ConfirmationId = chatBody.Data.BookingDraft!.ConfirmationId,
            ContextSnapshotId = chatBody.Data.ContextSnapshotId,
            SessionId = chatBody.Data.SessionId,
            DraftId = chatBody.Data.DraftId,
            DraftVersion = chatBody.Data.BookingDraft.Version,
            IdempotencyKey = $"ai-e2e-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var appointmentBody = await createResponse.Content.ReadFromJsonAsync<ApiResponse<AppointmentDto>>();
        Assert.NotNull(appointmentBody?.Data);
        Assert.Equal(confirmAction.Payload?.SlotId, appointmentBody!.Data!.AppointmentSlotId);

        var visibleAppointments = await Client.GetStringAsync("/api/v1/appointments/my");
        Assert.Contains(appointmentBody.Data.AppointmentCode, visibleAppointments, StringComparison.Ordinal);
    }

    private async Task<ConfirmationFixture> PrepareConfirmationAsync(string suffix, TimeSpan? ttl = null)
    {
        var date = GetFutureWorkingDate(500 + Math.Abs(suffix.GetHashCode()) % 20);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(10, 0), new TimeOnly(10, 30));
        var sessionId = $"sess_{suffix}_{Guid.NewGuid():N}";
        var draftId = $"draft_{suffix}_{Guid.NewGuid():N}";
        var reason = "Đau đầu kéo dài nhiều ngày cần được bác sĩ kiểm tra";

        using var scope = Factory.Services.CreateScope();
        var snapshotStore = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        var snapshot = await snapshotStore.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = Patient1Id,
            SessionId = sessionId,
            DraftId = draftId,
            DraftVersion = 1,
            SpecialtyId = SpecialtyEntityId,
            DoctorId = DoctorEntityId,
            SlotDate = date.ToString("yyyy-MM-dd"),
            DoctorIds = new List<long> { DoctorEntityId },
            SlotIds = new List<long> { slot.Id }
        });

        var confirmationStore = scope.ServiceProvider.GetRequiredService<IAiBookingConfirmationStore>();
        var confirmation = await confirmationStore.CreateAsync(new CreateAiBookingConfirmationRequest
        {
            UserId = Patient1Id,
            SessionId = sessionId,
            DraftId = draftId,
            DraftVersion = 1,
            ContextSnapshotId = snapshot.SnapshotId,
            SpecialtyId = SpecialtyEntityId,
            DoctorId = DoctorEntityId,
            SlotId = slot.Id,
            SlotDate = date,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Reason = reason,
            Ttl = ttl
        });

        return new ConfirmationFixture(
            confirmation.ConfirmationId,
            snapshot.SnapshotId,
            sessionId,
            draftId,
            slot.Id,
            date,
            slot.StartTime,
            slot.EndTime,
            reason);
    }

    private static CreateAppointmentRequest BuildRequest(ConfirmationFixture fixture, string idempotencyKey) => new()
    {
        DoctorId = DoctorEntityId,
        SpecialtyId = SpecialtyEntityId,
        AppointmentSlotId = fixture.SlotId,
        Reason = fixture.Reason,
        ConfirmationId = fixture.ConfirmationId,
        ContextSnapshotId = fixture.SnapshotId,
        SessionId = fixture.SessionId,
        DraftId = fixture.DraftId,
        DraftVersion = 1,
        IdempotencyKey = idempotencyKey
    };

    private sealed record ConfirmationFixture(
        string ConfirmationId,
        string SnapshotId,
        string SessionId,
        string DraftId,
        long SlotId,
        DateOnly SlotDate,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string Reason);
}
