using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Api.Controllers;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class ShortDisplayCodeLookupTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Public_lookup_accepts_both_codes_and_rejects_wrong_phone_without_revealing_code_existence()
    {
        var appointment = await AddAppointmentAsync("LH1025", Patient1EntityId);
        using var client = Factory.CreateClient();
        async Task<HttpResponseMessage> Lookup(string code, string phone) => await client.GetAsync(
            $"/api/v1/appointments/lookup?query={Uri.EscapeDataString(code)}&phone={phone}");

        foreach (var code in new[] { "lh 1025", appointment.AppointmentCode.ToLowerInvariant() })
        {
            using var response = await Lookup(code, "0123456784");
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<ApiResponse<List<AppointmentLookupDto>>>();
            Assert.Equal("LH1025", Assert.Single(data!.Data!).AppointmentCode);
        }

        using var wrong = await Lookup("LH1025", "0123456785");
        using var missing = await Lookup("LH9999", "0123456785");
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        for (var i = 0; i < 3; i++)
        {
            using var failed = await Lookup("lh 1025", "0123456785");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }
        using var throttled = await Lookup("LH1025", "0123456784");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    [Fact]
    public async Task Lookup_counts_failures_per_ip_and_per_normalized_code_and_reopens_after_lockout()
    {
        var clock = new LookupClock();
        var attempts = new AppointmentLookupAttempts(clock);
        for (var i = 0; i < 5; i++)
            Assert.Empty((await attempts.RunAsync($"ip-{i}", i % 2 == 0 ? "lh 12" : "LH0012", () => Task.FromResult(new List<int>())))!);
        Assert.Null(await attempts.RunAsync("other-ip", "LH0012", () => Task.FromResult(new List<int> { 1 })));
        for (var i = 0; i < 5; i++)
            Assert.Empty((await attempts.RunAsync("ip-fixed", $"LH{i + 20}", () => Task.FromResult(new List<int>())))!);
        Assert.Null(await attempts.RunAsync("ip-fixed", "LH99", () => Task.FromResult(new List<int> { 1 })));
        clock.Utc = clock.Utc.AddMinutes(15);
        Assert.Equal([1], await attempts.RunAsync("ip-fixed", "lh 12", () => Task.FromResult(new List<int> { 1 })));
    }

    [Fact]
    public async Task Chat_reads_and_cancels_using_both_codes_within_the_signed_in_patients_appointments()
    {
        var first = await AddAppointmentAsync("LH1031", Patient1EntityId);
        var second = await AddAppointmentAsync("LH1032", Patient1EntityId);
        var foreign = await AddAppointmentAsync("LH1033", Patient2EntityId);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        async Task<AiChatResponseDto> Chat(string message)
        {
            using var response = await patient.PostAsJsonAsync("/api/v1/ai/chat", new { message, sessionId = $"sess_{Guid.NewGuid():N}" });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
        }

        foreach (var code in new[] { "lh 1031", first.AppointmentCode })
        {
            var detail = Assert.Single((await Chat($"tra cứu lịch hẹn mã {code}")).ToolResults);
            Assert.Equal("appointment_detail", detail.ResultType);
            Assert.Equal("LH1031", Assert.IsType<JsonElement>(detail.Data).GetProperty("appointmentCode").GetString());
        }

        foreach (var code in new[] { foreign.ShortCode, foreign.AppointmentCode })
        {
            Assert.Empty((await Chat($"tra lịch mã {code}")).ToolResults);
            Assert.DoesNotContain((await Chat($"hủy lịch hẹn mã {code}")).Actions, action => action.Type == AiActionTypes.RequestCancellation);
        }

        foreach (var (row, code) in new[] { (first, "lh 1031"), (second, second.AppointmentCode) })
        {
            var result = await Chat($"hủy lịch hẹn mã {code}");
            var action = Assert.Single(result.Actions, action => action.Type == AiActionTypes.RequestCancellation);
            Assert.Equal(row.Id, action.Payload.AppointmentId);
            Assert.Equal(row.ShortCode, action.Payload.AppointmentCode);
            using var cancelled = await patient.PostAsJsonAsync($"/api/v1/appointments/{action.Payload.AppointmentId}/cancel",
                new { reason = "Bận việc đột xuất" });
            cancelled.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Audit_finds_old_and_new_events_by_either_code()
    {
        var appointment = await AddAppointmentAsync("LH1040", Patient1EntityId);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SystemAuditLogs.AddRange(
            new SystemAuditLog { EntityName = "Appointment", EntityId = appointment.Id.ToString(), Action = "CreateAppointment",
                Description = $"Đặt lịch {appointment.AppointmentCode}", CreatedAt = DateTime.UtcNow, UserId = Patient1Id },
            new SystemAuditLog { EntityName = "Appointment", EntityId = appointment.Id.ToString(), Action = "ConfirmAppointment",
                Description = "Xác nhận lịch LH1040", CreatedAt = DateTime.UtcNow, UserId = ReceptionistId });
        await db.SaveChangesAsync();
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        foreach (var code in new[] { "lh 1040", appointment.AppointmentCode.ToLowerInvariant() })
        {
            using var response = await admin.GetAsync($"/api/v1/admin/audit-logs?code={Uri.EscapeDataString(code)}");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<SystemAuditLogDto>>>();
            Assert.Equal(2, body!.Data!.TotalItems);
            Assert.All(body.Data.Items, log => Assert.Equal("LH1040", log.ReferenceCode));
            Assert.Contains(body.Data.Items, log => log.Description == $"Đặt lịch {appointment.AppointmentCode}");
        }
    }

    private async Task<Appointment> AddAppointmentAsync(string code, long patientId)
    {
        var date = GetFutureWorkingDate(20 + int.Parse(code[^2..]));
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(9, 0), new TimeOnly(9, 30));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = new Appointment
        {
            ShortCode = code, AppointmentCode = "APT-261128-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            PatientId = patientId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id, AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime,
            Status = AppointmentStatus.Pending
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    private sealed class LookupClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
}
