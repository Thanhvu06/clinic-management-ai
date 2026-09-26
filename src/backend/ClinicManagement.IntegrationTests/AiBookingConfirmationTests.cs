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
using Microsoft.Extensions.DependencyInjection;
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
