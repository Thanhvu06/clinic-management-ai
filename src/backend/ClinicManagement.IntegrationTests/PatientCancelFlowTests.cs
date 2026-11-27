using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.AI.Tools;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class PatientCancelFlowTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static int _phone = 100;

    [Fact]
    public async Task Pending_appointment_is_cancelled_at_once_releases_the_slot_and_is_audited_for_the_patient()
    {
        var (appointment, slot) = await AppointmentAsync(Patient1EntityId, AppointmentStatus.Pending, 3);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var since = DateTime.UtcNow;

        using var response = await patient.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/cancel", new { reason = "Có việc gia đình đột xuất" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AppointmentStatus.Cancelled, (await db.Appointments.SingleAsync(a => a.Id == appointment.Id)).Status);
        Assert.False((await db.AppointmentSlots.SingleAsync(s => s.Id == slot.Id)).IsBooked);
        Assert.Empty(await db.AppointmentChangeRequests.Where(r => r.AppointmentId == appointment.Id).ToListAsync());
        var history = await db.AppointmentHistories.SingleAsync(h => h.AppointmentId == appointment.Id && h.Action == AppointmentHistoryAction.Cancelled);
        Assert.Equal(Patient1Id, history.PerformedByUserId);
        var audit = await db.SystemAuditLogs.SingleAsync(l => l.Action == UserActionAudit.CancelAppointment && l.EntityId == appointment.Id.ToString());
        Assert.Equal(Patient1Id, audit.UserId);
        Assert.Contains("Bệnh nhân", audit.Description);
        Assert.True(audit.CreatedAt >= since.AddSeconds(-5));
    }

    [Fact]
    public async Task Cancel_now_for_a_confirmed_appointment_is_refused_by_the_server_and_changes_nothing()
    {
        var (appointment, slot) = await AppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed, 4);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");

        using var response = await patient.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/cancel", new { reason = "Muốn hủy ngay lập tức" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("APPOINTMENT_NOT_PENDING", await response.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.SingleAsync(a => a.Id == appointment.Id)).Status);
        Assert.True((await db.AppointmentSlots.SingleAsync(s => s.Id == slot.Id)).IsBooked);
        Assert.Empty(await db.SystemAuditLogs.Where(l => l.Action == UserActionAudit.CancelAppointment && l.EntityId == appointment.Id.ToString()).ToListAsync());
    }

    [Fact]
    public async Task Confirmed_appointment_still_goes_through_a_reception_request()
    {
        var (appointment, _) = await AppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed, 5);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var request = await patient.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/cancellation-requests", new { reason = "Bận việc cơ quan đột xuất" });
        Assert.True(request.IsSuccessStatusCode, await request.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AppointmentStatus.PendingCancellation, (await db.Appointments.SingleAsync(a => a.Id == appointment.Id)).Status);
        Assert.Single(await db.AppointmentChangeRequests.Where(r => r.AppointmentId == appointment.Id && r.Status == AppointmentChangeRequestStatus.Pending).ToListAsync());
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public async Task Another_patients_appointment_is_indistinguishable_from_a_missing_appointment(AppointmentStatus status)
    {
        var (appointment, slot) = await AppointmentAsync(Patient1EntityId, status, 5);
        using var client = await CreateAuthenticatedClientAsync("pat2@test.com");
        var payload = new { reason = "Kiểm tra quyền hủy lịch" };

        using var foreign = await client.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/cancel", payload);
        using var missing = await client.PostAsJsonAsync($"/api/v1/appointments/{long.MaxValue}/cancel", payload);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(missing.StatusCode, foreign.StatusCode);
        var foreignError = await foreign.Content.ReadFromJsonAsync<ApiErrorResponse>();
        var missingError = await missing.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(foreignError);
        Assert.NotNull(missingError);
        Assert.Equal(missingError.Message, foreignError.Message);
        Assert.Equal(missingError.ErrorCode, foreignError.ErrorCode);
        Assert.DoesNotContain(appointment.AppointmentCode, await foreign.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(status, (await db.Appointments.SingleAsync(a => a.Id == appointment.Id)).Status);
        Assert.True((await db.AppointmentSlots.SingleAsync(s => s.Id == slot.Id)).IsBooked);
        Assert.False(await db.SystemAuditLogs.AnyAsync(l => l.Action == UserActionAudit.CancelAppointment && l.EntityId == appointment.Id.ToString()));
    }

    [Fact]
    public async Task Reception_rejecting_a_cancellation_request_keeps_the_appointment_and_writes_an_audit_entry()
    {
        var (appointment, _) = await AppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed, 6);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var request = await patient.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/cancellation-requests", new { reason = "Bận việc cơ quan đột xuất" });
        Assert.True(request.IsSuccessStatusCode, await request.Content.ReadAsStringAsync());
        long requestId;
        using (var scope = Factory.Services.CreateScope())
            requestId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppointmentChangeRequests
                .Where(r => r.AppointmentId == appointment.Id).Select(r => r.Id).SingleAsync();

        using var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        using var reject = await reception.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/reject", new { note = "test QA" });
        Assert.True(reject.IsSuccessStatusCode, await reject.Content.ReadAsStringAsync());

        using var after = Factory.Services.CreateScope();
        var db = after.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.SingleAsync(a => a.Id == appointment.Id)).Status);
        var audit = await db.SystemAuditLogs.SingleAsync(l => l.Action == UserActionAudit.RejectAppointmentChangeRequest && l.EntityId == appointment.Id.ToString());
        Assert.Equal(ReceptionistId, audit.UserId);
        Assert.Contains("test QA", audit.Description);
        Assert.Contains(appointment.AppointmentCode, audit.Description);
    }

    [Fact]
    public async Task Chat_cancel_with_an_open_ai_draft_and_a_day_finds_the_one_real_appointment_and_asks_to_confirm_it()
    {
        var (email, patientId, userId) = await RegisterPatientAsync();
        var today = Today();
        var tomorrow = await AppointmentAsync(patientId, AppointmentStatus.Pending, date: today.AddDays(1));
        await AppointmentAsync(patientId, AppointmentStatus.Confirmed, date: today.AddDays(3));
        var draftSession = await OpenDraftAsync(userId);

        using var client = await CreateAuthenticatedClientAsync(email);
        foreach (var (message, session) in new[]
        {
            ("hủy lịch hẹn ngày mai của tôi", NewSession()),
            ($"hủy lịch {today.AddDays(1):dd/MM}", draftSession),
            ($"hủy {today.AddDays(1):dd/MM/yyyy}", draftSession),
            ($"hủy lịch mã {tomorrow.Appointment.AppointmentCode.ToLowerInvariant()}", draftSession)
        })
        {
            var response = await ChatAsync(client, message, session);
            Assert.Equal(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
            Assert.NotEqual("ClarificationRequired", response.DialogueOutcome);
            Assert.DoesNotContain("quay lại cuộc trò chuyện", response.Message);
            Assert.StartsWith($"Tìm thấy 1 lịch hẹn phù hợp: ngày {today.AddDays(1):dd/MM/yyyy}", response.Message);
            Assert.Contains("hủy ngay", response.Message);
            var action = Assert.Single(response.Actions, a => a.Type == AiActionTypes.RequestCancellation);
            Assert.Equal(tomorrow.Appointment.Id, action.Payload.AppointmentId);
            Assert.Equal("Hủy lịch này", action.Label);
            Assert.Contains(response.Actions, a => a.Type == AiActionTypes.ViewMyAppointments && a.Payload.TargetUrl == SafeRoutes.Appointments);
        }

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.SingleAsync(a => a.Id == tomorrow.Appointment.Id)).Status);
        Assert.Equal("draft_" + draftSession[5..], (await db.AiSessions.SingleAsync(s => s.SessionId == draftSession)).ActiveDraftId);
    }

    [Theory]
    [InlineData("hủy")]
    [InlineData("HỦY!")]
    [InlineData("thôi")]
    [InlineData("không đặt nữa")]
    public async Task Short_chat_cancel_clears_the_active_draft_and_leaves_the_appointment(string message)
    {
        var (email, patientId, userId) = await RegisterPatientAsync();
        var (appointment, _) = await AppointmentAsync(patientId, AppointmentStatus.Pending, date: Today().AddDays(2));
        var draftSession = await OpenDraftAsync(userId);
        using var client = await CreateAuthenticatedClientAsync(email);

        var response = await ChatAsync(client, message, draftSession);
        Assert.Equal("DraftCancelled", response.DialogueOutcome);
        Assert.Empty(response.Actions);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.AiSessions.SingleAsync(s => s.SessionId == draftSession)).ActiveDraftId);
        Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.SingleAsync(a => a.Id == appointment.Id)).Status);
    }

    [Theory]
    [InlineData("hủy lịch")]
    [InlineData("hủy lịch khám")]
    [InlineData("tôi muốn hủy lịch khám")]
    public async Task Chat_cancel_without_a_target_asks_before_cancelling_anything(string message)
    {
        var (email, patientId, userId) = await RegisterPatientAsync();
        var day = Today().AddDays(2);
        var pending = await AppointmentAsync(patientId, AppointmentStatus.Pending, date: day);
        var confirmed = await AppointmentAsync(patientId, AppointmentStatus.Confirmed, date: day);
        await AppointmentAsync(patientId, AppointmentStatus.PendingCancellation, date: day);
        var draftSession = await OpenDraftAsync(userId);
        using var client = await CreateAuthenticatedClientAsync(email);

        var response = await ChatAsync(client, message, draftSession);
        Assert.Equal("ClarificationRequired", response.DialogueOutcome);
        Assert.Contains("bản nháp đang đặt dở hay một lịch hẹn đã đặt", response.Message);
        AssertCancelChoices(response, pending.Appointment.Id, confirmed.Appointment.Id);

        var targeted = await ChatAsync(client, $"hủy lịch khám ngày {day:dd/MM}", draftSession);
        Assert.Equal("PatientReadResolved", targeted.DialogueOutcome);
        AssertCancelChoices(targeted, pending.Appointment.Id, confirmed.Appointment.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("draft_" + draftSession[5..], (await db.AiSessions.SingleAsync(s => s.SessionId == draftSession)).ActiveDraftId);
        Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.SingleAsync(a => a.Id == pending.Appointment.Id)).Status);
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.SingleAsync(a => a.Id == confirmed.Appointment.Id)).Status);
    }

    [Fact]
    public async Task Opening_wizard_without_a_saved_draft_still_distinguishes_the_two_cancellation_targets()
    {
        var (email, patientId, _) = await RegisterPatientAsync();
        var (appointment, _) = await AppointmentAsync(patientId, AppointmentStatus.Confirmed, date: Today().AddDays(2));
        using var client = await CreateAuthenticatedClientAsync(email);
        var session = NewSession();

        var ambiguous = await ChatAsync(client, "hủy lịch khám", session, hasOpenBookingWizard: true);
        Assert.Equal("ClarificationRequired", ambiguous.DialogueOutcome);
        Assert.Equal(appointment.Id, Assert.Single(ambiguous.Actions, a => a.Type == AiActionTypes.RequestCancellation).Payload.AppointmentId);

        var cancel = await ChatAsync(client, "hủy", session, hasOpenBookingWizard: true);
        Assert.Equal("DraftCancelled", cancel.DialogueOutcome);
        Assert.Empty(cancel.Actions);
    }

    private static void AssertCancelChoices(AiChatResponseDto response, long pendingId, long confirmedId)
    {
        var actions = response.Actions.Where(a => a.Type == AiActionTypes.RequestCancellation).ToArray();
        Assert.Equal(2, actions.Length);
        Assert.StartsWith("Hủy ngay · ", Assert.Single(actions, a => a.Payload.AppointmentId == pendingId).Label);
        Assert.StartsWith("Yêu cầu hủy · ", Assert.Single(actions, a => a.Payload.AppointmentId == confirmedId).Label);
    }

    [Fact]
    public async Task Chat_cancel_for_a_day_without_appointments_says_none_was_found_and_links_the_appointments_page()
    {
        var (email, patientId, _) = await RegisterPatientAsync();
        var today = Today();
        await AppointmentAsync(patientId, AppointmentStatus.Confirmed, date: today.AddDays(2));
        var empty = today.AddDays(6);

        using var client = await CreateAuthenticatedClientAsync(email);
        var response = await ChatAsync(client, $"hủy lịch hẹn ngày {empty:dd/MM}", NewSession());
        Assert.Equal("NothingToCancel", response.DialogueOutcome);
        Assert.Contains($"Không tìm thấy lịch hẹn nào phù hợp vào ngày {empty:dd/MM/yyyy}", response.Message);
        Assert.DoesNotContain(response.Actions, a => a.Type == AiActionTypes.RequestCancellation);
        var link = Assert.Single(response.Actions);
        Assert.Equal(AiActionTypes.ViewMyAppointments, link.Type);
        Assert.Equal("/patient/appointments", link.Payload.TargetUrl);
    }

    [Fact]
    public async Task Chat_cancel_of_a_pending_appointment_cancels_it_at_once_and_of_a_confirmed_one_creates_a_request()
    {
        var (email, patientId, _) = await RegisterPatientAsync();
        var today = Today();
        var pending = await AppointmentAsync(patientId, AppointmentStatus.Pending, date: today.AddDays(2));
        var confirmed = await AppointmentAsync(patientId, AppointmentStatus.Confirmed, date: today.AddDays(3));
        using var client = await CreateAuthenticatedClientAsync(email);
        var session = NewSession();

        var pendingPreview = await PrepareCancelAsync(client, session, pending.Appointment.Id);
        Assert.True(pendingPreview.Immediate);
        var pendingResult = await ConfirmAsync(client, session, pendingPreview);
        Assert.Contains("Đã hủy lịch hẹn", pendingResult.DisplayText);
        Assert.True(JsonSerializer.SerializeToElement(pendingResult.Data).GetProperty("cancelled").GetBoolean());

        var confirmedPreview = await PrepareCancelAsync(client, session, confirmed.Appointment.Id);
        Assert.False(confirmedPreview.Immediate);
        var confirmedResult = await ConfirmAsync(client, session, confirmedPreview);
        Assert.Contains("Yêu cầu hủy lịch đã được tạo", confirmedResult.DisplayText);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AppointmentStatus.Cancelled, (await db.Appointments.SingleAsync(a => a.Id == pending.Appointment.Id)).Status);
        Assert.False((await db.AppointmentSlots.SingleAsync(s => s.Id == pending.Slot.Id)).IsBooked);
        Assert.Empty(await db.AppointmentChangeRequests.Where(r => r.AppointmentId == pending.Appointment.Id).ToListAsync());
        Assert.Equal(AppointmentStatus.PendingCancellation, (await db.Appointments.SingleAsync(a => a.Id == confirmed.Appointment.Id)).Status);
        Assert.Single(await db.AppointmentChangeRequests.Where(r => r.AppointmentId == confirmed.Appointment.Id).ToListAsync());
    }

    [Theory]
    [InlineData("hủy lịch có bị mất phí không")]
    [InlineData("hủy hẹn trước mấy tiếng thì không sao")]
    public async Task Cancellation_policy_questions_describe_the_new_rule_with_sources(string message)
    {
        Assert.True(CancellationPolicyAnswer.IsQuestion(message));
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await ChatAsync(client, message, NewSession());
        Assert.NotEqual(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
        Assert.Contains("Chờ xác nhận được hủy ngay", response.Message);
        Assert.Contains("Đã xác nhận", response.Message);
        Assert.Contains("lễ tân duyệt", response.Message);
        Assert.Contains(response.Sources, source => source.DocId == "appointments");
    }

    [Fact]
    public void Appointments_document_states_the_pending_cancellation_rule()
    {
        var text = File.ReadAllText(Path.Combine(MarkdownKnowledgeCatalog.DefaultDirectory, "appointments.md"));
        Assert.Contains("Lịch còn Chờ xác nhận: bấm Hủy lịch", text);
        Assert.Contains("lịch chuyển Đã hủy ngay, khung giờ được giải phóng", text);
        Assert.Contains("Lịch Đã xác nhận: bấm Hủy lịch, nhập lý do rồi Xác nhận gửi yêu cầu", text);
    }

    [Theory]
    [InlineData("huy lich hen ngay mai cua toi", 1)]
    [InlineData("huy lich chieu mai", 1)]
    [InlineData("huy lich hom nay", 0)]
    [InlineData("huy lich ngay kia", 2)]
    public void Cancel_date_reads_relative_days(string text, int days)
    {
        var today = new DateOnly(2026, 11, 26);
        Assert.Equal(today.AddDays(days), AppointmentCancelDate.Parse(text, today, out var invalid));
        Assert.False(invalid);
    }

    [Fact]
    public void Cancel_date_reads_numeric_days_and_rejects_impossible_ones()
    {
        var today = new DateOnly(2026, 11, 26);
        Assert.Equal(new DateOnly(2026, 11, 27), AppointmentCancelDate.Parse("huy lich hen 27/11", today, out _));
        Assert.Equal(new DateOnly(2027, 1, 5), AppointmentCancelDate.Parse("huy lich 5/1", today, out _));
        Assert.Equal(new DateOnly(2026, 12, 1), AppointmentCancelDate.Parse("huy lich ngay 01/12/2026", today, out _));
        Assert.Null(AppointmentCancelDate.Parse("toi muon huy lich kham", today, out var none));
        Assert.False(none);
        Assert.Null(AppointmentCancelDate.Parse("huy lich 31/02", today, out var invalid));
        Assert.True(invalid);
        Assert.Null(AppointmentCancelDate.Parse("huy lich bac si mai", today, out _));
    }

    private DateOnly Today()
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
    }

    private async Task<(Appointment Appointment, AppointmentSlot Slot)> AppointmentAsync(long patientId, AppointmentStatus status, int workingDay = 0, DateOnly? date = null)
    {
        var day = date ?? GetFutureWorkingDate(60 + workingDay);
        var minute = Interlocked.Increment(ref _phone) % 50;
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, day, new TimeOnly(19, minute), new TimeOnly(19, minute).AddMinutes(30));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.AppointmentSlots.SingleAsync(s => s.Id == slot.Id);
        stored.IsBooked = true;
        var appointment = new Appointment
        {
            AppointmentCode = $"APT-CF-{Guid.NewGuid():N}"[..18].ToUpperInvariant(),
            PatientId = patientId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = slot.Id,
            AppointmentDate = day, StartTime = stored.StartTime, EndTime = stored.EndTime,
            Reason = "Kiểm thử hủy lịch hẹn", Status = status
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return (appointment, stored);
    }

    private async Task<(string Email, long PatientId, Guid UserId)> RegisterPatientAsync()
    {
        var email = $"cancel-flow-{Guid.NewGuid():N}@test.com";
        using var registration = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Bệnh nhân kiểm thử hủy lịch", email,
            phoneNumber = $"0876{Interlocked.Increment(ref _phone):D6}", password = "Pass@123"
        });
        Assert.True(registration.IsSuccessStatusCode, await registration.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var patient = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients.SingleAsync(p => p.Email == email);
        return (email, patient.Id, patient.UserId!.Value);
    }

    private async Task<string> OpenDraftAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        var id = Guid.NewGuid().ToString("N");
        Assert.True((await store.TouchSessionAsync("sess_" + id, userId, "draft_" + id, 1, null)).IsAccepted);
        await store.CreateSnapshotAsync(new()
        {
            UserId = userId, SessionId = "sess_" + id, DraftId = "draft_" + id, DraftVersion = 1,
            SpecialtyId = SpecialtyEntityId, DoctorIds = new() { DoctorEntityId }, SlotIds = new() { SlotEntityId }
        });
        return "sess_" + id;
    }

    private static async Task<AiChatResponseDto> ChatAsync(HttpClient client, string message, string sessionId, bool hasOpenBookingWizard = false)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/ai/chat", new { message, sessionId, draftVersion = 1, hasOpenBookingWizard });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }

    private static async Task<(Guid ActionId, string Token, bool Immediate)> PrepareCancelAsync(HttpClient client, string session, long appointmentId)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "patient.prepare_cancel_appointment", toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(new { appointmentId, reason = "Bận việc gia đình đột xuất" }),
            sessionId = session, conversationId = "conv_" + Guid.NewGuid().ToString("N"), idempotencyKey = "idem_" + Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AiToolExecutionResult>())!;
        Assert.Equal("pending_confirmation", body.Status);
        var data = Assert.IsType<JsonElement>(body.Data);
        return (data.GetProperty("actionId").GetGuid(), data.GetProperty("concurrencyToken").GetString()!, data.GetProperty("immediate").GetBoolean());
    }

    private static async Task<AiToolExecutionResult> ConfirmAsync(HttpClient client, string session, (Guid ActionId, string Token, bool Immediate) preview)
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/ai/tool-actions/{preview.ActionId}/confirm",
            new { sessionId = session, concurrencyToken = preview.Token });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<AiToolExecutionResult>())!;
        Assert.Equal("completed", result.Status);
        return result;
    }

    private static string NewSession() => $"sess_{Guid.NewGuid():N}";
}
