using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

/// <summary>S6: the doctor "Tiếp nhận" button creates the same visit and queue number as reception check-in.</summary>
public sealed class DoctorCheckInVisitTests : IntegrationTestBase
{
    private static int _minute = 300;

    public DoctorCheckInVisitTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Doctor_checkin_of_confirmed_today_appointment_creates_one_waiting_visit_and_is_idempotent()
    {
        var appointment = await AppointmentAsync(DoctorEntityId, 0);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");

        var first = await CheckInAsync(doctor, appointment.Id);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.True(first.Body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(first.Body.GetProperty("message").GetString()));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.AsNoTracking().SingleAsync(x => x.AppointmentId == appointment.Id);
            Assert.True(visit.QueueNumber > 0);
            Assert.Equal(appointment.AppointmentDate, visit.VisitDate);
            Assert.Equal(VisitStatus.WaitingForDoctor, visit.Status);
            Assert.Equal(VisitArrivalType.Scheduled, visit.ArrivalType);
            Assert.Equal(DoctorEntityId, visit.AssignedDoctorId);
            Assert.Equal(appointment.FacilityId, visit.FacilityId);
            Assert.Equal(DoctorId, visit.CreatedByUserId);
            Assert.Equal(AppointmentStatus.CheckedIn, (await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id)).Status);
            var history = Assert.Single(await db.AppointmentHistories.AsNoTracking().Where(x => x.AppointmentId == appointment.Id).ToListAsync());
            Assert.Equal(AppointmentHistoryAction.CheckedIn, history.Action);
            Assert.Equal(AppointmentStatus.Confirmed, history.OldStatus);
            Assert.Equal(DoctorId, history.PerformedByUserId);
            var data = first.Body.GetProperty("data");
            Assert.Equal(visit.Id, data.GetProperty("visitId").GetInt64());
            Assert.Equal(visit.QueueNumber, data.GetProperty("queueNumber").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("queueDisplay").GetString()));
        }

        var second = await CheckInAsync(doctor, appointment.Id);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Body.GetProperty("data").GetProperty("queueNumber").GetInt32(), second.Body.GetProperty("data").GetProperty("queueNumber").GetInt32());
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.PatientVisits.CountAsync(x => x.AppointmentId == appointment.Id));
            Assert.Equal(1, await db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.Id));
            Assert.Equal(1, await db.Notifications.CountAsync(x => x.DedupeKey == $"checkin_appointment_{appointment.Id}"));
        }
    }

    [Fact]
    public async Task Doctor_checkin_keeps_ownership_and_appointment_day_rules_without_creating_visits()
    {
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var own = await AppointmentAsync(DoctorEntityId, 0);
        Assert.Equal(HttpStatusCode.OK, (await CheckInAsync(doctor, own.Id)).Status);
        Assert.True(await VisitExistsAsync(own.Id));

        var foreign = await AppointmentAsync(Doctor2EntityId, 0);
        var notOwned = await CheckInAsync(doctor, foreign.Id);
        Assert.Equal(HttpStatusCode.NotFound, notOwned.Status);
        Assert.False(await VisitExistsAsync(foreign.Id));
        await AssertUnchangedAsync(foreign.Id);

        foreach (var offset in new[] { 1, -1 })
        {
            var otherDay = await AppointmentAsync(DoctorEntityId, offset);
            var blocked = await CheckInAsync(doctor, otherDay.Id);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.Status);
            Assert.Equal("CHECKIN_NOT_TODAY", blocked.Body.GetProperty("errorCode").GetString());
            Assert.False(await VisitExistsAsync(otherDay.Id));
            await AssertUnchangedAsync(otherDay.Id);
        }

        var pending = await AppointmentAsync(DoctorEntityId, 0, AppointmentStatus.Pending);
        var notConfirmed = await CheckInAsync(doctor, pending.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, notConfirmed.Status);
        Assert.Equal("INVALID_STATE_TRANSITION", notConfirmed.Body.GetProperty("errorCode").GetString());
        Assert.False(await VisitExistsAsync(pending.Id));
    }

    [Fact]
    public async Task Doctor_checked_in_patient_appears_in_reception_and_doctor_queues()
    {
        var appointment = await AppointmentAsync(DoctorEntityId, 0);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        Assert.Equal(HttpStatusCode.OK, (await CheckInAsync(doctor, appointment.Id)).Status);
        string visitCode;
        long departmentId;
        using (var scope = Factory.Services.CreateScope())
        {
            var visit = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.AsNoTracking().SingleAsync(x => x.AppointmentId == appointment.Id);
            visitCode = visit.VisitCode;
            departmentId = visit.DepartmentId;
        }

        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        var page = await reception.GetAsync($"/api/v1/patient-visits/department-queue?departmentId={departmentId}");
        var pageJson = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, pageJson);
        Assert.Contains(JsonDocument.Parse(pageJson).RootElement.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("visitCode").GetString() == visitCode && item.GetProperty("appointmentId").GetInt64() == appointment.Id);

        Assert.Contains(visitCode, await CopilotCardAsync(reception, "receptionist.queue", "reception.get_queue"));
        Assert.Contains(visitCode, await CopilotCardAsync(doctor, "doctor.my_queue", "doctor.get_my_queue"));
    }

    private static async Task<string> CopilotCardAsync(HttpClient client, string code, string tool)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "x", suggestionCode = code, sessionId = $"sess_dci_{Guid.NewGuid():N}" });
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, json);
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
        Assert.Equal(new[] { tool }, data.GetProperty("executedToolNames").EnumerateArray().Select(x => x.GetString()));
        return Assert.Single(data.GetProperty("cards").EnumerateArray()).GetProperty("data").GetRawText();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> CheckInAsync(HttpClient client, long appointmentId)
    {
        var response = await client.PostAsync($"/api/v1/doctor/appointments/{appointmentId}/check-in", null);
        var json = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrWhiteSpace(json) ? default : JsonDocument.Parse(json).RootElement.Clone());
    }

    private async Task<bool> VisitExistsAsync(long appointmentId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.AnyAsync(x => x.AppointmentId == appointmentId);
    }

    private async Task AssertUnchangedAsync(long appointmentId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointmentId)).Status);
        Assert.False(await db.AppointmentHistories.AnyAsync(x => x.AppointmentId == appointmentId));
    }

    private async Task<Appointment> AppointmentAsync(long doctorId, int dayOffset, AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var date = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday.AddDays(dayOffset);
        var department = await db.Departments.FirstAsync(x => x.SpecialtyId == SpecialtyEntityId && x.IsActive);
        var start = new TimeOnly(0, 0).AddMinutes(Interlocked.Increment(ref _minute));
        var appointment = new Appointment
        {
            AppointmentCode = "DCI-" + Guid.NewGuid().ToString("N")[..12], PatientId = Patient1EntityId, DoctorId = doctorId,
            SpecialtyId = SpecialtyEntityId, FacilityId = department.FacilityId, AppointmentDate = date,
            StartTime = start, EndTime = start.AddMinutes(1), Status = status, Reason = "Khám định kỳ",
            AppointmentSlot = new AppointmentSlot { DoctorId = doctorId, SlotDate = date, StartTime = start, EndTime = start.AddMinutes(1), IsBooked = true }
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }
}
