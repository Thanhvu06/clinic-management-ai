using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Diagnostics;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DiagnosticDashboardStatsTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateTime Now = new(2035, 1, 2, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayStart = new(2035, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    private async Task<(Facility A, Facility B, Guid UserId)> SeedScopeAsync(AppDbContext db, string role)
    {
        var code = Guid.NewGuid().ToString("N");
        var a = new Facility { Code = "A-" + code, Name = "Scope A", IsActive = true };
        var b = new Facility { Code = "B-" + code, Name = "Scope B", IsActive = true };
        var userId = role == "Doctor" ? DoctorId : Guid.NewGuid();
        if (role != "Doctor") db.Users.Add(new ApplicationUser { Id = userId, UserName = code, Email = code + "@test.com", PhoneNumber = "09" + Random.Shared.NextInt64(100000000, 999999999), FullName = "Dashboard reviewer", IsActive = true });
        db.Facilities.AddRange(a, b);
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = userId, FacilityId = a.Id, Role = role, IsActive = true });
        // A different role and an inactive assignment must not widen the scope.
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = userId, FacilityId = b.Id, Role = "Receptionist", IsActive = true });
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = userId, FacilityId = b.Id, Role = role, IsActive = false });
        await db.SaveChangesAsync();
        return (a, b, userId);
    }

    private DiagnosticWorkflowService Service(AppDbContext db, Guid userId, string role)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns(userId);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        clock.SetupGet(x => x.VietnamToday).Returns(new DateOnly(2035, 1, 2));
        clock.Setup(x => x.ConvertVietnamToUtc(It.IsAny<DateTime>()))
            .Returns((DateTime value) => DateTime.SpecifyKind(value.AddHours(-7), DateTimeKind.Utc));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role) }, "Test")) } };
        return new(db, Mock.Of<IDoctorContextService>(), user.Object, clock.Object, NullLogger<DiagnosticWorkflowService>.Instance, http);
    }

    private DiagnosticOrder Order(Facility facility, DiagnosticOrderStatus status = DiagnosticOrderStatus.InProgress,
        DiagnosticVerificationStatus verification = DiagnosticVerificationStatus.Pending, long? doctorId = null) => new()
    {
        FacilityId = facility.Id, PatientId = Patient1EntityId, OrderingDoctorId = doctorId ?? Doctor2EntityId,
        OrderCode = "DASH-" + Guid.NewGuid().ToString("N"), Status = status, VerificationStatus = verification,
        OrderedAtUtc = Now.AddMinutes(-90), SubmittedForVerificationAtUtc = Now.AddMinutes(-45)
    };

    [Theory]
    [InlineData(DiagnosticOrderStatus.Ordered)]
    [InlineData(DiagnosticOrderStatus.InProgress)]
    public async Task ActiveTechnicianOrders_AreOldestFirst_WithIdTieBreakAndPagination(DiagnosticOrderStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (a, b, user) = await SeedScopeAsync(db, "DiagnosticTechnician");
        var newest = Order(a, status); newest.OrderedAtUtc = Now;
        var oldest = Order(a, status); oldest.OrderedAtUtc = Now.AddHours(-3);
        var tied = Order(a, status); tied.OrderedAtUtc = oldest.OrderedAtUtc;
        db.DiagnosticOrders.AddRange(newest, oldest, tied, Order(b, status));
        await db.SaveChangesAsync();
        var service = Service(db, user, "DiagnosticTechnician");
        var first = await service.GetTechnicianOrdersAsync(status, null, null, 1, 2);
        Assert.Equal(3, first.TotalItems);
        Assert.Equal(new[] { oldest.Id, tied.Id }.OrderBy(id => id), first.Items.Select(x => x.Id));
        Assert.Equal(newest.Id, Assert.Single((await service.GetTechnicianOrdersAsync(status, null, null, 2, 2)).Items).Id);
    }

    [Fact]
    public async Task TechnicianStats_CountPendingReturnedAndStrictlyOverThirtyMinutes_WithinFacility()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (a, b, user) = await SeedScopeAsync(db, "DiagnosticTechnician");
        var pending = Order(a);
        var returned = Order(a, verification: DiagnosticVerificationStatus.Returned);
        var boundary = Order(a, DiagnosticOrderStatus.Ordered); boundary.OrderedAtUtc = Now.AddMinutes(-30);
        var older = Order(a, DiagnosticOrderStatus.Ordered); older.OrderedAtUtc = Now.AddMinutes(-31);
        var completed = Order(a, DiagnosticOrderStatus.Completed); completed.CompletedAtUtc = Now;
        db.DiagnosticOrders.AddRange(pending, returned, boundary, older, completed,
            Order(a, DiagnosticOrderStatus.Cancelled), Order(b), Order(b, verification: DiagnosticVerificationStatus.Returned));
        await db.SaveChangesAsync();
        var stats = await Service(db, user, "DiagnosticTechnician").GetTechnicianStatsAsync();
        Assert.Equal(1, stats.PendingVerificationCount);
        Assert.Equal(1, stats.ReturnedCount);
        Assert.Equal(3, stats.OverdueCount);
        Assert.Equal(2, stats.OrderedCount);
        Assert.Equal(2, stats.InProgressCount);
        Assert.Equal(1, stats.CompletedTodayCount);
    }

    [Theory]
    [InlineData("LabSupervisor")]
    [InlineData("Doctor")]
    public async Task VerificationStats_UseQueueScope_ExcludeDoctorsOwnOrders_AndVietnamDayBoundaries(string role)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (a, b, user) = await SeedScopeAsync(db, role);
        var critical = Order(a); critical.SubmittedForVerificationAtUtc = Now.AddMinutes(-75);
        critical.Items.Add(new DiagnosticOrderItem { Status = DiagnosticItemStatus.Completed,
            DiagnosticService = new DiagnosticService { Code = Guid.NewGuid().ToString("N"), Name = "Critical" },
            Result = new DiagnosticResult { HasCriticalValue = true, ResultedByUserId = TechnicianId } });
        var boundary = Order(a); boundary.SubmittedForVerificationAtUtc = Now.AddMinutes(-30);
        boundary.Items.Add(new DiagnosticOrderItem { Status = DiagnosticItemStatus.Cancelled,
            DiagnosticService = new DiagnosticService { Code = Guid.NewGuid().ToString("N"), Name = "Cancelled critical" },
            Result = new DiagnosticResult { HasCriticalValue = true, ResultedByUserId = TechnicianId } });
        var own = Order(a, doctorId: DoctorEntityId);
        db.DiagnosticOrders.AddRange(critical, boundary, own, Order(b));
        // Count events within [VN midnight, next VN midnight), including returned/resubmitted orders.
        foreach (var instant in new[] { DayStart.AddTicks(-1), DayStart, Now, DayStart.AddDays(1) })
        {
            var returned = Order(a, DiagnosticOrderStatus.InProgress, DiagnosticVerificationStatus.Returned);
            returned.ReturnedAtUtc = instant;
            var verified = Order(a, DiagnosticOrderStatus.Completed, DiagnosticVerificationStatus.Verified);
            verified.VerifiedAtUtc = instant;
            db.DiagnosticOrders.AddRange(returned, verified);
        }
        var outside = Order(b); outside.ReturnedAtUtc = Now; outside.VerifiedAtUtc = Now;
        var ownEvents = Order(a, DiagnosticOrderStatus.Completed, doctorId: DoctorEntityId);
        ownEvents.ReturnedAtUtc = Now; ownEvents.VerifiedAtUtc = Now;
        db.DiagnosticOrders.AddRange(outside, ownEvents);
        await db.SaveChangesAsync();
        var service = Service(db, user, role);
        var stats = await service.GetVerificationStatsAsync();
        Assert.Equal(role == "Doctor" ? 2 : 3, stats.PendingCount);
        Assert.Equal((await service.GetVerificationQueueAsync(1, 100)).TotalItems, stats.PendingCount);
        Assert.Equal(1, stats.CriticalPendingCount);
        Assert.Equal(role == "Doctor" ? 1 : 2, stats.OverduePendingCount);
        Assert.Equal(75, stats.OldestPendingMinutes);
        Assert.Equal(role == "Doctor" ? 2 : 3, stats.ReturnedTodayCount);
        Assert.Equal(role == "Doctor" ? 2 : 3, stats.VerifiedTodayCount);
    }

    [Fact]
    public async Task VerificationStats_EmptyScope_ReturnsZeros()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, _, user) = await SeedScopeAsync(db, "LabSupervisor");
        var stats = await Service(db, user, "LabSupervisor").GetVerificationStatsAsync();
        Assert.Equal(0, stats.PendingCount);
        Assert.Equal(0, stats.CriticalPendingCount);
        Assert.Equal(0, stats.OverduePendingCount);
        Assert.Equal(0, stats.OldestPendingMinutes);
        Assert.Equal(0, stats.ReturnedTodayCount);
        Assert.Equal(0, stats.VerifiedTodayCount);
    }

    [Fact]
    public async Task VerificationStatsEndpoint_ReturnsAuthorizedStats()
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync("/api/v1/diagnostic-verification/stats");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var stats = JsonSerializer.Deserialize<VerificationStatsDto>(json.RootElement.GetProperty("data"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(stats);
        Assert.True(stats.PendingCount >= 0);
    }
}
