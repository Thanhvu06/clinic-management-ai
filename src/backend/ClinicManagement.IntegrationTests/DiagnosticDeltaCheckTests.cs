using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// Delta check against the patient's previous released results. Every test seeds its own facility, staff,
/// patient, service and parameters, and runs on a fixed far-future clock, so no shared seed data is touched.
/// </summary>
public class DiagnosticDeltaCheckTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateTime Now = new(2040, 6, 15, 3, 0, 0, DateTimeKind.Utc);
    private const string PercentCode = "DPCT";
    private const string AbsoluteCode = "DABS";
    private const string NoLimitCode = "DNOL";

    private sealed record Fixture(long FacilityId, Guid TechnicianId, Guid SupervisorId, Guid PatientUserId, string PatientEmail,
        long PatientId, long DoctorId, long ServiceId, Dictionary<string, DiagnosticServiceParameter> Parameters);

    private static string Code() => Guid.NewGuid().ToString("N");
    private static string Phone() => "09" + Random.Shared.NextInt64(100000000, 999999999);

    private async Task<Fixture> SeedAsync()
    {
        await RoleSeeder.SeedRolesAsync(Factory.Services);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var code = Code();
        var facility = new Facility { Code = "DLT-" + code[..20], Name = "Delta facility", IsActive = true };
        var technicianId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();
        var doctorUserId = Guid.NewGuid();
        db.Users.AddRange(
            new ApplicationUser { Id = technicianId, UserName = "dlt-tech-" + code, Email = "dlt-tech-" + code + "@test.com", PhoneNumber = Phone(), FullName = "Delta technician", IsActive = true },
            new ApplicationUser { Id = supervisorId, UserName = "dlt-sup-" + code, Email = "dlt-sup-" + code + "@test.com", PhoneNumber = Phone(), FullName = "Delta supervisor", IsActive = true },
            new ApplicationUser { Id = doctorUserId, UserName = "dlt-doc-" + code, Email = "dlt-doc-" + code + "@test.com", PhoneNumber = Phone(), FullName = "Delta doctor", IsActive = true });
        var patientEmail = "dlt-pat-" + code + "@test.com";
        var patientUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = patientEmail, Email = patientEmail, PhoneNumber = Phone(), FullName = "Bệnh nhân delta", IsActive = true };
        Assert.True((await users.CreateAsync(patientUser, "Pass@123")).Succeeded);
        Assert.True((await users.AddToRoleAsync(patientUser, RoleNames.Patient)).Succeeded);
        var doctor = new Doctor { UserId = doctorUserId, IsActive = true };
        var patient = new Patient { UserId = patientUser.Id, FullName = "Bệnh nhân delta", MedicalRecordNumber = "DLT-" + code[..20] };
        var service = new DiagnosticService
        {
            Code = "DLT-" + code[..20], Name = "Xét nghiệm delta", Category = DiagnosticCategory.Laboratory, IsActive = true,
            SpecimenType = "Máu", ContainerType = "Ống EDTA",
            Parameters = new List<DiagnosticServiceParameter>
            {
                new() { Code = PercentCode, Name = "Chỉ số phần trăm", Unit = "U/L", DeltaPercentLimit = 50m, DeltaWindowDays = 14, SortOrder = 0 },
                new() { Code = AbsoluteCode, Name = "Chỉ số tuyệt đối", Unit = "g/L", DeltaAbsoluteLimit = 20m, DeltaWindowDays = 14, SortOrder = 1 },
                new() { Code = NoLimitCode, Name = "Chỉ số không giới hạn", Unit = "mmol/L", SortOrder = 2 }
            }
        };
        db.Facilities.Add(facility);
        db.Doctors.Add(doctor);
        db.Patients.Add(patient);
        db.DiagnosticServices.Add(service);
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.AddRange(
            new StaffFacilityAssignment { UserId = technicianId, FacilityId = facility.Id, Role = RoleNames.DiagnosticTechnician, IsActive = true },
            new StaffFacilityAssignment { UserId = supervisorId, FacilityId = facility.Id, Role = RoleNames.LabSupervisor, IsActive = true });
        await db.SaveChangesAsync();
        return new Fixture(facility.Id, technicianId, supervisorId, patientUser.Id, patientEmail, patient.Id, doctor.Id, service.Id,
            service.Parameters.ToDictionary(p => p.Code));
    }

    private async Task<T> RunAsync<T>(Guid userId, string role, Func<DiagnosticWorkflowService, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns(userId);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        clock.SetupGet(x => x.VietnamToday).Returns(DateOnly.FromDateTime(Now.AddHours(7)));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role) }, "Test")) } };
        var service = new DiagnosticWorkflowService(db, Mock.Of<IDoctorContextService>(), user.Object, clock.Object,
            NullLogger<DiagnosticWorkflowService>.Instance, http);
        return await action(service);
    }

    private async Task<string> AddPreviousAsync(Fixture fixture, DateTime resultedAtUtc, (string Code, decimal Value)[] values,
        DiagnosticOrderStatus orderStatus = DiagnosticOrderStatus.Completed, DiagnosticItemStatus itemStatus = DiagnosticItemStatus.Completed,
        string? unitOverride = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderCode = "DLT-" + Code()[..24];
        db.DiagnosticOrders.Add(new DiagnosticOrder
        {
            OrderCode = orderCode, PatientId = fixture.PatientId, OrderingDoctorId = fixture.DoctorId, FacilityId = fixture.FacilityId,
            ClinicalIndication = "Lần trước", Status = orderStatus, OrderedAtUtc = resultedAtUtc.AddHours(-2),
            CompletedAtUtc = orderStatus == DiagnosticOrderStatus.Completed ? resultedAtUtc : null,
            Items = new List<DiagnosticOrderItem>
            {
                new()
                {
                    DiagnosticServiceId = fixture.ServiceId, Status = itemStatus, SpecimenStatus = SpecimenStatus.Received,
                    Result = new DiagnosticResult
                    {
                        ResultText = "Kết quả lần trước", ResultedAtUtc = resultedAtUtc, ResultedByUserId = fixture.TechnicianId,
                        Values = values.Select(v => new DiagnosticResultValue
                        {
                            DiagnosticServiceParameterId = fixture.Parameters[v.Code].Id, ParameterCode = v.Code,
                            ParameterName = fixture.Parameters[v.Code].Name, Unit = unitOverride ?? fixture.Parameters[v.Code].Unit,
                            NumericValue = v.Value, Flag = DiagnosticResultFlag.Normal
                        }).ToList()
                    }
                }
            }
        });
        await db.SaveChangesAsync();
        return orderCode;
    }

    private async Task<(long OrderId, long ItemId)> AddCurrentOrderAsync(Fixture fixture)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new DiagnosticOrder
        {
            OrderCode = "DLT-" + Code()[..24], PatientId = fixture.PatientId, OrderingDoctorId = fixture.DoctorId, FacilityId = fixture.FacilityId,
            ClinicalIndication = "Lần hiện tại", Status = DiagnosticOrderStatus.InProgress, OrderedAtUtc = Now.AddHours(-1),
            StartedAtUtc = Now.AddMinutes(-50), StartedByUserId = fixture.TechnicianId,
            Items = new List<DiagnosticOrderItem>
            {
                new() { DiagnosticServiceId = fixture.ServiceId, Status = DiagnosticItemStatus.InProgress, SpecimenStatus = SpecimenStatus.Received }
            }
        };
        db.DiagnosticOrders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, order.Items.Single().Id);
    }

    private Task<DiagnosticOrderDto> RecordAsync(Fixture fixture, long orderId, long itemId, decimal percent, decimal absolute, decimal noLimit) =>
        RunAsync(fixture.TechnicianId, RoleNames.DiagnosticTechnician, service => service.RecordItemResultAsync(orderId, itemId,
            new RecordDiagnosticResultRequest
            {
                Values = new List<DiagnosticResultValueInput>
                {
                    new() { ParameterId = fixture.Parameters[PercentCode].Id, Value = percent },
                    new() { ParameterId = fixture.Parameters[AbsoluteCode].Id, Value = absolute },
                    new() { ParameterId = fixture.Parameters[NoLimitCode].Id, Value = noLimit }
                }
            }));

    // Complete no longer submits for verification; this recreates an order that was already waiting for review before the step was removed.
    private async Task<DiagnosticOrderDto> SubmitLegacyAsync(Fixture fixture, long orderId)
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await db.DiagnosticOrders.FindAsync(orderId);
            order!.VerificationStatus = DiagnosticVerificationStatus.Pending; order.SubmittedForVerificationAtUtc = Now;
            order.SubmittedForVerificationByUserId = fixture.TechnicianId; order.RowVersion = Guid.NewGuid().ToByteArray();
            await db.SaveChangesAsync();
        }
        return await RunAsync(fixture.TechnicianId, RoleNames.DiagnosticTechnician, service => service.GetTechnicianOrderByIdAsync(orderId));
    }

    private static DiagnosticResultValueDto Value(DiagnosticOrderDto order, string code) =>
        order.Items.Single().Result!.Values.Single(v => v.ParameterCode == code);

    [Fact]
    public async Task PercentLimitExceeded_FlagsFailure_AndSnapshotsLatestPreviousResult()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-10), new[] { (PercentCode, 150m) });
        var latestCode = await AddPreviousAsync(fixture, Now.AddDays(-5), new[] { (PercentCode, 100m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var order = await RecordAsync(fixture, orderId, itemId, 160m, 1m, 1m);

        var value = Value(order, PercentCode);
        Assert.Equal(100m, value.PreviousValue);
        Assert.Equal(Now.AddDays(-5), value.PreviousResultedAtUtc);
        Assert.Equal(DateTimeKind.Utc, value.PreviousResultedAtUtc!.Value.Kind);
        Assert.Equal(latestCode, value.PreviousOrderCode);
        Assert.Equal(60m, value.DeltaChange);
        Assert.Equal(60m, value.DeltaPercent);
        Assert.True(value.IsDeltaFailure);
        Assert.True(order.HasDeltaFailure);
    }

    [Fact]
    public async Task AbsoluteLimitExceeded_FlagsFailure()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-3), new[] { (AbsoluteCode, 130m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var value = Value(await RecordAsync(fixture, orderId, itemId, 1m, 155m, 1m), AbsoluteCode);

        Assert.Equal(130m, value.PreviousValue);
        Assert.Equal(25m, value.DeltaChange);
        Assert.Equal(19.23m, value.DeltaPercent);
        Assert.True(value.IsDeltaFailure);
    }

    [Fact]
    public async Task WithinLimits_KeepsPreviousValue_WithoutFailure()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-2), new[] { (PercentCode, 100m), (AbsoluteCode, 130m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var order = await RecordAsync(fixture, orderId, itemId, 120m, 115m, 1m);

        Assert.Equal(100m, Value(order, PercentCode).PreviousValue);
        Assert.Equal(20m, Value(order, PercentCode).DeltaPercent);
        Assert.False(Value(order, PercentCode).IsDeltaFailure);
        Assert.Equal(-15m, Value(order, AbsoluteCode).DeltaChange);
        Assert.False(Value(order, AbsoluteCode).IsDeltaFailure);
        Assert.False(order.HasDeltaFailure);
    }

    [Fact]
    public async Task PreviousOutsideWindow_IsIgnored()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-20), new[] { (PercentCode, 10m), (AbsoluteCode, 10m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var order = await RecordAsync(fixture, orderId, itemId, 100m, 100m, 1m);

        foreach (var code in new[] { PercentCode, AbsoluteCode })
        {
            var value = Value(order, code);
            Assert.Null(value.PreviousValue);
            Assert.Null(value.PreviousResultedAtUtc);
            Assert.Null(value.PreviousOrderCode);
            Assert.Null(value.DeltaChange);
            Assert.Null(value.DeltaPercent);
            Assert.False(value.IsDeltaFailure);
        }
        Assert.False(order.HasDeltaFailure);
    }

    [Fact]
    public async Task UnreleasedOrderOrCancelledItem_IsIgnored()
    {
        var fixture = await SeedAsync();
        var released = await AddPreviousAsync(fixture, Now.AddDays(-6), new[] { (PercentCode, 100m) });
        await AddPreviousAsync(fixture, Now.AddDays(-2), new[] { (PercentCode, 10m) }, orderStatus: DiagnosticOrderStatus.InProgress);
        await AddPreviousAsync(fixture, Now.AddDays(-1), new[] { (PercentCode, 10m) }, itemStatus: DiagnosticItemStatus.Cancelled);
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var value = Value(await RecordAsync(fixture, orderId, itemId, 110m, 1m, 1m), PercentCode);

        Assert.Equal(released, value.PreviousOrderCode);
        Assert.Equal(100m, value.PreviousValue);
        Assert.False(value.IsDeltaFailure);
    }

    [Fact]
    public async Task DifferentUnit_IsIgnored()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-1), new[] { (PercentCode, 10m) }, unitOverride: "mg/dL");
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var value = Value(await RecordAsync(fixture, orderId, itemId, 100m, 1m, 1m), PercentCode);

        Assert.Null(value.PreviousValue);
        Assert.False(value.IsDeltaFailure);
    }

    [Fact]
    public async Task PreviousZero_LeavesPercentNull_ButStillChecksAbsolute()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-1), new[] { (PercentCode, 0m), (AbsoluteCode, 0m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var order = await RecordAsync(fixture, orderId, itemId, 50m, 25m, 1m);

        var percent = Value(order, PercentCode);
        Assert.Equal(0m, percent.PreviousValue);
        Assert.Null(percent.DeltaPercent);
        Assert.False(percent.IsDeltaFailure);
        var absolute = Value(order, AbsoluteCode);
        Assert.Equal(25m, absolute.DeltaChange);
        Assert.Null(absolute.DeltaPercent);
        Assert.True(absolute.IsDeltaFailure);
    }

    [Fact]
    public async Task TinyPreviousValue_CapsStoredPercent_AndStillFlags()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-1), new[] { (PercentCode, 0.0001m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var value = Value(await RecordAsync(fixture, orderId, itemId, 500_000m, 1m, 1m), PercentCode);

        Assert.Equal(9_999_999.99m, value.DeltaPercent);
        Assert.True(value.IsDeltaFailure);
    }

    [Fact]
    public async Task ParameterWithoutLimits_ShowsPrevious_ButNeverFails()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-25), new[] { (NoLimitCode, 1m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var value = Value(await RecordAsync(fixture, orderId, itemId, 1m, 1m, 100m), NoLimitCode);

        Assert.Equal(1m, value.PreviousValue);
        Assert.Equal(99m, value.DeltaChange);
        Assert.Equal(9900m, value.DeltaPercent);
        Assert.False(value.IsDeltaFailure);
    }

    [Fact]
    public async Task ReenteringResult_RecalculatesSnapshot()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-5), new[] { (PercentCode, 100m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);

        var first = Value(await RecordAsync(fixture, orderId, itemId, 160m, 1m, 1m), PercentCode);
        Assert.True(first.IsDeltaFailure);

        var newer = await AddPreviousAsync(fixture, Now.AddDays(-1), new[] { (PercentCode, 150m) });
        var order = await RecordAsync(fixture, orderId, itemId, 110m, 1m, 1m);

        var second = Value(order, PercentCode);
        Assert.Equal(150m, second.PreviousValue);
        Assert.Equal(newer, second.PreviousOrderCode);
        Assert.Equal(-40m, second.DeltaChange);
        Assert.Equal(-26.67m, second.DeltaPercent);
        Assert.False(second.IsDeltaFailure);
        Assert.False(order.HasDeltaFailure);
    }

    [Fact]
    public async Task VerifyingDeltaFailure_RequiresNote()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-5), new[] { (PercentCode, 100m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);
        await RecordAsync(fixture, orderId, itemId, 160m, 1m, 1m);
        var submitted = await SubmitLegacyAsync(fixture, orderId);
        Assert.Equal("Pending", submitted.VerificationStatus);
        Assert.True(submitted.HasDeltaFailure);

        foreach (var note in new string?[] { null, "    ", " abc " })
        {
            var error = await Assert.ThrowsAsync<BusinessException>(() => RunAsync(fixture.SupervisorId, RoleNames.LabSupervisor,
                service => service.VerifyOrderAsync(orderId, new VerifyDiagnosticOrderRequest { Note = note, RowVersion = submitted.RowVersion })));
            Assert.Equal("DELTA_NOTE_REQUIRED", error.ErrorCode);
        }

        var verified = await RunAsync(fixture.SupervisorId, RoleNames.LabSupervisor,
            service => service.VerifyOrderAsync(orderId, new VerifyDiagnosticOrderRequest { Note = "Đã gọi lại khoa, diễn tiến lâm sàng", RowVersion = submitted.RowVersion }));
        Assert.Equal("Completed", verified.Status);
        Assert.Equal("Verified", verified.VerificationStatus);
    }

    [Fact]
    public async Task VerifyingWithoutDeltaFailure_DoesNotRequireNote()
    {
        var fixture = await SeedAsync();
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);
        await RecordAsync(fixture, orderId, itemId, 160m, 1m, 1m);
        var submitted = await SubmitLegacyAsync(fixture, orderId);

        var verified = await RunAsync(fixture.SupervisorId, RoleNames.LabSupervisor,
            service => service.VerifyOrderAsync(orderId, new VerifyDiagnosticOrderRequest { RowVersion = submitted.RowVersion }));
        Assert.Equal("Completed", verified.Status);
    }

    [Fact]
    public async Task PatientApi_NeverExposesDeltaFields_EvenWhenPublished()
    {
        var fixture = await SeedAsync();
        await AddPreviousAsync(fixture, Now.AddDays(-5), new[] { (PercentCode, 100m), (AbsoluteCode, 100m), (NoLimitCode, 1m) });
        var (orderId, itemId) = await AddCurrentOrderAsync(fixture);
        await RecordAsync(fixture, orderId, itemId, 160m, 150m, 5m);
        var submitted = await SubmitLegacyAsync(fixture, orderId);
        var verified = await RunAsync(fixture.SupervisorId, RoleNames.LabSupervisor,
            service => service.VerifyOrderAsync(orderId, new VerifyDiagnosticOrderRequest { Note = "Đã kiểm tra mẫu", RowVersion = submitted.RowVersion }));
        Assert.True(verified.HasDeltaFailure);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.DiagnosticOrders.SingleAsync(o => o.Id == orderId);
            entity.ReviewedAtUtc = Now;
            entity.ReviewedByDoctorId = fixture.DoctorId;
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(fixture.PatientEmail);
        var detailResponse = await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{orderId}");
        var detailText = await detailResponse.Content.ReadAsStringAsync();
        Assert.True(detailResponse.StatusCode == HttpStatusCode.OK, detailText);
        var listResponse = await Client.GetAsync("/api/v1/patients/me/diagnostic-orders?pageSize=100");
        var listText = await listResponse.Content.ReadAsStringAsync();
        Assert.True(listResponse.StatusCode == HttpStatusCode.OK, listText);

        using var detail = JsonDocument.Parse(detailText);
        using var list = JsonDocument.Parse(listText);
        var orders = new[]
        {
            detail.RootElement.GetProperty("data"),
            list.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Single(o => o.GetProperty("id").GetInt64() == orderId)
        };
        foreach (var order in orders)
        {
            Assert.False(order.GetProperty("hasDeltaFailure").GetBoolean());
            var values = order.GetProperty("items")[0].GetProperty("result").GetProperty("values").EnumerateArray().ToList();
            Assert.Equal(3, values.Count);
            foreach (var value in values)
            {
                foreach (var field in new[] { "previousValue", "previousResultedAtUtc", "previousOrderCode", "deltaChange", "deltaPercent" })
                    Assert.Equal(JsonValueKind.Null, value.GetProperty(field).ValueKind);
                Assert.False(value.GetProperty("isDeltaFailure").GetBoolean());
            }
        }
    }
}
