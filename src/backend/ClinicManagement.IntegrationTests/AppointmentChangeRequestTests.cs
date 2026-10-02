using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AppointmentChangeRequestTests : IntegrationTestBase
{
    private static int _counter = 50;

    public AppointmentChangeRequestTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<string> GetTokenAsync(string email)
    {
        var loginResponse = await Client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Pass@123" });
        var resStr = await loginResponse.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(resStr);
        return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task<(Appointment app, AppointmentSlot slot)> CreateTestAppointmentAsync(long patientId, AppointmentStatus status, long? doctorId = null)
    {
        var dayOffset = Interlocked.Increment(ref _counter);
        var docId = doctorId ?? DoctorEntityId;
        var date = GetFutureWorkingDate(dayOffset);
        var startTime = new TimeOnly(9, 0, 0);
        var endTime = new TimeOnly(9, 30, 0);

        var slot = await CreateAvailableSlotAsync(docId, date, startTime, endTime);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var slotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
        slotDb.IsBooked = true;

        var appointment = new Appointment
        {
            AppointmentCode = $"APT-CR-{Guid.NewGuid():N}"[..18].ToUpper(),
            PatientId = patientId,
            DoctorId = docId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            AppointmentDate = date,
            StartTime = startTime,
            EndTime = endTime,
            Reason = "Khám ban đầu kiểm thử change requests",
            Status = status
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (appointment, slotDb);
    }

    private async Task<AppointmentSlot> CreateAvailableTargetSlotAsync(long doctorId, int hour, int minute, DayOfWeek? forceDayOfWeek = null, int? customDurationMinutes = null)
    {
        var dayOffset = Interlocked.Increment(ref _counter);
        var date = GetFutureWorkingDate(dayOffset);
        if (forceDayOfWeek.HasValue)
        {
            while (date.DayOfWeek != forceDayOfWeek.Value)
            {
                date = date.AddDays(1);
            }
        }

        var startTime = new TimeOnly(hour, minute, 0);
        var duration = customDurationMinutes ?? 30;
        var endTime = startTime.AddMinutes(duration);

        var slot = await CreateAvailableSlotAsync(doctorId, date, startTime, endTime);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
        slotDb.IsBooked = false;
        await db.SaveChangesAsync();

        return slotDb;
    }

    [Fact]
    public async Task Scenario01_Patient_CanCreate_CancellationRequest_ForPendingOrConfirmedAppointment()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new
        {
            reason = "Bận việc đột xuất cần xin hủy lịch"
        });

        var resStr = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Failed with: {response.StatusCode} - {resStr}");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
        Assert.Equal(AppointmentStatus.PendingCancellation, updatedApp.Status);

        var changeReq = await db.AppointmentChangeRequests.FirstOrDefaultAsync(r => r.AppointmentId == app.Id);
        Assert.NotNull(changeReq);
        Assert.Equal(AppointmentChangeRequestType.Cancellation, changeReq.RequestType);
        Assert.Equal(AppointmentChangeRequestStatus.Pending, changeReq.Status);
        Assert.Equal(AppointmentStatus.Confirmed, changeReq.OriginalAppointmentStatus);

        // Slot remains booked
        var slotInDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
        Assert.True(slotInDb.IsBooked);

        // History recorded
        var history = await db.AppointmentHistories.FirstOrDefaultAsync(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.CancelRequested);
        Assert.NotNull(history);

        // Notification created for receptionist with /reception/change-requests
        var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == ReceptionistId
            && n.RelatedEntityType == "AppointmentChangeRequest"
            && n.RelatedEntityId == changeReq.Id.ToString()
            && n.Route == "/reception/change-requests");
        Assert.NotNull(notif);
        Assert.Equal("/reception/change-requests", notif.Route);
    }

    [Fact]
    public async Task Scenario02_Patient_CanCreate_RescheduleRequest_WithValidFutureSlot()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 14, 0);

        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Muốn đổi sang buổi chiều ngày khác"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
        Assert.Equal(AppointmentStatus.PendingReschedule, updatedApp.Status);

        var changeReq = await db.AppointmentChangeRequests.FirstOrDefaultAsync(r => r.AppointmentId == app.Id);
        Assert.NotNull(changeReq);
        Assert.Equal(AppointmentChangeRequestType.Reschedule, changeReq.RequestType);
        Assert.Equal(targetSlot.Id, changeReq.RequestedSlotId);
        Assert.Equal(AppointmentChangeRequestStatus.Pending, changeReq.Status);
        Assert.Equal(AppointmentStatus.Confirmed, changeReq.OriginalAppointmentStatus);

        // Old slot still booked, target slot still NOT booked yet
        var oldSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
        var targetSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == targetSlot.Id);
        Assert.True(oldSlotDb.IsBooked);
        Assert.False(targetSlotDb.IsBooked);

        // History recorded
        var history = await db.AppointmentHistories.FirstOrDefaultAsync(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.RescheduleRequested);
        Assert.NotNull(history);

        // Notification for receptionist
        var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == ReceptionistId
            && n.RelatedEntityType == "AppointmentChangeRequest"
            && n.RelatedEntityId == changeReq.Id.ToString()
            && n.Route == "/reception/change-requests");
        Assert.NotNull(notif);
    }

    [Fact]
    public async Task Scenario03_Patient_CannotCreate_ChangeRequest_ForAnotherPatientsAppointment()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        // Patient 2 attempts request on Patient 1's appointment
        await AuthenticateAsync("pat2@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new
        {
            reason = "Hủy hộ người khác"
        });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Scenario04_Patient_CannotCreate_ChangeRequest_ForCompletedOrCancelledAppointment()
    {
        var (compApp, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var (cancApp, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Cancelled);

        await AuthenticateAsync("pat1@test.com");
        var res1 = await Client.PostAsJsonAsync($"/api/v1/appointments/{compApp.Id}/cancellation-requests", new { reason = "Hủy lịch đã hoàn tất" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res1.StatusCode);

        var res2 = await Client.PostAsJsonAsync($"/api/v1/appointments/{cancApp.Id}/cancellation-requests", new { reason = "Hủy lịch đã hủy" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res2.StatusCode);
    }

    [Fact]
    public async Task Scenario05_Patient_CannotCreate_DuplicatePendingRequest_Returns409()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        var res1 = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Yêu cầu hủy lần 1" });
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Duplicate request on same appointment while pending
        var res2 = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Yêu cầu hủy lần 2" });
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        var content = await res2.Content.ReadAsStringAsync();
        Assert.Contains("ACTIVE_CHANGE_REQUEST_EXISTS", content);
    }

    [Fact]
    public async Task Scenario06_Reschedule_TargetSlotIsCurrentSlot_Returns422()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = slot.Id,
            reason = "Đổi sang chính slot hiện tại"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var content = await res.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_TARGET", content);
    }

    [Fact]
    public async Task Scenario07_Reschedule_TargetSlotOnSunday_Returns422()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var sundaySlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 10, 0, forceDayOfWeek: DayOfWeek.Sunday);

        await AuthenticateAsync("pat1@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = sundaySlot.Id,
            reason = "Xin đổi sang chủ nhật"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var content = await res.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_SCHEDULE", content);
    }

    [Fact]
    public async Task Scenario08_Reschedule_TargetSlotDifferentDoctor_Returns422()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed, doctorId: DoctorEntityId);
        var doctor2Slot = await CreateAvailableTargetSlotAsync(Doctor2EntityId, 10, 0);

        await AuthenticateAsync("pat1@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = doctor2Slot.Id,
            reason = "Xin đổi sang bác sĩ khác"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var content = await res.Content.ReadAsStringAsync();
        Assert.Contains("DOCTOR_MISMATCH", content);
    }

    [Fact]
    public async Task Scenario09_Reschedule_TargetSlotAlreadyBooked_Returns409()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var bookedTargetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 11, 0);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.AppointmentSlots.FirstAsync(x => x.Id == bookedTargetSlot.Id);
            s.IsBooked = true;
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pat1@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = bookedTargetSlot.Id,
            reason = "Xin đổi sang slot đã kín"
        });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var content = await res.Content.ReadAsStringAsync();
        Assert.Contains("TARGET_SLOT_ALREADY_BOOKED", content);
    }

    [Fact]
    public async Task Scenario10_Reschedule_PatientHasTimeConflict_Returns409()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 11, 30);

        // Create conflicting appointment for patient 1 on the same date and time
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var conflictingSlot = await CreateAvailableSlotAsync(Doctor2EntityId, targetSlot.SlotDate, targetSlot.StartTime, targetSlot.EndTime);
            conflictingSlot.IsBooked = true;
            await db.SaveChangesAsync();

            db.Appointments.Add(new Appointment
            {
                AppointmentCode = $"APT-CONF-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = Patient1EntityId,
                DoctorId = Doctor2EntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = conflictingSlot.Id,
                AppointmentDate = targetSlot.SlotDate,
                StartTime = targetSlot.StartTime,
                EndTime = targetSlot.EndTime,
                Reason = "Lịch trùng giờ",
                Status = AppointmentStatus.Confirmed
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pat1@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Đổi trùng vào giờ đã có lịch khác"
        });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var content = await res.Content.ReadAsStringAsync();
        Assert.Contains("PATIENT_TIME_CONFLICT", content);
    }

    [Fact]
    public async Task Scenario11_Patient_CanWithdraw_PendingRequest_RestoresStatus()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        var res1 = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Xin hủy lịch" });
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id && r.Status == AppointmentChangeRequestStatus.Pending);
            requestId = req.Id;
        }

        // Withdraw
        var withdrawRes = await Client.PostAsync($"/api/v1/appointment-change-requests/{requestId}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, withdrawRes.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.Equal(AppointmentChangeRequestStatus.Withdrawn, req.Status);

            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Confirmed, updatedApp.Status);

            var slotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
            Assert.True(slotDb.IsBooked); // Slot remains held
        }
    }

    [Fact]
    public async Task Scenario12_Patient_CannotWithdraw_ProcessedRequest_Returns409()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Xin hủy" });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        // Withdraw once -> OK
        var res1 = await Client.PostAsync($"/api/v1/appointment-change-requests/{requestId}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Withdraw second time -> 409 Conflict
        var res2 = await Client.PostAsync($"/api/v1/appointment-change-requests/{requestId}/withdraw", null);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        var content = await res2.Content.ReadAsStringAsync();
        Assert.Contains("CHANGE_REQUEST_ALREADY_PROCESSED", content);
    }

    [Fact]
    public async Task Scenario13_Patient_CannotWithdraw_AnotherPatientsRequest()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Xin hủy" });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        // Patient 2 attempts to withdraw Patient 1's request
        await AuthenticateAsync("pat2@test.com");
        var res = await Client.PostAsync($"/api/v1/appointment-change-requests/{requestId}/withdraw", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Scenario14_Patient_GetMyChangeRequests_OnlyReturnsOwnRequests()
    {
        var (app1, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var (app2, _) = await CreateTestAppointmentAsync(Patient2EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app1.Id}/cancellation-requests", new { reason = "Yêu cầu của P1" });

        await AuthenticateAsync("pat2@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app2.Id}/cancellation-requests", new { reason = "Yêu cầu của P2" });

        // Patient 1 queries own requests
        await AuthenticateAsync("pat1@test.com");
        var res = await Client.GetFromJsonAsync<ApiResponse<PagedResult<ChangeRequestDto>>>("/api/v1/appointment-change-requests");

        Assert.NotNull(res);
        Assert.True(res.Success);
        Assert.All(res.Data!.Items, item => Assert.Equal(Patient1Id, item.RequestedByUserId));
        Assert.Contains(res.Data.Items, item => item.AppointmentId == app1.Id);
        Assert.DoesNotContain(res.Data.Items, item => item.AppointmentId == app2.Id);
    }

    [Fact]
    public async Task Scenario15_Receptionist_ApproveCancellation_CancelsAppointmentAndReleasesSlot()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Bệnh nhân bận" });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        // Receptionist approves cancellation
        await AuthenticateAsync("rec@test.com");
        var approveRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-cancellation", new
        {
            reason = "Lễ tân duyệt yêu cầu hủy"
        });

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Cancelled, updatedApp.Status);

            var updatedSlot = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
            Assert.False(updatedSlot.IsBooked); // Released!

            var updatedReq = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.Equal(AppointmentChangeRequestStatus.Approved, updatedReq.Status);

            var history = await db.AppointmentHistories.FirstOrDefaultAsync(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.Cancelled);
            Assert.NotNull(history);

            var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == Patient1Id
                && n.RelatedEntityType == "Appointment"
                && n.RelatedEntityId == app.Id.ToString()
                && n.DedupeKey == $"appt_chg_proc_{requestId}_approved");
            Assert.NotNull(notif);
            Assert.Equal("/patient/appointments", notif.Route);
        }
    }

    [Fact]
    public async Task Scenario16_Receptionist_RejectCancellation_RestoresAppointmentAndKeepsSlot()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Bệnh nhân xin hủy" });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        // Receptionist rejects cancellation
        await AuthenticateAsync("rec@test.com");
        var rejectRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/reject", new
        {
            note = "Sát giờ khám quy định không cho hủy"
        });

        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Confirmed, updatedApp.Status); // Restored!

            var updatedSlot = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
            Assert.True(updatedSlot.IsBooked); // Retained!

            var updatedReq = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.Equal(AppointmentChangeRequestStatus.Rejected, updatedReq.Status);

            var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == Patient1Id
                && n.RelatedEntityType == "Appointment"
                && n.RelatedEntityId == app.Id.ToString()
                && n.DedupeKey == $"appt_chg_proc_{requestId}_rejected");
            Assert.NotNull(notif);
            Assert.Equal("/patient/appointments", notif.Route);
            Assert.Contains("Sát giờ khám quy định không cho hủy", notif.Message);
        }
    }

    [Fact]
    public async Task Scenario17_Receptionist_ApproveReschedule_ReleasesOldSlotAndBooksNewSlot()
    {
        var (app, oldSlot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 10, 30);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin đổi sang ngày khác"
        });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        // Receptionist approves reschedule
        await AuthenticateAsync("rec@test.com");
        var approveRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-reschedule", new
        {
            note = "Lễ tân xác nhận đổi lịch hẹn"
        });

        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Confirmed, updatedApp.Status);
            Assert.Equal(targetSlot.Id, updatedApp.AppointmentSlotId);
            Assert.Equal(targetSlot.SlotDate, updatedApp.AppointmentDate);
            Assert.Equal(targetSlot.StartTime, updatedApp.StartTime);
            Assert.Equal(targetSlot.EndTime, updatedApp.EndTime);

            var oldSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == oldSlot.Id);
            Assert.False(oldSlotDb.IsBooked); // Old slot freed

            var targetSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == targetSlot.Id);
            Assert.True(targetSlotDb.IsBooked); // Target slot booked

            var updatedReq = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.Equal(AppointmentChangeRequestStatus.Approved, updatedReq.Status);

            var history = await db.AppointmentHistories.FirstOrDefaultAsync(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.Rescheduled);
            Assert.NotNull(history);

            var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == Patient1Id
                && n.RelatedEntityType == "Appointment"
                && n.RelatedEntityId == app.Id.ToString()
                && n.DedupeKey == $"appt_chg_proc_{requestId}_approved");
            Assert.NotNull(notif);
            Assert.Equal("/patient/appointments", notif.Route);
        }
    }

    [Fact]
    public async Task Scenario18_Receptionist_RejectReschedule_RestoresAppointmentAndKeepsOldSlot()
    {
        var (app, oldSlot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 8, 30);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin dời lịch"
        });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        // Receptionist rejects reschedule
        await AuthenticateAsync("rec@test.com");
        var rejectRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/reject", new
        {
            note = "Bác sĩ có lịch hội chẩn đột xuất"
        });

        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Confirmed, updatedApp.Status); // Restored!
            Assert.Equal(oldSlot.Id, updatedApp.AppointmentSlotId); // Keeps old slot

            var oldSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == oldSlot.Id);
            Assert.True(oldSlotDb.IsBooked); // Retained!

            var targetSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == targetSlot.Id);
            Assert.False(targetSlotDb.IsBooked); // Untouched!

            var updatedReq = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.Equal(AppointmentChangeRequestStatus.Rejected, updatedReq.Status);
        }
    }

    [Fact]
    public async Task Scenario19_Receptionist_ApproveReschedule_WhenTargetSlotTakenConcurrently_Returns409AndRollsBack()
    {
        var (app, oldSlot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 10, 0);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin dời lịch"
        });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;

            // Simulate concurrent booking on target slot right before receptionist approves
            var s = await db.AppointmentSlots.FirstAsync(x => x.Id == targetSlot.Id);
            s.IsBooked = true;
            await db.SaveChangesAsync();
        }

        // Receptionist attempts approval
        await AuthenticateAsync("rec@test.com");
        var approveRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-reschedule", new
        {
            reason = "Lễ tân thử duyệt"
        });

        Assert.Equal(HttpStatusCode.Conflict, approveRes.StatusCode);
        var content = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("TARGET_SLOT_ALREADY_BOOKED", content);

        // Transaction rolled back: appointment is still PendingReschedule, old slot still booked, request still Pending
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appDb = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.PendingReschedule, appDb.Status);
            Assert.Equal(oldSlot.Id, appDb.AppointmentSlotId);

            var oldSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == oldSlot.Id);
            Assert.True(oldSlotDb.IsBooked);

            var reqDb = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.Equal(AppointmentChangeRequestStatus.Pending, reqDb.Status);
        }
    }

    [Fact]
    public async Task Scenario20_RBAC_PatientAndDoctor_CannotApproveOrReject_Returns403()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Xin hủy" });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        // Patient role calling reception endpoint -> 403 Forbidden
        await AuthenticateAsync("pat1@test.com");
        var resPatient = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-cancellation", new { });
        Assert.Equal(HttpStatusCode.Forbidden, resPatient.StatusCode);

        // Doctor role calling reception endpoint -> 403 Forbidden
        await AuthenticateAsync("doc@test.com");
        var resDoctor = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-cancellation", new { });
        Assert.Equal(HttpStatusCode.Forbidden, resDoctor.StatusCode);

        // Anonymous -> 401 Unauthorized
        Client.DefaultRequestHeaders.Authorization = null;
        var resAnon = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-cancellation", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, resAnon.StatusCode);
    }

    [Fact]
    public async Task Scenario21_CreateRequest_WhitespaceOrShortReason_ReturnsBadRequest()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 15, 0);

        await AuthenticateAsync("pat1@test.com");

        // Cancellation with whitespace reason
        var resCancelSpace = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new
        {
            reason = "   "
        });
        Assert.Equal(HttpStatusCode.BadRequest, resCancelSpace.StatusCode);

        // Cancellation with too short reason (< 5 chars)
        var resCancelShort = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new
        {
            reason = "abc"
        });
        Assert.Equal(HttpStatusCode.BadRequest, resCancelShort.StatusCode);

        // Reschedule with whitespace reason
        var resReschedSpace = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "    "
        });
        Assert.Equal(HttpStatusCode.BadRequest, resReschedSpace.StatusCode);

        // Reschedule with too short reason (< 5 chars)
        var resReschedShort = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "1234"
        });
        Assert.Equal(HttpStatusCode.BadRequest, resReschedShort.StatusCode);
    }

    [Fact]
    public async Task Scenario22_RejectReschedule_InitiallyPendingAppointment_RestoresPendingStatus()
    {
        // Start appointment in Pending status
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Pending);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 16, 0);

        await AuthenticateAsync("pat1@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Đổi lịch khi đang pending"
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
            Assert.Equal(AppointmentStatus.Pending, req.OriginalAppointmentStatus);
        }

        // Receptionist rejects request
        await AuthenticateAsync("rec@test.com");
        var rejectRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/reject", new
        {
            note = "Từ chối dời lịch của lịch hẹn chờ duyệt"
        });
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Pending, updatedApp.Status); // Correctly restored to Pending!
            Assert.Equal(slot.Id, updatedApp.AppointmentSlotId);

            var slotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
            Assert.True(slotDb.IsBooked);
        }
    }

    [Fact]
    public async Task Scenario23_WithdrawRequest_InitiallyPendingAppointment_RestoresPendingStatus()
    {
        // Start appointment in Pending status
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Pending);

        await AuthenticateAsync("pat1@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new
        {
            reason = "Yêu cầu hủy lịch hẹn pending"
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
            Assert.Equal(AppointmentStatus.Pending, req.OriginalAppointmentStatus);
        }

        // Patient withdraws
        var withdrawRes = await Client.PostAsync($"/api/v1/appointment-change-requests/{requestId}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, withdrawRes.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Pending, updatedApp.Status); // Correctly restored to Pending!

            var slotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
            Assert.True(slotDb.IsBooked);
        }
    }

    [Fact]
    public async Task Scenario24_ApproveReschedule_AlteringTargetSlot_Returns422()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 13, 0);
        var differentSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 13, 30);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin đổi sang slot A"
        });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        // Receptionist attempts to approve with a different slot ID
        await AuthenticateAsync("rec@test.com");
        var approveRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-reschedule", new
        {
            newSlotId = differentSlot.Id,
            note = "Cố tình thay đổi sang slot khác"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, approveRes.StatusCode);
        var content = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_TARGET", content);
    }

    [Fact]
    public async Task Scenario25_ApproveReschedule_DoctorOnApprovedLeave_Returns422()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 15, 30);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin đổi sang ca chiều"
        });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;

            // Simulate doctor approved leave covering the target slot time
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest
            {
                DoctorId = DoctorEntityId,
                StartDateTime = targetSlot.SlotDate.ToDateTime(targetSlot.StartTime),
                EndDateTime = targetSlot.SlotDate.ToDateTime(targetSlot.EndTime),
                Reason = "Bác sĩ nghỉ phép đột xuất",
                Status = DoctorLeaveRequestStatus.Approved
            });
            await db.SaveChangesAsync();
        }

        // Receptionist attempts approval
        await AuthenticateAsync("rec@test.com");
        var approveRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-reschedule", new
        {
            note = "Duyệt dời lịch"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, approveRes.StatusCode);
        var content = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("DOCTOR_NOT_AVAILABLE", content);
    }

    [Fact]
    public async Task Scenario26_ApproveReschedule_DoctorScheduleDeactivated_Returns422()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 16, 30);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin đổi sang cuối chiều"
        });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;

            // Deactivate the schedule for this doctor and date
            var schedules = await db.DoctorWorkSchedules
                .Where(ws => ws.DoctorId == DoctorEntityId && ws.WorkDate == targetSlot.SlotDate)
                .ToListAsync();
            foreach (var s in schedules) s.IsActive = false;
            await db.SaveChangesAsync();
        }

        // Receptionist attempts approval
        await AuthenticateAsync("rec@test.com");
        var approveRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-reschedule", new
        {
            note = "Duyệt dời lịch"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, approveRes.StatusCode);
        var content = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("DOCTOR_NOT_AVAILABLE", content);
    }

    [Fact]
    public async Task Scenario27_ApproveReschedule_PatientTimeConflict_Returns409()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 17, 0);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin dời sang 17:00"
        });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;

            // In the meantime, patient booked another appointment at that exact date and time with Doctor 2
            var slot2 = await CreateAvailableSlotAsync(Doctor2EntityId, targetSlot.SlotDate, targetSlot.StartTime, targetSlot.EndTime);
            slot2.IsBooked = true;
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = $"APT-CONFL-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = Patient1EntityId,
                DoctorId = Doctor2EntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = slot2.Id,
                AppointmentDate = targetSlot.SlotDate,
                StartTime = targetSlot.StartTime,
                EndTime = targetSlot.EndTime,
                Reason = "Lịch khám khác",
                Status = AppointmentStatus.Confirmed
            });
            await db.SaveChangesAsync();
        }

        // Receptionist attempts approval
        await AuthenticateAsync("rec@test.com");
        var approveRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-reschedule", new
        {
            note = "Duyệt dời lịch"
        });

        Assert.Equal(HttpStatusCode.Conflict, approveRes.StatusCode);
        var content = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("PATIENT_TIME_CONFLICT", content);
    }

    [Fact]
    public async Task Scenario28_ApproveReschedule_CompletedOrCancelledPatientAppointmentDoesNotBlock()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 17, 30);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin dời sang 17:30"
        });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;

            // Patient has another appointment at that time, but it was CANCELLED
            var slot2 = await CreateAvailableSlotAsync(Doctor2EntityId, targetSlot.SlotDate, targetSlot.StartTime, targetSlot.EndTime);
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = $"APT-CANC-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = Patient1EntityId,
                DoctorId = Doctor2EntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = slot2.Id,
                AppointmentDate = targetSlot.SlotDate,
                StartTime = targetSlot.StartTime,
                EndTime = targetSlot.EndTime,
                Reason = "Lịch đã hủy",
                Status = AppointmentStatus.Cancelled
            });
            await db.SaveChangesAsync();
        }

        // Receptionist attempts approval -> Should SUCCEED!
        await AuthenticateAsync("rec@test.com");
        var approveRes = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-reschedule", new
        {
            note = "Duyệt dời lịch thành công"
        });

        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);
    }

    [Fact]
    public async Task Scenario29_Concurrent_CreateRequest_SameAppointment_ExactlyOneSucceeds()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        var token = await GetTokenAsync("pat1@test.com");

        var client1 = Factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client2 = Factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var task1 = client1.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Hủy lịch đồng thời luồng 1" });
        var task2 = client2.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Hủy lịch đồng thời luồng 2" });

        var responses = await Task.WhenAll(task1, task2);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        if (okCount != 1 || conflictCount != 1)
        {
            var r0 = await responses[0].Content.ReadAsStringAsync();
            var r1 = await responses[1].Content.ReadAsStringAsync();
            throw new Exception($"Scenario29 assertion failed: okCount={okCount}, conflictCount={conflictCount}. R0: [{responses[0].StatusCode}] {r0} | R1: [{responses[1].StatusCode}] {r1}");
        }
        Assert.Equal(1, okCount);
        Assert.Equal(1, conflictCount);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Exactly 1 change request created for this appointment
            var reqs = await db.AppointmentChangeRequests.Where(r => r.AppointmentId == app.Id).ToListAsync();
            Assert.Single(reqs);
            var req = reqs[0];
            Assert.Equal(AppointmentChangeRequestStatus.Pending, req.Status);
            Assert.Equal(AppointmentChangeRequestType.Cancellation, req.RequestType);

            // Appointment in PendingCancellation status
            var appDb = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.PendingCancellation, appDb.Status);

            // Exactly 1 cancel-requested history entry, no duplicates
            var requestHistories = await db.AppointmentHistories
                .Where(h => h.AppointmentId == app.Id && (h.Action == AppointmentHistoryAction.CancelRequested || h.Action == AppointmentHistoryAction.RescheduleRequested))
                .ToListAsync();
            Assert.Single(requestHistories);
            Assert.Equal(AppointmentHistoryAction.CancelRequested, requestHistories[0].Action);

            // Notifications sent to receptionists
            var notifs = await db.Notifications
                .Where(n => n.RelatedEntityType == "AppointmentChangeRequest" && n.RelatedEntityId == req.Id.ToString())
                .ToListAsync();
            Assert.NotEmpty(notifs);
            Assert.All(notifs, n =>
            {
                Assert.Equal(NotificationType.AppointmentChangeRequest, n.Type);
                Assert.Equal("/reception/change-requests", n.Route);
                Assert.StartsWith($"chg_req_cancel_{req.Id}_", n.DedupeKey);
            });

            // Ensure distinct DedupeKeys (no duplicate notifications with identical keys)
            var dedupeKeys = notifs.Select(n => n.DedupeKey).ToList();
            Assert.Equal(dedupeKeys.Count, dedupeKeys.Distinct().Count());
        }
    }

    [Fact]
    public async Task Scenario30_Concurrent_ApproveAndApprove_ExactlyOneSucceeds()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Hủy lịch để test duyệt đồng thời" });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        var token = await GetTokenAsync("rec@test.com");

        var client1 = Factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client2 = Factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var task1 = client1.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-cancellation", new { note = "Duyệt luồng 1" });
        var task2 = client2.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-cancellation", new { note = "Duyệt luồng 2" });

        var responses = await Task.WhenAll(task1, task2);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        if (okCount != 1 || conflictCount != 1)
        {
            var r0 = await responses[0].Content.ReadAsStringAsync();
            var r1 = await responses[1].Content.ReadAsStringAsync();
            throw new Exception($"Scenario30 assertion failed: okCount={okCount}, conflictCount={conflictCount}. R0: [{responses[0].StatusCode}] {r0} | R1: [{responses[1].StatusCode}] {r1}");
        }
        Assert.Equal(1, okCount);
        Assert.Equal(1, conflictCount);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Request status Approved
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.Equal(AppointmentChangeRequestStatus.Approved, req.Status);
            Assert.NotNull(req.ProcessedAt);
            Assert.Equal(ReceptionistId, req.ProcessedByUserId);

            // Appointment status Cancelled
            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Cancelled, updatedApp.Status);

            // Slot is released
            var slotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
            Assert.False(slotDb.IsBooked);

            // Exactly 1 Cancelled history entry across the appointment
            var cancelHistories = await db.AppointmentHistories
                .Where(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.Cancelled)
                .ToListAsync();
            Assert.Single(cancelHistories);
            Assert.Equal(ReceptionistId, cancelHistories[0].PerformedByUserId);

            // No conflicting Confirmed histories created after the request
            var conflictingHistories = await db.AppointmentHistories
                .Where(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.Confirmed && h.CreatedAt > req.CreatedAt)
                .ToListAsync();
            Assert.Empty(conflictingHistories);

            // Exactly 1 approved notification for this request
            var approvedNotifs = await db.Notifications
                .Where(n => n.DedupeKey == $"appt_chg_proc_{requestId}_approved")
                .ToListAsync();
            Assert.Single(approvedNotifs);
            Assert.Equal(Patient1Id, approvedNotifs[0].UserId);
            Assert.Equal(NotificationType.AppointmentChangeRequest, approvedNotifs[0].Type);
            Assert.Equal("Appointment", approvedNotifs[0].RelatedEntityType);
            Assert.Equal(app.Id.ToString(), approvedNotifs[0].RelatedEntityId);
            Assert.Equal("/patient/appointments", approvedNotifs[0].Route);

            // Zero conflicting rejected notifications
            var rejectedNotifs = await db.Notifications
                .Where(n => n.DedupeKey == $"appt_chg_proc_{requestId}_rejected")
                .ToListAsync();
            Assert.Empty(rejectedNotifs);
        }
    }

    [Fact]
    public async Task Scenario31_Concurrent_ApproveAndReject_ExactlyOneSucceeds()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Hủy lịch test approve vs reject" });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        var token = await GetTokenAsync("rec@test.com");

        var client1 = Factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client2 = Factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var task1 = client1.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-cancellation", new { note = "Duyệt nhanh" });
        var task2 = client2.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/reject", new { note = "Từ chối nhanh" });

        var responses = await Task.WhenAll(task1, task2);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        if (okCount != 1 || conflictCount != 1)
        {
            var r0 = await responses[0].Content.ReadAsStringAsync();
            var r1 = await responses[1].Content.ReadAsStringAsync();
            throw new Exception($"Scenario31 assertion failed: okCount={okCount}, conflictCount={conflictCount}. R0: [{responses[0].StatusCode}] {r0} | R1: [{responses[1].StatusCode}] {r1}");
        }
        Assert.Equal(1, okCount);
        Assert.Equal(1, conflictCount);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.True(req.Status == AppointmentChangeRequestStatus.Approved || req.Status == AppointmentChangeRequestStatus.Rejected);
            Assert.NotNull(req.ProcessedAt);
            Assert.Equal(ReceptionistId, req.ProcessedByUserId);

            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            var slotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);

            var approvedNotifs = await db.Notifications.Where(n => n.DedupeKey == $"appt_chg_proc_{requestId}_approved").ToListAsync();
            var rejectedNotifs = await db.Notifications.Where(n => n.DedupeKey == $"appt_chg_proc_{requestId}_rejected").ToListAsync();

            var cancelHistories = await db.AppointmentHistories
                .Where(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.Cancelled)
                .ToListAsync();
            var confirmedHistoriesAfterRequest = await db.AppointmentHistories
                .Where(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.Confirmed && h.CreatedAt > req.CreatedAt)
                .ToListAsync();

            if (req.Status == AppointmentChangeRequestStatus.Approved)
            {
                // Approved outcome
                Assert.Equal(AppointmentStatus.Cancelled, updatedApp.Status);
                Assert.False(slotDb.IsBooked);

                // History: exactly 1 Cancelled history, 0 Confirmed histories
                Assert.Single(cancelHistories);
                Assert.Empty(confirmedHistoriesAfterRequest);

                // Notifications: exactly 1 Approved notification, 0 Rejected notifications
                Assert.Single(approvedNotifs);
                Assert.Empty(rejectedNotifs);
                Assert.Equal(Patient1Id, approvedNotifs[0].UserId);
                Assert.Equal(NotificationType.AppointmentChangeRequest, approvedNotifs[0].Type);
                Assert.Equal("/patient/appointments", approvedNotifs[0].Route);
            }
            else
            {
                // Rejected outcome
                Assert.Equal(AppointmentStatus.Confirmed, updatedApp.Status);
                Assert.True(slotDb.IsBooked);

                // History: exactly 1 Confirmed history, 0 Cancelled histories
                Assert.Single(confirmedHistoriesAfterRequest);
                Assert.Empty(cancelHistories);

                // Notifications: exactly 1 Rejected notification, 0 Approved notifications
                Assert.Single(rejectedNotifs);
                Assert.Empty(approvedNotifs);
                Assert.Equal(Patient1Id, rejectedNotifs[0].UserId);
                Assert.Equal(NotificationType.AppointmentChangeRequest, rejectedNotifs[0].Type);
                Assert.Equal("/patient/appointments", rejectedNotifs[0].Route);
            }
        }
    }

    [Fact]
    public async Task Scenario32_Concurrent_RejectAndWithdraw_ExactlyOneSucceeds()
    {
        var (app, slot) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);

        await AuthenticateAsync("pat1@test.com");
        await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/cancellation-requests", new { reason = "Hủy lịch test reject vs withdraw" });

        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.AppointmentId == app.Id);
            requestId = req.Id;
        }

        var recToken = await GetTokenAsync("rec@test.com");
        var patToken = await GetTokenAsync("pat1@test.com");

        var recClient = Factory.CreateClient();
        recClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", recToken);

        var patClient = Factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patToken);

        var task1 = recClient.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/reject", new { note = "Lễ tân từ chối" });
        var task2 = patClient.PostAsync($"/api/v1/appointment-change-requests/{requestId}/withdraw", null);

        var responses = await Task.WhenAll(task1, task2);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        if (okCount != 1 || conflictCount != 1)
        {
            var r0 = await responses[0].Content.ReadAsStringAsync();
            var r1 = await responses[1].Content.ReadAsStringAsync();
            throw new Exception($"Scenario32 assertion failed: okCount={okCount}, conflictCount={conflictCount}. R0: [{responses[0].StatusCode}] {r0} | R1: [{responses[1].StatusCode}] {r1}");
        }
        Assert.Equal(1, okCount);
        Assert.Equal(1, conflictCount);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var req = await db.AppointmentChangeRequests.FirstAsync(r => r.Id == requestId);
            Assert.True(req.Status == AppointmentChangeRequestStatus.Rejected || req.Status == AppointmentChangeRequestStatus.Withdrawn);

            var updatedApp = await db.Appointments.FirstAsync(a => a.Id == app.Id);
            Assert.Equal(AppointmentStatus.Confirmed, updatedApp.Status);

            var slotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
            Assert.True(slotDb.IsBooked);

            // Never approved: 0 approved notifications and 0 cancelled histories
            var approvedNotifs = await db.Notifications.Where(n => n.DedupeKey == $"appt_chg_proc_{requestId}_approved").ToListAsync();
            Assert.Empty(approvedNotifs);
            var cancelHistories = await db.AppointmentHistories.Where(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.Cancelled).ToListAsync();
            Assert.Empty(cancelHistories);

            var rejectedNotifs = await db.Notifications.Where(n => n.DedupeKey == $"appt_chg_proc_{requestId}_rejected").ToListAsync();

            // Terminal histories created after the initial CancelRequested
            var terminalHistories = await db.AppointmentHistories
                .Where(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.Confirmed && h.CreatedAt > req.CreatedAt)
                .ToListAsync();
            Assert.Single(terminalHistories);

            if (req.Status == AppointmentChangeRequestStatus.Rejected)
            {
                // Receptionist reject won
                Assert.Equal(ReceptionistId, req.ProcessedByUserId);
                Assert.Single(rejectedNotifs);
                Assert.Equal(Patient1Id, rejectedNotifs[0].UserId);
                Assert.Equal(NotificationType.AppointmentChangeRequest, rejectedNotifs[0].Type);
                Assert.Equal("/patient/appointments", rejectedNotifs[0].Route);
                Assert.Equal(ReceptionistId, terminalHistories[0].PerformedByUserId);
            }
            else
            {
                // Patient withdraw won
                // Per service policy: Withdraw does NOT send notification to patient
                Assert.Empty(rejectedNotifs);
                Assert.Equal(Patient1Id, terminalHistories[0].PerformedByUserId);
            }
        }
    }

    [Fact]
    public async Task Scenario33_GetChangeRequests_PaginationClampedAndEnumValidation()
    {
        await AuthenticateAsync("rec@test.com");

        // Negative page clamped to 1, pageSize 200 clamped to 100
        var resClamped = await Client.GetAsync("/api/v1/reception/change-requests?page=-5&pageSize=200");
        Assert.Equal(HttpStatusCode.OK, resClamped.StatusCode);
        var clampedData = await resClamped.Content.ReadFromJsonAsync<ApiResponse<PagedResult<ChangeRequestDto>>>();
        Assert.NotNull(clampedData);
        Assert.Equal(1, clampedData.Data!.Page);
        Assert.Equal(100, clampedData.Data.PageSize);

        // Invalid requestType -> 400 Bad Request
        var resInvalidType = await Client.GetAsync("/api/v1/reception/change-requests?requestType=NonExistentType");
        Assert.Equal(HttpStatusCode.BadRequest, resInvalidType.StatusCode);

        // Invalid status -> 400 Bad Request
        var resInvalidStatus = await Client.GetAsync("/api/v1/reception/change-requests?status=InvalidStatus");
        Assert.Equal(HttpStatusCode.BadRequest, resInvalidStatus.StatusCode);
    }
}
