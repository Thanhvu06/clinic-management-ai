using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Authentication;
using ClinicManagement.Infrastructure.Diagnostics;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// TAT report for lab supervisors. Every test seeds its own facilities, supervisor, doctor and patient,
/// and uses fixed far-future dates, so no shared seed account or slot is touched.
/// </summary>
public class LabTurnaroundReportTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateOnly Day = new(2040, 3, 10);
    // 00:00 Vietnam time (UTC+7) on Day.
    private static readonly DateTime DayStartUtc = new(2040, 3, 9, 17, 0, 0, DateTimeKind.Utc);
    // 09:00 Vietnam time on Day.
    private static readonly DateTime T0 = DayStartUtc.AddHours(9);

    private sealed record Scope(Facility Inside, Facility Outside, Guid SupervisorId, long PatientId, long DoctorId);

    private static string Code() => Guid.NewGuid().ToString("N");
    private static string Phone() => "09" + Random.Shared.NextInt64(100000000, 999999999);

    private static async Task<Scope> SeedScopeAsync(AppDbContext db)
    {
        var code = Code();
        var inside = new Facility { Code = "TATA-" + code[..20], Name = "TAT inside", IsActive = true };
        var outside = new Facility { Code = "TATB-" + code[..20], Name = "TAT outside", IsActive = true };
        var supervisorId = Guid.NewGuid();
        var doctorUserId = Guid.NewGuid();
        db.Users.AddRange(
            new ApplicationUser { Id = supervisorId, UserName = "tat-sup-" + code, Email = "tat-sup-" + code + "@test.com", PhoneNumber = Phone(), FullName = "TAT supervisor", IsActive = true },
            new ApplicationUser { Id = doctorUserId, UserName = "tat-doc-" + code, Email = "tat-doc-" + code + "@test.com", PhoneNumber = Phone(), FullName = "TAT doctor", IsActive = true });
        var doctor = new Doctor { UserId = doctorUserId, IsActive = true };
        var patient = new Patient { FullName = "Bệnh nhân TAT", MedicalRecordNumber = "TAT-" + code[..20] };
        db.Facilities.AddRange(inside, outside);
        db.Doctors.Add(doctor);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = supervisorId, FacilityId = inside.Id, Role = RoleNames.LabSupervisor, IsActive = true });
        // Another role and an inactive supervisor assignment must not widen the scope.
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = supervisorId, FacilityId = outside.Id, Role = RoleNames.DiagnosticTechnician, IsActive = true });
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = supervisorId, FacilityId = outside.Id, Role = RoleNames.LabSupervisor, IsActive = false });
        await db.SaveChangesAsync();
        return new Scope(inside, outside, supervisorId, patient.Id, doctor.Id);
    }

    private static DiagnosticWorkflowService Service(AppDbContext db, Guid userId, string role = RoleNames.LabSupervisor, DiagnosticTatOptions? options = null)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns(userId);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(DayStartUtc.AddDays(5));
        clock.SetupGet(x => x.VietnamToday).Returns(Day.AddDays(5));
        clock.Setup(x => x.ConvertVietnamToUtc(It.IsAny<DateTime>()))
            .Returns((DateTime value) => DateTime.SpecifyKind(value.AddHours(-7), DateTimeKind.Utc));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role) }, "Test")) } };
        return new(db, Mock.Of<IDoctorContextService>(), user.Object, clock.Object, NullLogger<DiagnosticWorkflowService>.Instance, http,
            Options.Create(options ?? new DiagnosticTatOptions()));
    }

    private static DiagnosticService Catalog(DiagnosticCategory category, string name) =>
        new() { Code = "TAT-" + Code()[..20], Name = name, Category = category, IsActive = true };

    private static DiagnosticOrderItem Item(Scope scope, DiagnosticService service, DateTime? received = null, DateTime? resulted = null,
        DiagnosticItemStatus status = DiagnosticItemStatus.Completed) => new()
    {
        DiagnosticService = service,
        Status = status,
        ReceivedAtUtc = received,
        Result = resulted.HasValue ? new DiagnosticResult { ResultText = "OK", ResultedAtUtc = resulted.Value, ResultedByUserId = scope.SupervisorId } : null
    };

    private static DiagnosticOrder Completed(Scope scope, DateTime orderedUtc, double totalMinutes, Facility? facility = null, params DiagnosticOrderItem[] items) => new()
    {
        FacilityId = (facility ?? scope.Inside).Id,
        PatientId = scope.PatientId,
        OrderingDoctorId = scope.DoctorId,
        OrderCode = "TAT-" + Code()[..24],
        Status = DiagnosticOrderStatus.Completed,
        OrderedAtUtc = orderedUtc,
        CompletedAtUtc = orderedUtc.AddMinutes(totalMinutes),
        Items = items.ToList()
    };

    private static DiagnosticTurnaroundSegmentDto Segment(DiagnosticTurnaroundReportDto report, string key) =>
        Assert.Single(report.Segments, segment => segment.Key == key);

    [Fact]
    public async Task Report_CountsOnlyCompletedOrdersInAssignedFacilities()
    {
        using var serviceScope = Factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scope = await SeedScopeAsync(db);
        var lab = Catalog(DiagnosticCategory.Laboratory, "Công thức máu");
        var inProgress = Completed(scope, T0, 20, null, Item(scope, lab));
        inProgress.Status = DiagnosticOrderStatus.InProgress;
        db.DiagnosticOrders.AddRange(
            Completed(scope, T0, 20, null, Item(scope, lab)),
            Completed(scope, T0, 30, null, Item(scope, lab)),
            Completed(scope, T0, 40, scope.Outside, Item(scope, lab)),
            inProgress);
        await db.SaveChangesAsync();

        var report = await Service(db, scope.SupervisorId).GetTurnaroundReportAsync(Day, Day, null);

        Assert.Equal(Day, report.From);
        Assert.Equal(Day, report.To);
        Assert.Null(report.Category);
        Assert.Equal(2, report.OrderCount);
        Assert.Equal(25, Segment(report, "Total").MedianMinutes);

        // A supervisor without an active assignment sees nothing.
        var stranger = (await SeedScopeAsync(db)).SupervisorId;
        db.StaffFacilityAssignments.RemoveRange(db.StaffFacilityAssignments.Where(a => a.UserId == stranger));
        await db.SaveChangesAsync();
        var empty = await Service(db, stranger).GetTurnaroundReportAsync(Day, Day, null);
        Assert.Equal(0, empty.OrderCount);
        Assert.Null(empty.WithinTargetPercent);
        Assert.All(empty.Segments, segment => { Assert.Equal(0, segment.Count); Assert.Null(segment.MedianMinutes); Assert.Null(segment.P90Minutes); });
        Assert.Empty(empty.ByService);
        Assert.Empty(empty.SlowestOrders);
    }

    [Fact]
    public async Task Report_UsesVietnamDayBoundaries()
    {
        using var serviceScope = Factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scope = await SeedScopeAsync(db);
        var lab = Catalog(DiagnosticCategory.Laboratory, "Đường huyết");
        // Completed at 23:59 VN the previous day, 00:00 VN, 23:30 VN on Day, and 00:00 VN the next day.
        foreach (var completedUtc in new[] { DayStartUtc.AddMinutes(-1), DayStartUtc, DayStartUtc.AddHours(23.5), DayStartUtc.AddDays(1) })
            db.DiagnosticOrders.Add(Completed(scope, completedUtc.AddMinutes(-30), 30, null, Item(scope, lab)));
        await db.SaveChangesAsync();
        var service = Service(db, scope.SupervisorId);

        Assert.Equal(2, (await service.GetTurnaroundReportAsync(Day, Day, null)).OrderCount);
        Assert.Equal(1, (await service.GetTurnaroundReportAsync(Day.AddDays(-1), Day.AddDays(-1), null)).OrderCount);
        Assert.Equal(1, (await service.GetTurnaroundReportAsync(Day.AddDays(1), Day.AddDays(1), null)).OrderCount);
        Assert.Equal(4, (await service.GetTurnaroundReportAsync(Day.AddDays(-1), Day.AddDays(1), null)).OrderCount);
    }

    [Fact]
    public async Task Report_MedianHandlesOddAndEvenCounts_AndP90UsesNearestRank()
    {
        using var serviceScope = Factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scope = await SeedScopeAsync(db);
        var lab = Catalog(DiagnosticCategory.Laboratory, "Sinh hóa");
        foreach (var total in new[] { 50, 10, 41, 30, 20 })
            db.DiagnosticOrders.Add(Completed(scope, T0, total, null, Item(scope, lab)));
        db.DiagnosticOrders.Add(Completed(scope, T0.AddDays(1), 65, null, Item(scope, lab)));
        foreach (var total in new[] { 1, 2, 3, 4 })
            db.DiagnosticOrders.Add(Completed(scope, T0.AddDays(2), total, null, Item(scope, lab)));
        await db.SaveChangesAsync();
        var service = Service(db, scope.SupervisorId);

        var odd = Segment(await service.GetTurnaroundReportAsync(Day, Day, null), "Total");
        Assert.Equal(5, odd.Count);
        Assert.Equal(30, odd.MedianMinutes);
        Assert.Equal(50, odd.P90Minutes);

        var even = Segment(await service.GetTurnaroundReportAsync(Day, Day.AddDays(1), null), "Total");
        Assert.Equal(6, even.Count);
        Assert.Equal(35.5, even.MedianMinutes);
        Assert.Equal(65, even.P90Minutes);

        // n = 10: P90 is the 9th smallest value (not the max, not interpolated).
        var ten = await service.GetTurnaroundReportAsync(Day, Day.AddDays(2), null);
        var tenTotal = Segment(ten, "Total");
        Assert.Equal(10, tenTotal.Count);
        Assert.Equal(15, tenTotal.MedianMinutes);
        Assert.Equal(50, tenTotal.P90Minutes);
        var service1 = Assert.Single(ten.ByService);
        Assert.Equal(10, service1.OrderCount);
        Assert.Equal(15, service1.MedianTotalMinutes);
        Assert.Equal(50, service1.P90TotalMinutes);
    }

    [Fact]
    public async Task Report_SkipsStagesWithMissingOrNegativeData()
    {
        using var serviceScope = Factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scope = await SeedScopeAsync(db);
        var lab = Catalog(DiagnosticCategory.Laboratory, "Điện giải");
        var lipid = Catalog(DiagnosticCategory.Laboratory, "Mỡ máu");
        var ultrasound = Catalog(DiagnosticCategory.Ultrasound, "Siêu âm bụng");

        // Full timeline: 10 → 30 → 5, total 55. The cancelled item's earlier receive/later result is ignored.
        var full = Completed(scope, T0, 55, null,
            Item(scope, lab, T0.AddMinutes(10), T0.AddMinutes(40)),
            Item(scope, lipid, T0.AddMinutes(1), T0.AddMinutes(90), DiagnosticItemStatus.Cancelled));
        full.VerificationStatus = DiagnosticVerificationStatus.Verified;
        full.SubmittedForVerificationAtUtc = T0.AddMinutes(45);
        full.VerifiedAtUtc = T0.AddMinutes(50);

        // No specimen received and no verification: only the total counts.
        var noSpecimen = Completed(scope, T0, 20, null, Item(scope, ultrasound, null, T0.AddMinutes(15)));

        // Received before ordered (negative) and verified before submitted (negative): both stages skipped.
        var negative = Completed(scope, T0, 30, null, Item(scope, lab, T0.AddMinutes(-5), T0.AddMinutes(15)));
        negative.VerificationStatus = DiagnosticVerificationStatus.Verified;
        negative.SubmittedForVerificationAtUtc = T0.AddMinutes(25);
        negative.VerifiedAtUtc = T0.AddMinutes(20);

        // Verification timestamps on an order that is not Verified are ignored.
        var notVerified = Completed(scope, T0, 25, null, Item(scope, ultrasound, null, T0.AddMinutes(20)));
        notVerified.SubmittedForVerificationAtUtc = T0.AddMinutes(21);
        notVerified.VerifiedAtUtc = T0.AddMinutes(24);

        db.DiagnosticOrders.AddRange(full, noSpecimen, negative, notVerified);
        await db.SaveChangesAsync();

        var report = await Service(db, scope.SupervisorId).GetTurnaroundReportAsync(Day, Day, null);

        Assert.Equal(new[] { "OrderToReceive", "ReceiveToResult", "ResultToVerify", "Total" }, report.Segments.Select(segment => segment.Key));
        var orderToReceive = Segment(report, "OrderToReceive");
        Assert.Equal(1, orderToReceive.Count);
        Assert.Equal(10, orderToReceive.MedianMinutes);
        var receiveToResult = Segment(report, "ReceiveToResult");
        Assert.Equal(2, receiveToResult.Count);
        Assert.Equal(25, receiveToResult.MedianMinutes);
        Assert.Equal(30, receiveToResult.P90Minutes);
        var resultToVerify = Segment(report, "ResultToVerify");
        Assert.Equal(1, resultToVerify.Count);
        Assert.Equal(5, resultToVerify.MedianMinutes);
        var total = Segment(report, "Total");
        Assert.Equal(4, total.Count);
        Assert.Equal(27.5, total.MedianMinutes);
        Assert.Equal(55, total.P90Minutes);
    }

    [Fact]
    public async Task Report_UsesMostLenientCategoryTarget_AndComputesCompliance()
    {
        using var serviceScope = Factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scope = await SeedScopeAsync(db);
        var lab = Catalog(DiagnosticCategory.Laboratory, "Xét nghiệm máu");
        var ultrasound = Catalog(DiagnosticCategory.Ultrasound, "Siêu âm tim");
        var ultrasoundOnly = Completed(scope, T0, 50, null, Item(scope, ultrasound));           // target 45 → exceeds
        var mixed = Completed(scope, T0, 50, null, Item(scope, ultrasound), Item(scope, lab));  // target max(45, 60) = 60 → within
        var labOnTarget = Completed(scope, T0, 60, null, Item(scope, lab));                     // target 60, equal → within
        var labLate = Completed(scope, T0, 61, null, Item(scope, lab));                         // target 60 → exceeds
        db.DiagnosticOrders.AddRange(ultrasoundOnly, mixed, labOnTarget, labLate);
        await db.SaveChangesAsync();

        var report = await Service(db, scope.SupervisorId).GetTurnaroundReportAsync(Day, Day, null);

        Assert.Equal(4, report.OrderCount);
        Assert.Equal(2, report.WithinTargetCount);
        Assert.Equal(50.0, report.WithinTargetPercent);

        Assert.Equal(new[] { labLate.Id, ultrasoundOnly.Id }, report.SlowestOrders.Select(order => order.OrderId));
        var slowest = report.SlowestOrders[0];
        Assert.Equal(61, slowest.TotalMinutes);
        Assert.Equal(60, slowest.TargetMinutes);
        Assert.Equal(labLate.OrderCode, slowest.OrderCode);
        Assert.Equal("Bệnh nhân TAT", slowest.PatientName);
        Assert.Equal(new[] { "Xét nghiệm máu" }, slowest.ServiceNames);
        Assert.Equal(DateTimeKind.Utc, slowest.OrderedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, slowest.CompletedAtUtc.Kind);
        Assert.Equal(T0, slowest.OrderedAtUtc);
        Assert.Equal(45, report.SlowestOrders[1].TargetMinutes);

        // Services are ordered by P90 descending; each row is judged against its own category target.
        Assert.Equal(new[] { lab.Code, ultrasound.Code }, report.ByService.Select(service => service.ServiceCode));
        var labRow = report.ByService[0];
        Assert.Equal("Laboratory", labRow.Category);
        Assert.Equal(3, labRow.OrderCount);
        Assert.Equal(60, labRow.MedianTotalMinutes);
        Assert.Equal(61, labRow.P90TotalMinutes);
        Assert.Equal(60, labRow.TargetMinutes);
        Assert.Equal(66.7, labRow.WithinTargetPercent);
        var ultrasoundRow = report.ByService[1];
        Assert.Equal(2, ultrasoundRow.OrderCount);
        Assert.Equal(45, ultrasoundRow.TargetMinutes);
        Assert.Equal(0.0, ultrasoundRow.WithinTargetPercent);

        // Configured targets are honoured.
        var relaxed = new DiagnosticTatOptions { TargetMinutes = new DiagnosticTatTargets { Ultrasound = 50 } };
        var relaxedReport = await Service(db, scope.SupervisorId, options: relaxed).GetTurnaroundReportAsync(Day, Day, null);
        Assert.Equal(3, relaxedReport.WithinTargetCount);
        Assert.Equal(75.0, relaxedReport.WithinTargetPercent);
        Assert.Equal(labLate.Id, Assert.Single(relaxedReport.SlowestOrders).OrderId);
    }

    [Fact]
    public async Task Report_FiltersByCategoryOfActiveItems()
    {
        using var serviceScope = Factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scope = await SeedScopeAsync(db);
        var lab = Catalog(DiagnosticCategory.Laboratory, "Nước tiểu");
        var ultrasound = Catalog(DiagnosticCategory.Ultrasound, "Siêu âm tuyến giáp");
        var labOnly = Completed(scope, T0, 20, null, Item(scope, lab));
        var ultrasoundOnly = Completed(scope, T0, 30, null, Item(scope, ultrasound));
        var mixed = Completed(scope, T0, 40, null, Item(scope, lab), Item(scope, ultrasound));
        var cancelledUltrasound = Completed(scope, T0, 50, null, Item(scope, lab), Item(scope, ultrasound, status: DiagnosticItemStatus.Cancelled));
        db.DiagnosticOrders.AddRange(labOnly, ultrasoundOnly, mixed, cancelledUltrasound);
        await db.SaveChangesAsync();
        var service = Service(db, scope.SupervisorId);

        var ultrasoundReport = await service.GetTurnaroundReportAsync(Day, Day, DiagnosticCategory.Ultrasound);
        Assert.Equal("Ultrasound", ultrasoundReport.Category);
        Assert.Equal(2, ultrasoundReport.OrderCount);
        var ultrasoundRow = Assert.Single(ultrasoundReport.ByService);
        Assert.Equal(ultrasound.Code, ultrasoundRow.ServiceCode);
        Assert.Equal(2, ultrasoundRow.OrderCount);
        Assert.Equal(35, ultrasoundRow.MedianTotalMinutes);

        var labReport = await service.GetTurnaroundReportAsync(Day, Day, DiagnosticCategory.Laboratory);
        Assert.Equal(3, labReport.OrderCount);
        Assert.Equal(lab.Code, Assert.Single(labReport.ByService).ServiceCode);

        Assert.Equal(0, (await service.GetTurnaroundReportAsync(Day, Day, DiagnosticCategory.Imaging)).OrderCount);
        Assert.Equal(4, (await service.GetTurnaroundReportAsync(Day, Day, null)).OrderCount);
    }

    [Fact]
    public async Task Report_RejectsInvertedOrTooLongRanges()
    {
        using var serviceScope = Factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scope = await SeedScopeAsync(db);
        var service = Service(db, scope.SupervisorId);

        var inverted = await Assert.ThrowsAsync<BusinessException>(() => service.GetTurnaroundReportAsync(Day, Day.AddDays(-1), null));
        Assert.Equal("VALIDATION_ERROR", inverted.ErrorCode);
        var tooLong = await Assert.ThrowsAsync<BusinessException>(() => service.GetTurnaroundReportAsync(Day, Day.AddDays(31), null));
        Assert.Equal("VALIDATION_ERROR", tooLong.ErrorCode);
        // 31 days inclusive is the maximum allowed.
        Assert.Equal(0, (await service.GetTurnaroundReportAsync(Day, Day.AddDays(30), null)).OrderCount);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(db, scope.SupervisorId, RoleNames.DiagnosticTechnician).GetTurnaroundReportAsync(Day, Day, null));
    }

    private async Task<string> CreateUserAsync(string role, Action<AppDbContext, Guid>? extra = null)
    {
        await RoleSeeder.SeedRolesAsync(Factory.Services);
        using var scope = Factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"tat-{role.ToLowerInvariant()}-{Code()[..12]}@test.com";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, FullName = "TAT " + role, PhoneNumber = Phone(), IsActive = true };
        Assert.True((await manager.CreateAsync(user, "Pass@123")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        if (extra != null)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            extra(db, user.Id);
            await db.SaveChangesAsync();
        }
        return email;
    }

    [Fact]
    public async Task Endpoint_AllowsLabSupervisorOnly_AndReturnsUtcTimestamps()
    {
        Scope scope;
        DiagnosticOrder late;
        using (var serviceScope = Factory.Services.CreateScope())
        {
            var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            scope = await SeedScopeAsync(db);
            var lab = Catalog(DiagnosticCategory.Laboratory, "Mỡ máu");
            late = Completed(scope, T0, 90, null, Item(scope, lab));
            db.DiagnosticOrders.AddRange(late, Completed(scope, T0, 30, null, Item(scope, lab)));
            await db.SaveChangesAsync();
        }
        var supervisorEmail = await CreateUserAsync(RoleNames.LabSupervisor, (db, userId) =>
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = userId, FacilityId = scope.Inside.Id, Role = RoleNames.LabSupervisor, IsActive = true }));
        var technicianEmail = await CreateUserAsync(RoleNames.DiagnosticTechnician);
        var doctorEmail = await CreateUserAsync(RoleNames.Doctor, (db, userId) => db.Doctors.Add(new Doctor { UserId = userId, IsActive = true }));

        var url = "/api/v1/lab-reports/turnaround?from=2040-03-10&to=2040-03-10&category=Laboratory";
        var supervisor = await CreateAuthenticatedClientAsync(supervisorEmail);
        var response = await supervisor.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var data = json.RootElement.GetProperty("data");
            Assert.Equal("2040-03-10", data.GetProperty("from").GetString());
            Assert.Equal("Laboratory", data.GetProperty("category").GetString());
            Assert.Equal(2, data.GetProperty("orderCount").GetInt32());
            Assert.Equal(4, data.GetProperty("segments").GetArrayLength());
            Assert.Equal(50.0, data.GetProperty("withinTargetPercent").GetDouble());
            var slow = Assert.Single(data.GetProperty("slowestOrders").EnumerateArray());
            Assert.Equal(late.Id, slow.GetProperty("orderId").GetInt64());
            Assert.EndsWith("Z", slow.GetProperty("orderedAtUtc").GetString());
            Assert.EndsWith("Z", slow.GetProperty("completedAtUtc").GetString());
        }

        // Missing dates default to the last 7 Vietnam days ending today.
        var today = Factory.Services.GetRequiredService<IDateTimeProvider>().VietnamToday;
        using (var json = JsonDocument.Parse(await (await supervisor.GetAsync("/api/v1/lab-reports/turnaround")).Content.ReadAsStringAsync()))
        {
            var data = json.RootElement.GetProperty("data");
            Assert.Equal(today.AddDays(-6).ToString("yyyy-MM-dd"), data.GetProperty("from").GetString());
            Assert.Equal(today.ToString("yyyy-MM-dd"), data.GetProperty("to").GetString());
        }

        var invalid = await supervisor.GetAsync("/api/v1/lab-reports/turnaround?from=2040-03-10&to=2040-03-09");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        using (var json = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()))
            Assert.Equal("VALIDATION_ERROR", json.RootElement.GetProperty("errorCode").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await (await CreateAuthenticatedClientAsync(technicianEmail)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await CreateAuthenticatedClientAsync(doctorEmail)).GetAsync(url)).StatusCode);
    }
}
