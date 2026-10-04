using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class AuditRound3Tests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory), IDisposable
{
    private readonly List<long> _facilities = [];
    private readonly List<Guid> _users = [];

    public void Dispose()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var facility in db.Facilities.Where(x => _facilities.Contains(x.Id))) facility.IsActive = false;
        foreach (var user in db.Users.Where(x => _users.Contains(x.Id))) user.IsActive = false;
        db.SaveChanges();
    }

    [Theory]
    [InlineData(null, VisitStatus.InConsultation, false)]
    [InlineData(DiagnosticOrderStatus.Ordered, VisitStatus.WaitingForDiagnostics, false)]
    [InlineData(DiagnosticOrderStatus.Completed, VisitStatus.ResultsReady, false)]
    [InlineData(null, VisitStatus.InConsultation, true)]
    [InlineData(DiagnosticOrderStatus.Completed, VisitStatus.ResultsReady, true)]
    public async Task R3_1_Cancel_restores_visit_from_remaining_orders(DiagnosticOrderStatus? other, VisitStatus expected, bool appointmentLink)
    {
        var visit = await VisitAsync(VisitStatus.WaitingForDiagnostics, appointmentLink);
        var order = await OrderAsync(visit, DiagnosticOrderStatus.Ordered, appointmentLink);
        if (other.HasValue) await OrderAsync(visit, other.Value, appointmentLink);
        using var client = await CreateAuthenticatedClientAsync("doc@test.com");
        var response = await client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}/cancel", new { reason = "Kiểm thử hủy chỉ định" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var actual = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.AsNoTracking().SingleAsync(x => x.Id == visit.Id);
        Assert.Equal(expected, actual.Status);
        Assert.True(actual.UpdatedAtUtc > visit.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(VisitStatus.Completed)]
    [InlineData(VisitStatus.Cancelled)]
    public async Task R3_1_Cancel_does_not_reopen_terminal_visit(VisitStatus status)
    {
        var visit = await VisitAsync(status);
        var order = await OrderAsync(visit, DiagnosticOrderStatus.Ordered);
        using var client = await CreateAuthenticatedClientAsync("doc@test.com");
        var response = await client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}/cancel", new { reason = "Kiểm thử trạng thái kết thúc" });
        response.EnsureSuccessStatusCode();
        using var scope = Factory.Services.CreateScope();
        var actual = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.AsNoTracking().SingleAsync(x => x.Id == visit.Id);
        Assert.Equal(status, actual.Status);
        Assert.Equal(visit.UpdatedAtUtc, actual.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(VisitStatus.Cancelled, VisitStatus.Cancelled)]
    [InlineData(VisitStatus.Completed, VisitStatus.Completed)]
    [InlineData(VisitStatus.InConsultation, VisitStatus.InConsultation)]
    [InlineData(VisitStatus.WaitingForDiagnostics, VisitStatus.ResultsReady)]
    public async Task R3_2_Complete_only_advances_waiting_visit(VisitStatus initial, VisitStatus expected)
    {
        var visit = await VisitAsync(initial);
        var order = await OrderAsync(visit, DiagnosticOrderStatus.InProgress);
        await CompleteAsync(order.Id);
        using var scope = Factory.Services.CreateScope();
        var actual = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.AsNoTracking().SingleAsync(x => x.Id == visit.Id);
        Assert.Equal(expected, actual.Status);
        if (initial != VisitStatus.WaitingForDiagnostics) Assert.Equal(visit.UpdatedAtUtc, actual.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R3_2_Complete_keeps_waiting_while_a_linked_order_is_pending(bool legacyLink)
    {
        var visit = await VisitAsync(VisitStatus.WaitingForDiagnostics, legacyLink);
        var order = await OrderAsync(visit, DiagnosticOrderStatus.InProgress, legacyLink);
        await OrderAsync(visit, DiagnosticOrderStatus.Ordered, legacyLink);
        await CompleteAsync(order.Id);
        using var scope = Factory.Services.CreateScope();
        Assert.Equal(VisitStatus.WaitingForDiagnostics, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.AsNoTracking().SingleAsync(x => x.Id == visit.Id)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R3_3_Doctor_notification_uses_the_existing_examination_route(bool appointment)
    {
        var visit = await VisitAsync(VisitStatus.WaitingForDiagnostics, appointment);
        var order = await OrderAsync(visit, DiagnosticOrderStatus.InProgress, appointment);
        await CompleteAsync(order.Id);
        using var scope = Factory.Services.CreateScope();
        var notification = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.SingleAsync(x => x.DedupeKey == $"diag_completed_doc_{order.Id}_{DoctorId}");
        Assert.Equal(appointment ? $"/doctor/appointments/{visit.AppointmentId}/examination" : $"/doctor/visits/{visit.Id}/examination", notification.Route);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R3_3_Patient_creation_notification_uses_the_registered_results_route(bool appointment)
    {
        var visit = await VisitAsync(VisitStatus.InConsultation, appointment);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var serviceId = await db.DiagnosticServices.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        using var client = await CreateAuthenticatedClientAsync("doc@test.com");
        var route = appointment ? $"/api/v1/doctor/appointments/{visit.AppointmentId}/diagnostic-orders" : $"/api/v1/doctor/visits/{visit.Id}/diagnostic-orders";
        var response = await client.PostAsJsonAsync(route, new { clinicalIndication = "Kiểm thử thông báo", serviceIds = new[] { serviceId }, facilityId = visit.FacilityId });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetInt64();
        var notification = await db.Notifications.AsNoTracking().SingleAsync(x => x.UserId == Patient1Id && x.RelatedEntityType == "DiagnosticOrder" && x.RelatedEntityId == id.ToString());
        Assert.Equal("/patient/diagnostic-results", notification.Route);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R3_4_Revisit_notifies_only_facility_receptionists_or_all_for_legacy(bool legacy)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = await FacilityAsync(db);
        var otherFacility = await FacilityAsync(db);
        var eligible = await ReceptionistAsync(scope.ServiceProvider, db, facility.Id, "Receptionist", true);
        var elsewhere = await ReceptionistAsync(scope.ServiceProvider, db, otherFacility.Id, "Receptionist", true);
        var inactive = await ReceptionistAsync(scope.ServiceProvider, db, facility.Id, "Receptionist", false);
        var wrongRole = await ReceptionistAsync(scope.ServiceProvider, db, facility.Id, "Doctor", true);
        var (requestId, slotId) = await RevisitAsync(db, legacy ? null : facility.Id);
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await client.PostAsJsonAsync($"/api/v1/revisit-requests/{requestId}/accept", new { targetSlotId = slotId, reason = "Tái khám kiểm thử đúng cơ sở" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var recipients = await db.Notifications.Where(x => x.DedupeKey != null && x.DedupeKey.StartsWith($"revisit_booked_rec_{requestId}_")).Select(x => x.UserId).ToListAsync();
        Assert.Contains(eligible, recipients);
        Assert.Equal(legacy, recipients.Contains(elsewhere));
        Assert.Equal(legacy, recipients.Contains(inactive));
        Assert.Equal(legacy, recipients.Contains(wrongRole));
    }

    [Fact]
    public async Task R3_4_Inactive_doctor_account_cannot_accept_revisit_or_book_slot()
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (requestId, slotId) = await RevisitAsync(db, null);
        var doctor = await db.Users.SingleAsync(x => x.Id == DoctorId);
        doctor.IsActive = false;
        await db.SaveChangesAsync();
        try
        {
            var response = await client.PostAsJsonAsync($"/api/v1/revisit-requests/{requestId}/accept", new { targetSlotId = slotId, reason = "Kiểm thử tài khoản bác sĩ khóa" });
            Assert.False(response.IsSuccessStatusCode);
            Assert.Contains("DOCTOR_NOT_AVAILABLE", await response.Content.ReadAsStringAsync());
            db.ChangeTracker.Clear();
            Assert.False((await db.AppointmentSlots.SingleAsync(x => x.Id == slotId)).IsBooked);
            Assert.Equal(RevisitRequestStatus.PendingPatientResponse, (await db.RevisitRequests.SingleAsync(x => x.Id == requestId)).Status);
        }
        finally
        {
            doctor = await db.Users.SingleAsync(x => x.Id == DoctorId);
            doctor.IsActive = true;
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("Pharmacist", true)]
    [InlineData("Doctor", false)]
    public async Task R3_6_Doctors_optional_facility_filter_preserves_unfiltered_catalog(string role, bool active)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = await FacilityAsync(db);
        db.StaffFacilityAssignments.Add(new() { UserId = DoctorId, FacilityId = facility.Id, Role = "Doctor", IsActive = true });
        db.StaffFacilityAssignments.Add(new() { UserId = Doctor2UserId, FacilityId = facility.Id, Role = role, IsActive = active });
        await db.SaveChangesAsync();
        var response = await Client.GetAsync($"/api/v1/doctors?facilityId={facility.Id}");
        response.EnsureSuccessStatusCode();
        var ids = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal(new[] { DoctorEntityId }, ids);
        var all = (await Client.GetFromJsonAsync<JsonElement>("/api/v1/doctors")).GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
        Assert.Contains(DoctorEntityId, all);
        Assert.Contains(Doctor2EntityId, all);
        var absent = (await Client.GetFromJsonAsync<JsonElement>("/api/v1/doctors?facilityId=9223372036854775807")).GetProperty("data");
        Assert.Empty(absent.EnumerateArray());
    }

    private async Task<PatientVisit> VisitAsync(VisitStatus status, bool appointment = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await db.Departments.FirstAsync(x => x.IsActive && x.SpecialtyId == SpecialtyEntityId);
        var visit = new PatientVisit { VisitCode = "R3-" + Guid.NewGuid().ToString("N")[..12], PatientId = Patient1EntityId, AssignedDoctorId = DoctorEntityId, FacilityId = department.FacilityId, DepartmentId = department.Id, VisitDate = GetFutureWorkingDate(150), Status = status, CreatedByUserId = DoctorId, UpdatedAtUtc = DateTime.UtcNow.AddDays(-1) };
        visit.QueueNumber = (await db.PatientVisits.Where(x => x.VisitDate == visit.VisitDate && x.FacilityId == visit.FacilityId && x.DepartmentId == visit.DepartmentId).MaxAsync(x => (int?)x.QueueNumber) ?? 0) + 1;
        if (appointment)
        {
            var source = await db.AppointmentSlots.FirstAsync(x => x.Id == SlotEntityId);
            visit.Appointment = new Appointment { AppointmentCode = "R3-" + Guid.NewGuid().ToString("N")[..12], PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, FacilityId = department.FacilityId, AppointmentSlotId = source.Id, AppointmentDate = source.SlotDate, StartTime = source.StartTime, EndTime = source.EndTime, Status = AppointmentStatus.InConsultation, Reason = "Kiểm thử chỉ định" };
        }
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    private async Task<DiagnosticOrder> OrderAsync(PatientVisit visit, DiagnosticOrderStatus status, bool legacyLink = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var serviceId = await db.DiagnosticServices.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        var order = new DiagnosticOrder { OrderCode = "R3-" + Guid.NewGuid().ToString("N")[..12], PatientId = visit.PatientId, OrderingDoctorId = DoctorEntityId, PatientVisitId = legacyLink ? null : visit.Id, AppointmentId = visit.AppointmentId, FacilityId = visit.FacilityId, Status = status, ClinicalIndication = "Kiểm thử trạng thái lượt khám" };
        order.Items.Add(new DiagnosticOrderItem { DiagnosticServiceId = serviceId, Status = status == DiagnosticOrderStatus.InProgress ? DiagnosticItemStatus.Completed : DiagnosticItemStatus.Ordered, Result = status == DiagnosticOrderStatus.InProgress ? new DiagnosticResult { ResultText = "Kết quả giả lập", ResultedByUserId = TechnicianId } : null });
        db.DiagnosticOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task CompleteAsync(long id)
    {
        using var client = await CreateAuthenticatedClientAsync("tech@test.com");
        var response = await client.PostAsync($"/api/v1/diagnostics/orders/{id}/complete", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<Facility> FacilityAsync(AppDbContext db)
    {
        var facility = new Facility { Code = "R3-" + Guid.NewGuid().ToString("N")[..12], Name = "Cơ sở thử nghiệm R3", IsActive = true };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        _facilities.Add(facility.Id);
        return facility;
    }

    private async Task<Guid> ReceptionistAsync(IServiceProvider services, AppDbContext db, long facilityId, string role, bool active)
    {
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"r3-{Guid.NewGuid():N}@test.com";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Lễ tân kiểm thử R3", PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", IsActive = true };
        Assert.True((await manager.CreateAsync(user, "Pass@1234")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, "Receptionist")).Succeeded);
        _users.Add(user.Id);
        db.StaffFacilityAssignments.Add(new() { UserId = user.Id, FacilityId = facilityId, Role = role, IsActive = active });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(long RequestId, long SlotId)> RevisitAsync(AppDbContext db, long? facilityId)
    {
        var date = GetFutureWorkingDate(180 + Random.Shared.Next(1000));
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new(10, 0), new(10, 30));
        var source = await db.AppointmentSlots.FirstAsync(x => x.Id == SlotEntityId);
        var original = new Appointment { AppointmentCode = "R3-" + Guid.NewGuid().ToString("N")[..12], PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, FacilityId = facilityId, AppointmentSlotId = source.Id, AppointmentDate = source.SlotDate, StartTime = source.StartTime, EndTime = source.EndTime, Status = AppointmentStatus.Completed, Reason = "Khám gốc thử nghiệm" };
        db.Appointments.Add(original);
        await db.SaveChangesAsync();
        var request = new RevisitRequest { AppointmentId = original.Id, PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SuggestedDate = date, Status = RevisitRequestStatus.PendingPatientResponse };
        db.RevisitRequests.Add(request);
        await db.SaveChangesAsync();
        return (request.Id, slot.Id);
    }
}
