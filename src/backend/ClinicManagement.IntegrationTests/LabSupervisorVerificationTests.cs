using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Authentication;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class LabSupervisorVerificationTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string SupervisorEmail = "lab.supervisor@test.com";
    private const string OtherSupervisorEmail = "lab.outside@test.com";
    private const string OtherTechEmail = "lab.technician@test.com";
    private static int _sequence = 1200;
    private const string Shared = "/api/v1/diagnostic-verification";

    private async Task<Guid> EnsureStaffAsync(string email, string role, bool inScope = true)
    {
        await RoleSeeder.SeedRolesAsync(Factory.Services);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, FullName = role + " reviewer", PhoneNumber = role == RoleNames.DiagnosticTechnician ? "0199900031" : inScope ? "0199900032" : "0199900033", IsActive = true };
            Assert.True((await manager.CreateAsync(user, "Pass@123")).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        }
        if (inScope)
        {
            var assignment = await db.StaffFacilityAssignments.AsNoTracking().Where(a => a.UserId == TechnicianId && a.IsActive).OrderBy(a => a.FacilityId).FirstAsync();
            if (!await db.StaffFacilityAssignments.AnyAsync(a => a.UserId == user.Id && a.FacilityId == assignment.FacilityId && a.Role == role && a.IsActive))
                db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = user.Id, FacilityId = assignment.FacilityId, DepartmentId = assignment.DepartmentId, Role = role, IsActive = true });
            await db.SaveChangesAsync();
        }
        return user.Id;
    }

    private async Task<DiagnosticOrderDto> ReadyAsync(bool submit = true, long? doctorId = null)
    {
        await EnsureStaffAsync(SupervisorEmail, RoleNames.LabSupervisor);
        await EnsureStaffAsync(OtherSupervisorEmail, RoleNames.LabSupervisor, false);
        await EnsureStaffAsync(OtherTechEmail, RoleNames.DiagnosticTechnician);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignment = await db.StaffFacilityAssignments.Where(a => a.UserId == TechnicianId && a.IsActive).OrderBy(a => a.FacilityId).FirstAsync();
        if (!await db.StaffFacilityAssignments.AnyAsync(a => a.UserId == Doctor2UserId && a.FacilityId == assignment.FacilityId && a.Role == RoleNames.Doctor && a.IsActive))
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = Doctor2UserId, FacilityId = assignment.FacilityId, DepartmentId = assignment.DepartmentId, Role = RoleNames.Doctor, IsActive = true });
        var sequence = Interlocked.Increment(ref _sequence);
        var visit = new PatientVisit { VisitCode = $"LB-{Guid.NewGuid():N}"[..22], PatientId = Patient1EntityId, FacilityId = assignment.FacilityId, DepartmentId = assignment.DepartmentId!.Value,
            AssignedDoctorId = doctorId ?? DoctorEntityId, VisitDate = GetFutureWorkingDate(sequence), QueueNumber = sequence, Status = VisitStatus.WaitingForDiagnostics, CreatedByUserId = ReceptionistId };
        var order = new DiagnosticOrder { OrderCode = $"DX-LB-{Guid.NewGuid():N}", PatientVisit = visit, PatientId = Patient1EntityId, OrderingDoctorId = doctorId ?? DoctorEntityId,
            FacilityId = assignment.FacilityId, ClinicalIndication = "Kiểm thử duyệt đa vai trò", Status = DiagnosticOrderStatus.InProgress, StartedByUserId = TechnicianId,
            Items = new List<DiagnosticOrderItem> { new() { Status = DiagnosticItemStatus.Completed, SpecimenStatus = SpecimenStatus.Received,
                DiagnosticService = new DiagnosticService { Code = $"LB-{Guid.NewGuid():N}", Name = "Xét nghiệm kiểm thử", Category = DiagnosticCategory.Laboratory, SpecimenType = "Máu", ContainerType = "Ống EDTA" },
                Result = new DiagnosticResult { ResultText = "Kết quả đầy đủ", ResultedByUserId = TechnicianId } } } };
        db.DiagnosticOrders.Add(order); await db.SaveChangesAsync();
        await AuthenticateAsync("tech@test.com");
        var dto = await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
        return submit ? await ReadAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/complete", new { rowVersion = dto.RowVersion })) : dto;
    }

    private static async Task<DiagnosticOrderDto> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var json = JsonDocument.Parse(text);
        return JsonSerializer.Deserialize<DiagnosticOrderDto>(json.RootElement.GetProperty("data").GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private async Task<DiagnosticOrderDto> DetailAsync(long id) => await ReadAsync(await Client.GetAsync($"{Shared}/orders/{id}"));
    private static async Task ErrorAsync(HttpResponseMessage response, string code) { Assert.False(response.IsSuccessStatusCode); Assert.Contains(code, await response.Content.ReadAsStringAsync()); }

    [Theory]
    [InlineData("Completed", "NotRequired", true)]
    [InlineData("InProgress", "NotRequired", false)]
    [InlineData("InProgress", "Pending", false)]
    [InlineData("InProgress", "Returned", false)]
    public async Task LegacyCompletedResultsRemainVisible_WhileUnverifiedInProgressResultsStayHidden(string status, string verification, bool visible)
    {
        var order = await ReadyAsync(false);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var entity = await db.DiagnosticOrders.FindAsync(order.Id);
            entity!.Status = Enum.Parse<DiagnosticOrderStatus>(status); entity.VerificationStatus = Enum.Parse<DiagnosticVerificationStatus>(verification); await db.SaveChangesAsync();
        }
        await AuthenticateAsync("doc@test.com");
        var detail = await ReadAsync(await Client.GetAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}"));
        Assert.Equal(visible, detail.Items[0].Result != null);
        using var list = JsonDocument.Parse(await (await Client.GetAsync($"/api/v1/doctor/visits/{order.PatientVisitId}/diagnostic-orders")).Content.ReadAsStringAsync());
        Assert.Equal(visible, list.RootElement.GetProperty("data")[0].GetProperty("items")[0].GetProperty("result").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task RoleSeederCreatesLabSupervisorIdempotently()
    {
        await RoleSeeder.SeedRolesAsync(Factory.Services); await RoleSeeder.SeedRolesAsync(Factory.Services);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Roles.CountAsync(r => r.Name == RoleNames.LabSupervisor));
    }

    [Theory]
    [InlineData(SupervisorEmail, "LabSupervisor")]
    [InlineData("doc2@test.com", "Doctor")]
    [InlineData(OtherTechEmail, "DiagnosticTechnician")]
    public async Task EligibleReviewerCompletesOrder_NotifiesOrderingDoctor_AndPreservesHistoricalRole(string email, string role)
    {
        var order = await ReadyAsync(); await AuthenticateAsync(email);
        var detail = await DetailAsync(order.Id); Assert.True(detail.CanCurrentUserVerify); Assert.NotNull(detail.Items[0].Result);
        var completed = await ReadAsync(await Client.PostAsJsonAsync($"{Shared}/orders/{order.Id}/verify", new { note = "Đã đối chiếu", rowVersion = detail.RowVersion }));
        Assert.Equal("Completed", completed.Status); Assert.Equal("Verified", completed.VerificationStatus); Assert.Equal(role, completed.VerifiedByRole);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(VisitStatus.ResultsReady, (await db.PatientVisits.FindAsync(order.PatientVisitId))!.Status);
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == DoctorId && n.DedupeKey == $"diag_completed_doc_{order.Id}_{DoctorId}"));
        Assert.Contains(role == RoleNames.Doctor ? "(Bác sĩ)." : role == RoleNames.LabSupervisor ? "(Trưởng phòng xét nghiệm)." : "(Kỹ thuật viên).", (await db.SystemAuditLogs.SingleAsync(a => a.Action == "DiagnosticResultsVerified" && a.EntityId == order.Id.ToString())).Description);
        Assert.Equal(role, (await DetailAsync(order.Id)).VerifiedByRole);
    }

    [Theory]
    [InlineData("verify")]
    [InlineData("return")]
    public async Task OrderingDoctorCannotVerifyOrReturn_AndCannotReadOwnPendingThroughSharedEndpoint(string action)
    {
        var order = await ReadyAsync(); await AuthenticateAsync("doc@test.com");
        await ErrorAsync(await Client.GetAsync($"{Shared}/orders/{order.Id}"), "ORDERING_DOCTOR_CANNOT_VERIFY");
        await ErrorAsync(await Client.PostAsJsonAsync($"{Shared}/orders/{order.Id}/{action}", new { rowVersion = order.RowVersion, reason = "Cần đối chiếu" }), "ORDERING_DOCTOR_CANNOT_VERIFY");
        var doctorDto = await ReadAsync(await Client.GetAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}")); Assert.False(doctorDto.CanCurrentUserVerify); Assert.Null(doctorDto.Items[0].Result);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("verify")]
    [InlineData("return")]
    public async Task SupervisorOutsideFacilityReceivesNotFound(string operation)
    {
        var order = await ReadyAsync(); await AuthenticateAsync(OtherSupervisorEmail);
        var response = operation == "get" ? await Client.GetAsync($"{Shared}/orders/{order.Id}") : await Client.PostAsJsonAsync($"{Shared}/orders/{order.Id}/{operation}", new { rowVersion = order.RowVersion, reason = "Cần đối chiếu" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("submitter")]
    [InlineData("writer")]
    public async Task SupervisorStillCannotApproveOwnSubmissionOrResult(string selfKind)
    {
        var order = await ReadyAsync(); var supervisorId = await EnsureStaffAsync(SupervisorEmail, RoleNames.LabSupervisor);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (selfKind == "submitter") (await db.DiagnosticOrders.FindAsync(order.Id))!.SubmittedForVerificationByUserId = supervisorId;
            else (await db.DiagnosticResults.SingleAsync(r => r.DiagnosticOrderItemId == order.Items[0].Id)).ResultedByUserId = supervisorId;
            await db.SaveChangesAsync();
        }
        await AuthenticateAsync(SupervisorEmail); var detail = await DetailAsync(order.Id); Assert.False(detail.CanCurrentUserVerify);
        await ErrorAsync(await Client.PostAsJsonAsync($"{Shared}/orders/{order.Id}/verify", new { rowVersion = detail.RowVersion }), "SELF_VERIFICATION_NOT_ALLOWED");
        await ErrorAsync(await Client.PostAsJsonAsync($"{Shared}/orders/{order.Id}/return", new { rowVersion = detail.RowVersion, reason = "Cần đối chiếu" }), "SELF_VERIFICATION_NOT_ALLOWED");
    }

    [Fact]
    public async Task SubmitNotifiesOnlySupervisorInFacility_ResubmitUsesNewDedupeKey()
    {
        var order = await ReadyAsync(); var supervisorId = await EnsureStaffAsync(SupervisorEmail, RoleNames.LabSupervisor);
        var outsiderId = await EnsureStaffAsync(OtherSupervisorEmail, RoleNames.LabSupervisor, false);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notice = await db.Notifications.SingleAsync(n => n.DedupeKey == $"diag_pending_{order.Id}_{supervisorId}_0");
        Assert.Equal("Có phiếu kết quả chờ duyệt", notice.Title); Assert.Equal(NotificationType.Diagnostic, notice.Type); Assert.Equal($"/lab/orders/{order.Id}", notice.Route);
        Assert.False(await db.Notifications.AnyAsync(n => n.UserId == outsiderId && n.RelatedEntityId == order.Id.ToString()));
        await AuthenticateAsync(SupervisorEmail);
        var returned = await ReadAsync(await Client.PostAsJsonAsync($"{Shared}/orders/{order.Id}/return", new { reason = "Cần đối chiếu", rowVersion = order.RowVersion }));
        Assert.Equal("Returned", returned.VerificationStatus);
        await AuthenticateAsync("tech@test.com");
        await ReadAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/complete", new { rowVersion = returned.RowVersion }));
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.UserId == supervisorId && n.DedupeKey!.StartsWith($"diag_pending_{order.Id}_")));
    }

    [Fact]
    public async Task DoctorQueueExcludesOwnOrders_AndSupervisorQueueMatchesSingleDto()
    {
        var own = await ReadyAsync(); var other = await ReadyAsync(doctorId: Doctor2EntityId);
        await AuthenticateAsync("doc@test.com");
        using var doctorQueue = JsonDocument.Parse(await (await Client.GetAsync(Shared + "/queue?pageSize=100")).Content.ReadAsStringAsync());
        var ids = doctorQueue.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
        Assert.DoesNotContain(own.Id, ids); Assert.Contains(other.Id, ids); Assert.True((await DetailAsync(other.Id)).CanCurrentUserVerify);
        await AuthenticateAsync(SupervisorEmail);
        using var queue = JsonDocument.Parse(await (await Client.GetAsync(Shared + "/queue?pageSize=100")).Content.ReadAsStringAsync());
        var item = queue.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt64() == own.Id);
        var single = await DetailAsync(own.Id);
        Assert.Equal(JsonSerializer.Serialize(single), JsonSerializer.Serialize(JsonSerializer.Deserialize<DiagnosticOrderDto>(item.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    [Fact]
    public async Task OldTechnicianRoutesAndAuthorizationRemainAvailable()
    {
        var order = await ReadyAsync(); await AuthenticateAsync(OtherTechEmail);
        var detail = await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}")); Assert.True(detail.CanCurrentUserVerify);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/v1/diagnostics/orders/verification-queue")).StatusCode);
        var completed = await ReadAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/verify", new { rowVersion = detail.RowVersion })); Assert.Equal("Completed", completed.Status);
        await AuthenticateAsync(SupervisorEmail); Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("/api/v1/diagnostics/orders/verification-queue")).StatusCode);
    }

    [Fact]
    public async Task AdminCanCreateSupervisor_AndAssignFacility()
    {
        await RoleSeeder.SeedRolesAsync(Factory.Services); await AuthenticateAsync("admin@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/admin/users", new { email = $"new-lab-{Guid.NewGuid():N}@test.com", fullName = "Trưởng khoa mới", phoneNumber = "0199900044", password = "Pass@123", role = RoleNames.LabSupervisor });
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, text);
        using var json = JsonDocument.Parse(text); var userId = json.RootElement.GetProperty("data").GetProperty("id").GetGuid();
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = await db.StaffFacilityAssignments.FirstAsync(a => a.UserId == TechnicianId && a.IsActive);
        var assignment = await Client.PostAsJsonAsync("/api/v1/admin/staff-assignments", new { userId, facilityId = facility.FacilityId, departmentId = facility.DepartmentId, role = RoleNames.LabSupervisor, isPrimary = true });
        Assert.True(assignment.IsSuccessStatusCode, await assignment.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DemoSeedTwiceCreatesOneUserAndAssignmentPerNewAccount_WithoutDuplicatingCatalog()
    {
        using var seedApp = FixedSeedWebApplicationFactory.WithClock(Factory);
        await RoleSeeder.SeedRolesAsync(seedApp.Services);
        await DevelopmentDataSeeder.SeedMasterDataAsync(seedApp.Services);
        using var scope = seedApp.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var services = await db.DiagnosticServices.CountAsync(); var parameters = await db.DiagnosticServiceParameters.CountAsync();
        await DevelopmentDataSeeder.SeedMasterDataAsync(seedApp.Services);
        Assert.Equal(services, await db.DiagnosticServices.CountAsync()); Assert.Equal(parameters, await db.DiagnosticServiceParameters.CountAsync());
        foreach (var email in new[] { "labsupervisor@cliniccare.local", "technician.02@cliniccare.local" })
        {
            var user = await db.Users.SingleAsync(u => u.Email == email); Assert.Equal(1, await db.StaffFacilityAssignments.CountAsync(a => a.UserId == user.Id && a.IsActive));
            var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Demo@12345" });
            Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
        }
    }
}
