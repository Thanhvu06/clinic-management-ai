using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class AuditRound2AuthorizationTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static int _day = 500;

    private async Task<Facility> FacilityAsync(bool assigned)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = new Facility { Code = $"R2-{Guid.NewGuid():N}"[..18], Name = "Round 2 facility", IsActive = true };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = DoctorId, FacilityId = facility.Id, Role = "Doctor", IsActive = true });
        if (assigned) db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = ReceptionistId, FacilityId = facility.Id, Role = "Receptionist", IsActive = true });
        await db.SaveChangesAsync();
        return facility;
    }

    private async Task<Appointment> AppointmentAsync(long? facility, bool fromVisit = false, AppointmentStatus status = AppointmentStatus.Confirmed, DateOnly? date = null)
    {
        var day = date ?? GetFutureWorkingDate(Interlocked.Increment(ref _day));
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, day, new(9, 0), new(9, 30));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = new Appointment { AppointmentCode = $"R2-{Guid.NewGuid():N}"[..18], FacilityId = fromVisit ? null : facility, PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = slot.Id, AppointmentDate = day, StartTime = slot.StartTime, EndTime = slot.EndTime, Reason = "Khám sức khỏe vòng audit hai", Status = status };
        db.Appointments.Add(appointment);
        (await db.AppointmentSlots.FindAsync(slot.Id))!.IsBooked = true;
        await db.SaveChangesAsync();
        if (fromVisit)
        {
            var department = new Department { FacilityId = facility!.Value, Code = $"R2D-{Guid.NewGuid():N}"[..18], Name = "Audit department", IsActive = true };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            db.PatientVisits.Add(new PatientVisit { VisitCode = $"R2V-{Guid.NewGuid():N}"[..18], AppointmentId = appointment.Id, PatientId = Patient1EntityId, FacilityId = facility.Value, DepartmentId = department.Id, VisitDate = day, QueueNumber = 1, Status = VisitStatus.WaitingForDoctor, CreatedByUserId = ReceptionistId, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        db.AppointmentHistories.Add(new AppointmentHistory { AppointmentId = appointment.Id, Action = AppointmentHistoryAction.Created, NewStatus = status, Note = "Round 2 history", PerformedByUserId = Patient1Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return appointment;
    }

    private async Task<AppointmentChangeRequest> RequestAsync(long? facility, bool fromVisit, string operation = "detail")
    {
        var reschedule = operation == "approve-reschedule";
        var appointment = await AppointmentAsync(facility, fromVisit, reschedule ? AppointmentStatus.PendingReschedule : AppointmentStatus.PendingCancellation);
        long? target = null;
        if (reschedule)
            target = (await CreateAvailableSlotAsync(DoctorEntityId, GetFutureWorkingDate(Interlocked.Increment(ref _day)), new(10, 0), new(10, 30))).Id;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = new AppointmentChangeRequest { AppointmentId = appointment.Id, RequestType = reschedule ? AppointmentChangeRequestType.Reschedule : AppointmentChangeRequestType.Cancellation, RequestedSlotId = target, Reason = "Yêu cầu kiểm tra audit", Status = AppointmentChangeRequestStatus.Pending, OriginalAppointmentStatus = AppointmentStatus.Confirmed, RequestedByUserId = Patient1Id, CreatedAt = DateTime.UtcNow };
        db.AppointmentChangeRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static Task<HttpResponseMessage> OperateAsync(HttpClient client, long id, string operation) => operation == "detail"
        ? client.GetAsync($"/api/v1/reception/change-requests/{id}")
        : client.PostAsJsonAsync($"/api/v1/reception/change-requests/{id}/{operation}", new { note = "Xử lý yêu cầu audit" });

    [Theory]
    [InlineData("detail", false)] [InlineData("detail", true)]
    [InlineData("approve-cancellation", false)] [InlineData("approve-cancellation", true)]
    [InlineData("approve-reschedule", false)] [InlineData("approve-reschedule", true)]
    [InlineData("reject", false)] [InlineData("reject", true)]
    public async Task R1_Reception_operations_enforce_appointment_then_visit_facility(string operation, bool fromVisit)
    {
        var own = await FacilityAsync(true);
        var other = await FacilityAsync(false);
        var denied = await RequestAsync(other.Id, fromVisit, operation);
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        var response = await OperateAsync(rec, denied.Id, operation);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("ACCESS_DENIED_TO_FACILITY_RESOURCE", await response.Content.ReadAsStringAsync());
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(AppointmentChangeRequestStatus.Pending, (await db.AppointmentChangeRequests.FindAsync(denied.Id))!.Status);
        }
        Assert.Equal(HttpStatusCode.OK, (await OperateAsync(rec, (await RequestAsync(own.Id, fromVisit, operation)).Id, operation)).StatusCode);
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        Assert.Equal(HttpStatusCode.OK, (await OperateAsync(admin, denied.Id, operation)).StatusCode);
    }

    [Fact]
    public async Task R1_Lists_and_pending_stats_scope_requests_and_preserve_unknown_facility()
    {
        var own = await FacilityAsync(true);
        var other = await FacilityAsync(false);
        var accessible = new[] { await RequestAsync(own.Id, false), await RequestAsync(own.Id, true), await RequestAsync(null, false) };
        var outside = new[] { await RequestAsync(other.Id, false), await RequestAsync(other.Id, true) };
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        var list = JsonDocument.Parse(await (await rec.GetAsync("/api/v1/reception/change-requests?pageSize=100")).Content.ReadAsStringAsync());
        var ids = list.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
        foreach (var request in accessible) Assert.Contains(request.Id, ids);
        foreach (var request in outside) Assert.DoesNotContain(request.Id, ids);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expected = await db.AppointmentChangeRequests.CountAsync(c => c.Status == AppointmentChangeRequestStatus.Pending && (c.Appointment!.FacilityId == own.Id || (c.Appointment.FacilityId == null && (c.Appointment.PatientVisit == null || c.Appointment.PatientVisit.FacilityId == own.Id))));
        var stats = JsonDocument.Parse(await (await rec.GetAsync($"/api/v1/reception/stats?facilityId={own.Id}")).Content.ReadAsStringAsync());
        Assert.Equal(expected, stats.RootElement.GetProperty("data").GetProperty("pendingChangeRequests").GetInt32());
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var all = JsonDocument.Parse(await (await admin.GetAsync("/api/v1/reception/change-requests?pageSize=100")).Content.ReadAsStringAsync());
        foreach (var request in outside) Assert.Contains(request.Id, all.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()));
        var adminStats = JsonDocument.Parse(await (await admin.GetAsync("/api/v1/reception/stats")).Content.ReadAsStringAsync());
        Assert.Equal(await db.AppointmentChangeRequests.CountAsync(c => c.Status == AppointmentChangeRequestStatus.Pending), adminStats.RootElement.GetProperty("data").GetProperty("pendingChangeRequests").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await OperateAsync(rec, accessible[2].Id, "detail")).StatusCode);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task R1_Patient_creation_notifies_only_active_reception_assignments_or_all_for_unknown(bool unknown, bool fromVisit)
    {
        var own = await FacilityAsync(true);
        var other = await FacilityAsync(false);
        Guid outsideId, inactiveId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            async Task<Guid> Receptionist(long facility, bool activeAssignment)
            {
                var email = $"r2-{Guid.NewGuid():N}@test.com";
                var user = new ApplicationUser { UserName = email, Email = email, PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", FullName = "Audit receptionist", IsActive = true };
                Assert.True((await manager.CreateAsync(user, "Pass@123")).Succeeded);
                Assert.True((await manager.AddToRoleAsync(user, "Receptionist")).Succeeded);
                db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = user.Id, FacilityId = facility, Role = "Receptionist", IsActive = activeAssignment });
                await db.SaveChangesAsync();
                return user.Id;
            }
            outsideId = await Receptionist(other.Id, true);
            inactiveId = await Receptionist(own.Id, false);
        }
        var appointment = await AppointmentAsync(unknown ? null : own.Id, fromVisit);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await patient.PostAsJsonAsync($"/api/v1/appointments/{appointment.Id}/cancellation-requests", new { reason = "Bận việc cần hủy lịch khám" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetInt64();
        using var verify = Factory.Services.CreateScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var notified = await dbVerify.Notifications.Where(n => n.Type == NotificationType.AppointmentChangeRequest && n.RelatedEntityId == id.ToString()).Select(n => n.UserId).ToListAsync();
        Assert.Contains(ReceptionistId, notified);
        Assert.Equal(unknown, notified.Contains(outsideId));
        Assert.Equal(unknown, notified.Contains(inactiveId));
    }

    [Fact]
    public async Task R2_Appointment_history_enforces_facility_and_admin_access()
    {
        var own = await AppointmentAsync((await FacilityAsync(true)).Id);
        var other = await AppointmentAsync((await FacilityAsync(false)).Id);
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await rec.GetAsync($"/api/v1/reception/appointments/{other.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rec.GetAsync($"/api/v1/reception/appointments/{own.Id}/history")).StatusCode);
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/reception/appointments/{other.Id}/history")).StatusCode);
    }
}
