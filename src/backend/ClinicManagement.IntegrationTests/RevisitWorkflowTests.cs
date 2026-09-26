using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class RevisitWorkflowTests : IntegrationTestBase
{
    public RevisitWorkflowTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<long> CreatePendingRevisitRequestAsync(long patientId, DateOnly suggestedDate)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceSlot = await db.AppointmentSlots.AsNoTracking()
            .FirstAsync(slot => slot.Id == SlotEntityId);

        var originalAppointment = new Appointment
        {
            AppointmentCode = $"APT-{Guid.NewGuid():N}"[..18].ToUpper(),
            PatientId = patientId,
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = sourceSlot.Id,
            AppointmentDate = sourceSlot.SlotDate,
            StartTime = sourceSlot.StartTime,
            EndTime = sourceSlot.EndTime,
            Reason = "Lịch khám gốc đã hoàn tất để kiểm thử tái khám",
            Status = AppointmentStatus.Completed
        };

        db.Appointments.Add(originalAppointment);
        await db.SaveChangesAsync();

        var request = new RevisitRequest
        {
            AppointmentId = originalAppointment.Id,
            PatientId = patientId,
            DoctorId = DoctorEntityId,
            SuggestedDate = suggestedDate,
            Note = "Tái khám để đánh giá đáp ứng điều trị",
            Status = RevisitRequestStatus.PendingPatientResponse
        };

        db.RevisitRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task Given_CompletedOwnAppointment_When_DoctorCreatesRevisit_Then_NotificationUsesRealRequestIdAndValidRoute()
    {
        long appointmentId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sourceSlot = await db.AppointmentSlots.AsNoTracking()
                .FirstAsync(slot => slot.Id == SlotEntityId);
            var appointment = new Appointment
            {
                AppointmentCode = $"APT-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = sourceSlot.Id,
                AppointmentDate = sourceSlot.SlotDate,
                StartTime = sourceSlot.StartTime,
                EndTime = sourceSlot.EndTime,
                Reason = "Ca khám hoàn tất cần được theo dõi tái khám",
                Status = AppointmentStatus.Completed
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            appointmentId = appointment.Id;
        }

        await AuthenticateAsync("doc@test.com");
        var suggestedDate = GetFutureWorkingDate(40);
        var response = await Client.PostAsJsonAsync(
            $"/api/v1/doctor/appointments/{appointmentId}/revisit-requests",
            new
            {
                suggestedDate = suggestedDate.ToString("yyyy-MM-dd"),
                note = "Tái khám để đánh giá đáp ứng điều trị"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestId = body.RootElement.GetProperty("data").GetProperty("id").GetInt64();
        Assert.True(requestId > 0);
        Assert.Equal(
            SpecialtyEntityId,
            body.RootElement.GetProperty("data").GetProperty("specialtyId").GetInt64());

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = await verifyDb.Notifications.AsNoTracking()
            .FirstAsync(item =>
                item.RelatedEntityType == "RevisitRequest" &&
                item.RelatedEntityId == requestId.ToString());

        Assert.Equal("/patient/revisit", notification.Route);
        Assert.Equal($"revisit_req_{requestId}", notification.DedupeKey);
        Assert.NotEqual("0", notification.RelatedEntityId);
    }

    [Fact]
    public async Task Given_PendingRevisit_When_OwnerSelectsValidSlot_Then_CreatesLinkedAppointmentAtomically()
    {
        var date = GetFutureWorkingDate(41);
        var targetSlot = await CreateAvailableSlotAsync(
            DoctorEntityId,
            date,
            new TimeOnly(10, 0),
            new TimeOnly(10, 30));
        var requestId = await CreatePendingRevisitRequestAsync(Patient1EntityId, date);

        await AuthenticateAsync("pat1@test.com");

        var detailResponse = await Client.GetAsync($"/api/v1/revisit-requests/{requestId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailBody = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            SpecialtyEntityId,
            detailBody.RootElement.GetProperty("data").GetProperty("specialtyId").GetInt64());

        var response = await Client.PostAsJsonAsync($"/api/v1/revisit-requests/{requestId}/accept", new
        {
            targetSlotId = targetSlot.Id,
            reason = "Tái khám theo chỉ định để đánh giá tiến triển"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var appointmentData = body.RootElement.GetProperty("data");
        var newAppointmentId = appointmentData.GetProperty("id").GetInt64();
        Assert.Equal("Pending", appointmentData.GetProperty("status").GetString());
        Assert.Equal(targetSlot.Id, appointmentData.GetProperty("appointmentSlotId").GetInt64());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var revisitRequest = await db.RevisitRequests.AsNoTracking()
            .FirstAsync(request => request.Id == requestId);
        Assert.Equal(RevisitRequestStatus.Accepted, revisitRequest.Status);
        Assert.Equal(newAppointmentId, revisitRequest.NewAppointmentId);

        var appointment = await db.Appointments.AsNoTracking()
            .FirstAsync(item => item.Id == newAppointmentId);
        Assert.Equal(Patient1EntityId, appointment.PatientId);
        Assert.Equal(DoctorEntityId, appointment.DoctorId);
        Assert.Equal(SpecialtyEntityId, appointment.SpecialtyId);

        var slot = await db.AppointmentSlots.AsNoTracking()
            .FirstAsync(item => item.Id == targetSlot.Id);
        Assert.True(slot.IsBooked);

        Assert.True(await db.AppointmentHistories.AsNoTracking()
            .AnyAsync(history => history.AppointmentId == newAppointmentId));

        Assert.True(await db.Notifications.AsNoTracking()
            .AnyAsync(notification =>
                notification.UserId == Patient1Id &&
                notification.RelatedEntityId == newAppointmentId.ToString() &&
                notification.Route == "/patient/appointments"));

        Assert.True(await db.Notifications.AsNoTracking()
            .AnyAsync(notification =>
                notification.UserId == ReceptionistId &&
                notification.RelatedEntityId == newAppointmentId.ToString() &&
                notification.Route == "/reception/appointments"));
    }

    [Fact]
    public async Task Given_AnotherPatientsRevisit_When_PatientAccepts_Then_ReturnsNotFoundAndDoesNotBookSlot()
    {
        var date = GetFutureWorkingDate(42);
        var targetSlot = await CreateAvailableSlotAsync(
            DoctorEntityId,
            date,
            new TimeOnly(11, 0),
            new TimeOnly(11, 30));
        var requestId = await CreatePendingRevisitRequestAsync(Patient1EntityId, date);

        await AuthenticateAsync("pat2@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/revisit-requests/{requestId}/accept", new
        {
            targetSlotId = targetSlot.Id,
            reason = "Không được phép nhận đề xuất của bệnh nhân khác"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slot = await db.AppointmentSlots.AsNoTracking()
            .FirstAsync(item => item.Id == targetSlot.Id);
        Assert.False(slot.IsBooked);
    }

    [Fact]
    public async Task Given_BookedRevisitSlot_When_PatientAccepts_Then_ReturnsConflictAndKeepsRequestPending()
    {
        var date = GetFutureWorkingDate(43);
        var targetSlot = await CreateAvailableSlotAsync(
            DoctorEntityId,
            date,
            new TimeOnly(12, 0),
            new TimeOnly(12, 30));
        var requestId = await CreatePendingRevisitRequestAsync(Patient1EntityId, date);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var slot = await db.AppointmentSlots.FirstAsync(item => item.Id == targetSlot.Id);
            slot.IsBooked = true;
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/revisit-requests/{requestId}/accept", new
        {
            targetSlotId = targetSlot.Id,
            reason = "Tái khám theo đề xuất của bác sĩ điều trị"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "SLOT_ALREADY_BOOKED",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await verifyDb.RevisitRequests.AsNoTracking()
            .FirstAsync(item => item.Id == requestId);
        Assert.Equal(RevisitRequestStatus.PendingPatientResponse, request.Status);
        Assert.Null(request.NewAppointmentId);
    }
}
