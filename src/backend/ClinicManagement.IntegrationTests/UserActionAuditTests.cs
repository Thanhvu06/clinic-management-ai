using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class UserActionAuditTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static int _dayOffset = 40;

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    private static async Task OkAsync(HttpResponseMessage response) =>
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

    private async Task<SystemAuditLog> AssertAuditAsync(string action, string entityName, long entityId, Guid userId, DateTime since)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.SystemAuditLogs.AsNoTracking()
            .SingleAsync(l => l.Action == action && l.EntityName == entityName && l.EntityId == entityId.ToString());
        Assert.Equal(userId, log.UserId);
        Assert.InRange(log.CreatedAt, since.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
        Assert.False(string.IsNullOrWhiteSpace(log.Description));
        return log;
    }

    private async Task<(long Id, string Code)> BookAsync(int dayOffset)
    {
        var date = GetFutureWorkingDate(dayOffset);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(16, 10), new TimeOnly(16, 40));
        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var created = await DataAsync(await patient.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = slot.Id, Reason = "Khám sức khỏe định kỳ cho nhật ký"
        }));
        return (created.GetProperty("id").GetInt64(), created.GetProperty("appointmentCode").GetString()!);
    }

    private async Task<long> ChangeRequestIdAsync(long appointmentId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AppointmentChangeRequests.Where(r => r.AppointmentId == appointmentId).OrderByDescending(r => r.Id).Select(r => r.Id).FirstAsync();
    }

    [Fact]
    public async Task Booking_confirming_checking_in_starting_and_completing_each_write_an_attributed_audit_entry()
    {
        var since = DateTime.UtcNow;
        var (appointmentId, code) = await BookAsync(Interlocked.Increment(ref _dayOffset));
        var created = await AssertAuditAsync(UserActionAudit.CreateAppointment, "Appointment", appointmentId, Patient1Id, since);
        Assert.Contains(code, created.Description);

        await MoveAppointmentToVietnamTodayAsync(appointmentId);
        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        Assert.Equal(HttpStatusCode.OK, (await reception.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null)).StatusCode);
        await AssertAuditAsync(UserActionAudit.ConfirmAppointment, "Appointment", appointmentId, ReceptionistId, since);

        var ticket = await DataAsync(await reception.PostAsJsonAsync("/api/v1/patient-visits/check-in-appointment", new { appointmentId }));
        var checkIn = await AssertAuditAsync(UserActionAudit.CheckInPatient, "Appointment", appointmentId, ReceptionistId, since);
        var visitId = ticket.GetProperty("visitId").GetInt64();
        string visitCode;
        using (var scope = Factory.Services.CreateScope())
            visitCode = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.Where(v => v.Id == visitId).Select(v => v.VisitCode).SingleAsync();
        Assert.Contains(visitCode, checkIn.Description);

        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsync($"/api/v1/doctor/visits/{visitId}/start-consultation", null)).StatusCode);
        var started = await AssertAuditAsync(UserActionAudit.StartConsultation, "PatientVisit", visitId, DoctorId, since);
        Assert.Contains(visitCode, started.Description);

        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/complete",
            new CompleteConsultationRequest { Diagnosis = "Khỏe mạnh", Summary = "Sức khỏe ổn định, không cần điều trị" })).StatusCode);
        await AssertAuditAsync("COMPLETE_CONSULTATION", "PatientVisit", visitId, DoctorId, since);
    }

    [Fact]
    public async Task Starting_a_consultation_from_the_appointment_is_audited_once()
    {
        var since = DateTime.UtcNow;
        var (appointmentId, _) = await BookAsync(Interlocked.Increment(ref _dayOffset));
        await MoveAppointmentToVietnamTodayAsync(appointmentId);
        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        Assert.Equal(HttpStatusCode.OK, (await reception.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null)).StatusCode);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null)).StatusCode);
        await AssertAuditAsync(UserActionAudit.CheckInPatient, "Appointment", appointmentId, DoctorId, since);
        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/start-consultation", null)).StatusCode);
        await AssertAuditAsync(UserActionAudit.StartConsultation, "Appointment", appointmentId, DoctorId, since);
    }

    [Fact]
    public async Task Cancellation_request_and_its_approval_are_audited_with_their_actors()
    {
        var since = DateTime.UtcNow;
        var (appointmentId, _) = await BookAsync(Interlocked.Increment(ref _dayOffset));
        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        await OkAsync(await patient.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/cancellation-requests", new { reason = "Bận việc đột xuất cần hủy lịch" }));
        await AssertAuditAsync(UserActionAudit.RequestAppointmentCancellation, "Appointment", appointmentId, Patient1Id, since);

        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        await OkAsync(await reception.PostAsJsonAsync($"/api/v1/reception/change-requests/{await ChangeRequestIdAsync(appointmentId)}/approve-cancellation", new { reason = "Lễ tân duyệt hủy" }));
        await AssertAuditAsync(UserActionAudit.CancelAppointment, "Appointment", appointmentId, ReceptionistId, since);
    }

    [Fact]
    public async Task Reschedule_request_and_its_approval_are_audited_with_their_actors()
    {
        var since = DateTime.UtcNow;
        var (appointmentId, _) = await BookAsync(Interlocked.Increment(ref _dayOffset));
        var target = await CreateAvailableSlotAsync(DoctorEntityId, GetFutureWorkingDate(Interlocked.Increment(ref _dayOffset)), new TimeOnly(17, 10), new TimeOnly(17, 40));
        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        await OkAsync(await patient.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/reschedule-requests", new { requestedSlotId = target.Id, reason = "Muốn đổi sang ngày khác cho tiện" }));
        await AssertAuditAsync(UserActionAudit.RequestAppointmentReschedule, "Appointment", appointmentId, Patient1Id, since);

        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        await OkAsync(await reception.PostAsJsonAsync($"/api/v1/reception/change-requests/{await ChangeRequestIdAsync(appointmentId)}/approve-reschedule", new { note = "Lễ tân xác nhận đổi lịch" }));
        var rescheduled = await AssertAuditAsync(UserActionAudit.RescheduleAppointment, "Appointment", appointmentId, ReceptionistId, since);
        Assert.Contains(target.SlotDate.ToString("dd/MM/yyyy"), rescheduled.Description);
    }

    private async Task<long> CreateLeaveAsync(HttpClient doctor, int hour)
    {
        var day = DateTime.UtcNow.AddDays(Interlocked.Increment(ref _dayOffset) + 120).ToString("yyyy-MM-dd");
        var leave = await DataAsync(await doctor.PostAsJsonAsync("/api/v1/doctor/leave-requests", new
        {
            startDateTime = $"{day}T{hour:D2}:00:00", endDateTime = $"{day}T{hour:D2}:30:00", reason = "Việc gia đình cần nghỉ"
        }));
        return leave.GetProperty("id").GetInt64();
    }

    [Fact]
    public async Task Leave_requests_created_withdrawn_approved_and_rejected_are_audited()
    {
        var since = DateTime.UtcNow;
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var withdrawn = await CreateLeaveAsync(doctor, 1);
        var created = await AssertAuditAsync(UserActionAudit.CreateLeaveRequest, "DoctorLeaveRequest", withdrawn, DoctorId, since);
        Assert.Contains($"NP{withdrawn}", created.Description);
        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsync($"/api/v1/doctor/leave-requests/{withdrawn}/withdraw", null)).StatusCode);
        await AssertAuditAsync(UserActionAudit.WithdrawLeaveRequest, "DoctorLeaveRequest", withdrawn, DoctorId, since);

        var approved = await CreateLeaveAsync(doctor, 2);
        var rejected = await CreateLeaveAsync(doctor, 3);
        var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/admin/leave-requests/{approved}/approve", new { adminNote = "Đồng ý" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/admin/leave-requests/{rejected}/reject", new { adminNote = "Thiếu người trực" })).StatusCode);
        await AssertAuditAsync(UserActionAudit.ApproveLeaveRequest, "DoctorLeaveRequest", approved, AdminId, since);
        await AssertAuditAsync(UserActionAudit.RejectLeaveRequest, "DoctorLeaveRequest", rejected, AdminId, since);
    }

    [Fact]
    public async Task Admin_audit_log_api_returns_user_actions_with_role_and_reference_code()
    {
        var (appointmentId, code) = await BookAsync(Interlocked.Increment(ref _dayOffset));
        var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var page = await DataAsync(await admin.GetAsync($"/api/v1/admin/audit-logs?action={UserActionAudit.CreateAppointment}&pageSize=100"));
        var row = page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("entityId").GetString() == appointmentId.ToString());
        Assert.Equal("Patient", row.GetProperty("userRole").GetString());
        Assert.Equal(code, row.GetProperty("referenceCode").GetString());
        Assert.NotEqual("Hệ thống", row.GetProperty("userFullName").GetString());

        var recent = await DataAsync(await admin.GetAsync("/api/v1/admin/audit-logs?page=1&pageSize=8"));
        Assert.Contains(recent.GetProperty("items").EnumerateArray(), r => r.GetProperty("action").GetString() == UserActionAudit.CreateAppointment);
    }
}
