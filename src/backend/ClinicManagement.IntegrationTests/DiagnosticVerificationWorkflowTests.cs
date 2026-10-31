using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DiagnosticVerificationWorkflowTests : IntegrationTestBase
{
    public DiagnosticVerificationWorkflowTests(CustomWebApplicationFactory factory) : base(factory) { }
    private static int _sequence = 800;
    private const string ReviewerEmail = "verification.tech2@test.com";

    private async Task<Guid> EnsureReviewerAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(ReviewerEmail);
        if (user == null)
        {
            user = new ApplicationUser { Id = Guid.NewGuid(), UserName = ReviewerEmail, Email = ReviewerEmail, PhoneNumber = "0199999901", FullName = "Technician reviewer", IsActive = true };
            Assert.True((await manager.CreateAsync(user, "Pass@123")).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, "DiagnosticTechnician")).Succeeded);
        }
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignments = await db.StaffFacilityAssignments.AsNoTracking().Where(a => a.UserId == TechnicianId && a.IsActive).ToListAsync();
        foreach (var assignment in assignments)
        {
            if (!await db.StaffFacilityAssignments.AnyAsync(a => a.UserId == user.Id && a.FacilityId == assignment.FacilityId && a.IsActive))
                db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = user.Id, FacilityId = assignment.FacilityId, DepartmentId = assignment.DepartmentId, Role = "DiagnosticTechnician", IsActive = true });
        }
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<DiagnosticOrderDto> ReadyAsync(bool required = true, bool numeric = false, string? serviceCode = null)
    {
        await EnsureReviewerAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = await db.StaffFacilityAssignments.Where(a => a.UserId == TechnicianId && a.IsActive).OrderBy(a => a.FacilityId).Select(a => a.FacilityId).FirstAsync();
        var department = await db.Departments.Where(d => d.FacilityId == facility).Select(d => d.Id).FirstAsync();
        var service = serviceCode != null ? await db.DiagnosticServices.FirstOrDefaultAsync(s => s.Code == serviceCode) : null;
        service ??= new DiagnosticService { Code = serviceCode ?? $"VR-{Guid.NewGuid():N}", Name = "Xét nghiệm duyệt kiểm thử", Category = DiagnosticCategory.Laboratory, SpecimenType = required ? "Máu toàn phần" : null, ContainerType = required ? "Ống EDTA" : null };
        if (numeric && service.Parameters.Count == 0) service.Parameters.Add(new DiagnosticServiceParameter { Code = "WBC", Name = "Bạch cầu", Unit = "10^9/L", RefLow = 4, RefHigh = 10, CriticalLow = 2, CriticalHigh = 30 });
        var sequence = Interlocked.Increment(ref _sequence);
        var visit = new PatientVisit { VisitCode = $"VR-{Guid.NewGuid():N}"[..22], PatientId = Patient1EntityId, FacilityId = facility, DepartmentId = department, AssignedDoctorId = DoctorEntityId,
            VisitDate = GetFutureWorkingDate(sequence), QueueNumber = sequence, Status = VisitStatus.WaitingForDiagnostics, CreatedByUserId = ReceptionistId };
        var entity = new DiagnosticOrder { OrderCode = $"DX-VR-{Guid.NewGuid():N}", PatientVisit = visit, PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId, FacilityId = facility,
            ClinicalIndication = "Kiểm thử duyệt kết quả", Status = DiagnosticOrderStatus.InProgress, StartedAtUtc = DateTime.UtcNow, StartedByUserId = TechnicianId,
            Items = new List<DiagnosticOrderItem> { new() { DiagnosticService = service, Status = DiagnosticItemStatus.InProgress, SpecimenStatus = required ? SpecimenStatus.Received : SpecimenStatus.NotRequired } } };
        db.DiagnosticOrders.Add(entity); await db.SaveChangesAsync();
        await AuthenticateAsync("tech@test.com");
        var order = await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{entity.Id}"));
        object body = numeric ? new { resultText = "", rowVersion = order.Items[0].RowVersion, values = new[] { new { parameterId = order.Items[0].Parameters[0].Id, value = 2m } } }
            : new { resultText = "Kết quả đã nhập", rowVersion = order.Items[0].RowVersion };
        return await ReadAsync(await Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/items/{order.Items[0].Id}/result", body));
    }

    private static async Task<DiagnosticOrderDto> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var json = JsonDocument.Parse(text);
        return JsonSerializer.Deserialize<DiagnosticOrderDto>(json.RootElement.GetProperty("data").GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private Task<HttpResponseMessage> ActionAsync(DiagnosticOrderDto order, string action, object body) => Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/{action}", body);
    private async Task<DiagnosticOrderDto> CompleteAsync(DiagnosticOrderDto order) => await ReadAsync(await ActionAsync(order, "complete", new { rowVersion = order.RowVersion }));

    // Complete no longer submits for verification; this recreates an order that was already waiting for review before the step was removed.
    private async Task<DiagnosticOrderDto> SubmitAsync(DiagnosticOrderDto order, Guid? submitterId = null)
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.DiagnosticOrders.FindAsync(order.Id);
            var submitter = submitterId ?? TechnicianId;
            entity.VerificationStatus = DiagnosticVerificationStatus.Pending;
            entity.SubmittedForVerificationAtUtc = DateTime.UtcNow;
            entity.SubmittedForVerificationByUserId = submitter;
            entity.RowVersion = Guid.NewGuid().ToByteArray();
            await db.SaveChangesAsync();
        }
        return await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
    }
    private static async Task ErrorAsync(HttpResponseMessage response, string code)
    {
        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CompleteWithSpecimenFinalizesImmediately_AndNotifiesDoctor()
    {
        var order = await CompleteAsync(await ReadyAsync());
        Assert.Equal("Completed", order.Status); Assert.Equal("NotRequired", order.VerificationStatus);
        Assert.NotNull(order.CompletedAtUtc); Assert.Null(order.SubmittedForVerificationAtUtc);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(VisitStatus.ResultsReady, (await db.PatientVisits.FindAsync(order.PatientVisitId))!.Status);
        Assert.True(await db.Notifications.AnyAsync(n => n.DedupeKey == $"diag_completed_doc_{order.Id}_{DoctorId}"));
        Assert.False(await db.SystemAuditLogs.AnyAsync(a => a.Action == "DiagnosticResultsSubmittedForVerification" && a.EntityId == order.Id.ToString()));
    }

    [Fact]
    public async Task TechnicianCompletingSpecimenOrder_LetsDoctorSeeResultAndNotifiesThem()
    {
        var order = await CompleteAsync(await ReadyAsync(serviceCode: "LAB-GLU"));
        Assert.Equal("Completed", order.Status);
        await AuthenticateAsync("doc@test.com");
        var visitOrders = JsonDocument.Parse(await (await Client.GetAsync($"/api/v1/doctor/visits/{order.PatientVisitId}/diagnostic-orders")).Content.ReadAsStringAsync());
        var item = visitOrders.RootElement.GetProperty("data").EnumerateArray().Single(o => o.GetProperty("id").GetInt64() == order.Id).GetProperty("items")[0];
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("result").ValueKind);
        Assert.NotNull((await ReadAsync(await Client.GetAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}"))).Items[0].Result);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notice = await db.Notifications.SingleAsync(n => n.DedupeKey == $"diag_completed_doc_{order.Id}_{DoctorId}");
        Assert.Equal("Có kết quả cận lâm sàng", notice.Title);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Returned")]
    public async Task CompleteFinalizesLegacyPendingOrReturnedOrder(string legacyStatus)
    {
        var order = await ReadyAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.DiagnosticOrders.FindAsync(order.Id);
            entity!.VerificationStatus = Enum.Parse<DiagnosticVerificationStatus>(legacyStatus); entity.RowVersion = Guid.NewGuid().ToByteArray();
            await db.SaveChangesAsync();
        }
        var current = await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
        var completed = await CompleteAsync(current);
        Assert.Equal("Completed", completed.Status);
    }

    [Theory]
    [InlineData("verify")]
    [InlineData("return")]
    public async Task SubmitterCannotVerifyOrReturnOwnOrder(string action)
    {
        var order = await SubmitAsync(await ReadyAsync());
        await ErrorAsync(await ActionAsync(order, action, new { rowVersion = order.RowVersion, reason = "Cần chỉnh sửa" }), "SELF_VERIFICATION_NOT_ALLOWED");
    }

    [Fact]
    public async Task DifferentTechnicianVerifies_CompletionIsAttributedToSubmitter_VisitBecomesReady()
    {
        var order = await SubmitAsync(await ReadyAsync());
        var reviewerId = await EnsureReviewerAsync(); await AuthenticateAsync(ReviewerEmail);
        var detail = await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}")); Assert.True(detail.CanCurrentUserVerify);
        var verified = await ReadAsync(await ActionAsync(detail, "verify", new { note = " Đã đối chiếu ", rowVersion = detail.RowVersion }));
        Assert.Equal("Completed", verified.Status); Assert.Equal("Verified", verified.VerificationStatus);
        Assert.Equal("Technician reviewer", verified.VerifiedByUserName); Assert.Equal("Technician", verified.CompletedByUserName);
        Assert.Equal("Đã đối chiếu", verified.VerificationNote); Assert.NotNull(verified.VerifiedAtUtc); Assert.False(verified.CanCurrentUserVerify);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.DiagnosticOrders.FindAsync(order.Id); Assert.Equal(TechnicianId, entity!.CompletedByUserId); Assert.Equal(reviewerId, entity.VerifiedByUserId);
        Assert.Equal(VisitStatus.ResultsReady, (await db.PatientVisits.FindAsync(order.PatientVisitId))!.Status);
        var notice = Assert.Single(await db.Notifications.Where(n => n.DedupeKey == $"diag_completed_doc_{order.Id}_{DoctorId}").ToListAsync());
        Assert.Equal($"/doctor/visits/{order.PatientVisitId}/examination", notice.Route);
        Assert.True(await db.SystemAuditLogs.AnyAsync(a => a.Action == "DiagnosticResultsVerified" && a.UserId == reviewerId && a.EntityId == order.Id.ToString()));
        await ErrorAsync(await ActionAsync(verified, "verify", new { rowVersion = verified.RowVersion }), "INVALID_STATE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("     ")]
    public async Task ReturnRequiresFiveTrimmedCharacters(string reason)
    {
        var order = await SubmitAsync(await ReadyAsync()); await AuthenticateAsync(ReviewerEmail);
        await ErrorAsync(await ActionAsync(order, "return", new { reason, rowVersion = order.RowVersion }), "VALIDATION_ERROR");
        Assert.Equal("Pending", (await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"))).VerificationStatus);
    }

    [Fact]
    public async Task ReturnedOrderCanBeEditedAndResubmitted_WithCountAndNotificationHistory()
    {
        var order = await SubmitAsync(await ReadyAsync()); await AuthenticateAsync(ReviewerEmail);
        var returned = await ReadAsync(await ActionAsync(order, "return", new { reason = " Đối chiếu lại chỉ số ", rowVersion = order.RowVersion }));
        Assert.Equal("Returned", returned.VerificationStatus); Assert.Equal("InProgress", returned.Status); Assert.Equal(1, returned.ReturnCount);
        Assert.Equal("Đối chiếu lại chỉ số", returned.ReturnReason); Assert.Equal("Technician reviewer", returned.ReturnedByUserName);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notice = await db.Notifications.SingleAsync(n => n.DedupeKey == $"diag_returned_{order.Id}_1");
        Assert.Equal(TechnicianId, notice.UserId); Assert.Equal("Kết quả bị trả lại — cần chỉnh sửa", notice.Title);
        Assert.Equal($"/diagnostics/orders/{order.Id}", notice.Route); Assert.Equal(NotificationType.Diagnostic, notice.Type);
        Assert.True(await db.SystemAuditLogs.AnyAsync(a => a.Action == "DiagnosticResultsReturned" && a.EntityId == order.Id.ToString()));
        await AuthenticateAsync("tech@test.com");
        var edited = await ReadAsync(await Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/items/{returned.Items[0].Id}/result", new { resultText = "Đã chỉnh sửa", rowVersion = returned.Items[0].RowVersion }));
        var resubmitted = await SubmitAsync(edited); Assert.Equal("Pending", resubmitted.VerificationStatus); Assert.Equal(1, resubmitted.ReturnCount);
        await AuthenticateAsync(ReviewerEmail);
        var verified = await ReadAsync(await ActionAsync(resubmitted, "verify", new { rowVersion = resubmitted.RowVersion }));
        Assert.Equal("Đã chỉnh sửa", verified.Items[0].Result!.ResultText);
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.DedupeKey == $"diag_returned_{order.Id}_1"));
    }

    [Fact]
    public async Task LegacyPendingOrderNoLongerLocksResultEditing()
    {
        var order = await SubmitAsync(await ReadyAsync()); var item = order.Items[0];
        var edited = await ReadAsync(await Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/items/{item.Id}/result", new { resultText = "Sửa khi phiếu cũ còn chờ duyệt", rowVersion = item.RowVersion }));
        Assert.Equal("Sửa khi phiếu cũ còn chờ duyệt", edited.Items[0].Result!.ResultText);
    }

    [Fact]
    public async Task DoctorSeesOnlyVerifiedResults_AndPatientStillWaitsForDoctorReview_WithRedaction()
    {
        var order = await SubmitAsync(await ReadyAsync(numeric: true)); await AuthenticateAsync("doc@test.com");
        Assert.Null((await ReadAsync(await Client.GetAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}"))).Items[0].Result);
        var pendingList = JsonDocument.Parse(await (await Client.GetAsync($"/api/v1/doctor/visits/{order.PatientVisitId}/diagnostic-orders")).Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, pendingList.RootElement.GetProperty("data")[0].GetProperty("items")[0].GetProperty("result").ValueKind);
        await AuthenticateAsync(ReviewerEmail);
        var verified = await ReadAsync(await ActionAsync(order, "verify", new { note = "Ghi chú nội bộ", rowVersion = order.RowVersion }));
        await AuthenticateAsync("doc@test.com");
        Assert.Single((await ReadAsync(await Client.GetAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}"))).Items[0].Result!.Values);
        await AuthenticateAsync("pat1@test.com");
        Assert.Null((await ReadAsync(await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{order.Id}"))).Items[0].Result);
        await AuthenticateAsync("doc@test.com");
        await ReadAsync(await Client.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}/review", new { rowVersion = verified.RowVersion }));
        await AuthenticateAsync("pat1@test.com");
        var published = await ReadAsync(await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{order.Id}"));
        Assert.NotNull(published.Items[0].Result); Assert.Equal("Verified", published.VerificationStatus);
        Assert.Null(published.SubmittedForVerificationByUserName); Assert.Null(published.VerifiedByUserName); Assert.Null(published.ReturnedByUserName);
        Assert.Null(published.VerificationNote); Assert.Null(published.ReturnReason); Assert.False(published.CanCurrentUserVerify);
    }

    [Fact]
    public async Task CriticalNotificationIsImmediateBeforeSubmittingOrVerifying()
    {
        var order = await ReadyAsync(numeric: true); Assert.True(order.Items[0].Result!.HasCriticalValue);
        using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notices = await db.Notifications.Where(n => n.DedupeKey!.StartsWith($"diag_critical_{order.Items[0].Id}_")).ToListAsync();
        Assert.Single(notices); Assert.Equal(DoctorId, notices[0].UserId);
        await SubmitAsync(order); Assert.Single(await db.Notifications.Where(n => n.DedupeKey!.StartsWith($"diag_critical_{order.Items[0].Id}_")).ToListAsync());
    }

    [Fact]
    public async Task UndeclaredServicesStillCompleteImmediately_EvenWithCancelledDeclaredItem()
    {
        var order = await ReadyAsync(required: false);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DiagnosticOrderItems.Add(new DiagnosticOrderItem { DiagnosticOrderId = order.Id, Status = DiagnosticItemStatus.Cancelled,
                DiagnosticService = new DiagnosticService { Code = $"C-VR-{Guid.NewGuid():N}", Name = "Mẫu hủy", SpecimenType = "Máu" } });
            await db.SaveChangesAsync();
        }
        var completed = await CompleteAsync(order); Assert.Equal("Completed", completed.Status); Assert.Equal("NotRequired", completed.VerificationStatus);
    }

    [Theory]
    [InlineData("verify")]
    [InlineData("return")]
    public async Task StaleOrderRowVersionConflicts(string action)
    {
        var ready = await ReadyAsync(); var submitted = await SubmitAsync(ready); await AuthenticateAsync(ReviewerEmail);
        var response = await ActionAsync(submitted, action, new { rowVersion = ready.RowVersion, reason = "Đối chiếu lại" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); await ErrorAsync(response, "STATE_CONFLICT");
    }

    [Fact]
    public async Task VerificationQueueIsFacilityScoped_PaginatedAndSharesSingleDtoMapping()
    {
        var first = await SubmitAsync(await ReadyAsync()); var second = await SubmitAsync(await ReadyAsync()); var outside = await SubmitAsync(await ReadyAsync());
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var facility = new Facility { Code = $"VR-{Guid.NewGuid():N}"[..20], Name = "Cơ sở ngoài scope", Address = "Test", City = "Test", Phone = "00000000" };
            db.Facilities.Add(facility); await db.SaveChangesAsync();
            var entity = await db.DiagnosticOrders.FindAsync(outside.Id); entity!.FacilityId = facility.Id; await db.SaveChangesAsync();
        }
        await AuthenticateAsync(ReviewerEmail);
        var json = JsonDocument.Parse(await (await Client.GetAsync("/api/v1/diagnostics/orders/verification-queue?page=1&pageSize=100")).Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, item => item.GetProperty("id").GetInt64() == outside.Id);
        var mapped = items.Single(item => item.GetProperty("id").GetInt64() == first.Id);
        var single = await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{first.Id}"));
        Assert.True(single.CanCurrentUserVerify);
        Assert.Equal(JsonSerializer.Serialize(single), JsonSerializer.Serialize(JsonSerializer.Deserialize<DiagnosticOrderDto>(mapped.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.True(items.FindIndex(item => item.GetProperty("id").GetInt64() == first.Id) < items.FindIndex(item => item.GetProperty("id").GetInt64() == second.Id));
        var page = JsonDocument.Parse(await (await Client.GetAsync("/api/v1/diagnostics/orders/verification-queue?page=1&pageSize=1")).Content.ReadAsStringAsync());
        Assert.Equal(1, page.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await ActionAsync(outside, "verify", new { rowVersion = outside.RowVersion })).StatusCode);
        await AuthenticateAsync("doc@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("/api/v1/diagnostics/orders/verification-queue")).StatusCode);
    }

    [Fact]
    public async Task AResultWriterCannotVerifyWhenAnotherTechnicianSubmitted()
    {
        var ready = await ReadyAsync(); var reviewerId = await EnsureReviewerAsync(); await AuthenticateAsync(ReviewerEmail); var order = await SubmitAsync(ready, reviewerId);
        await AuthenticateAsync("tech@test.com");
        var single = await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}")); Assert.False(single.CanCurrentUserVerify);
        await ErrorAsync(await ActionAsync(single, "verify", new { rowVersion = single.RowVersion }), "SELF_VERIFICATION_NOT_ALLOWED");
    }

    [Fact]
    public async Task VerificationExcludesWritersOfEveryItemInAMixedOrder()
    {
        var ready = await ReadyAsync(); var reviewer = await EnsureReviewerAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DiagnosticOrderItems.Add(new DiagnosticOrderItem { DiagnosticOrderId = ready.Id, Status = DiagnosticItemStatus.Completed,
                DiagnosticService = new DiagnosticService { Code = $"M-VR-{Guid.NewGuid():N}", Name = "Kết quả người nhập khác" },
                Result = new DiagnosticResult { ResultText = "Người thứ hai nhập", ResultedByUserId = reviewer } });
            await db.SaveChangesAsync();
        }
        var submitted = await SubmitAsync(ready); await AuthenticateAsync(ReviewerEmail);
        var single = await ReadAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{submitted.Id}")); Assert.False(single.CanCurrentUserVerify);
        await ErrorAsync(await ActionAsync(single, "verify", new { rowVersion = single.RowVersion }), "SELF_VERIFICATION_NOT_ALLOWED");
    }
}
