using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

    private async Task<AppointmentSlot> CreateAvailableTargetSlotAsync(long doctorId, int hour, int minute, DayOfWeek? forceDayOfWeek = null)
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
        var endTime = startTime.AddMinutes(30);

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

        // Slot remains booked
        var slotInDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
        Assert.True(slotInDb.IsBooked);

        // History recorded
        var history = await db.AppointmentHistories.FirstOrDefaultAsync(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.CancelRequested);
        Assert.NotNull(history);

        // Notification created for receptionist with /reception/change-requests
        var notif = await db.Notifications.FirstOrDefaultAsync(n => n.RelatedEntityId == changeReq.Id.ToString() && n.Type == NotificationType.AppointmentChangeRequest);
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

        // Old slot still booked, target slot still NOT booked yet
        var oldSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == slot.Id);
        var targetSlotDb = await db.AppointmentSlots.FirstAsync(s => s.Id == targetSlot.Id);
        Assert.True(oldSlotDb.IsBooked);
        Assert.False(targetSlotDb.IsBooked);

        // History recorded
        var history = await db.AppointmentHistories.FirstOrDefaultAsync(h => h.AppointmentId == app.Id && h.Action == AppointmentHistoryAction.RescheduleRequested);
        Assert.NotNull(history);
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
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 11, 0);

        // Pre-book the target slot
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.AppointmentSlots.FirstAsync(x => x.Id == targetSlot.Id);
            s.IsBooked = true;
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pat1@test.com");
        var res = await Client.PostAsJsonAsync($"/api/v1/appointments/{app.Id}/reschedule-requests", new
        {
            requestedSlotId = targetSlot.Id,
            reason = "Xin đổi sang slot đã kín"
        });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var content = await res.Content.ReadAsStringAsync();
        Assert.Contains("TARGET_SLOT_ALREADY_BOOKED", content);
    }

    [Fact]
    public async Task Scenario10_Reschedule_PatientTimeConflict_Returns409()
    {
        var (app, _) = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Confirmed);
        var targetSlot = await CreateAvailableTargetSlotAsync(DoctorEntityId, 15, 0);

        // Create another appointment for same patient at overlapping time
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
    public async Task Scenario12_Patient_CannotWithdraw_ProcessedRequest_Returns422()
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

        // Withdraw once
        var res1 = await Client.PostAsync($"/api/v1/appointment-change-requests/{requestId}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Withdraw second time -> 422
        var res2 = await Client.PostAsync($"/api/v1/appointment-change-requests/{requestId}/withdraw", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res2.StatusCode);
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

            var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == Patient1Id && n.DedupeKey == $"appt_chg_proc_{requestId}_approved");
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
            reason = "Sát giờ khám quy định không cho hủy"
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

            var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == Patient1Id && n.DedupeKey == $"appt_chg_proc_{requestId}_rejected");
            Assert.NotNull(notif);
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
            reason = "Lễ tân xác nhận đổi lịch hẹn"
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

            var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == Patient1Id && n.DedupeKey == $"appt_chg_proc_{requestId}_approved");
            Assert.NotNull(notif);
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
            reason = "Bác sĩ có lịch hội chẩn đột xuất"
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
}
