using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Planning;
using ClinicManagement.Infrastructure.AI.Tools;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.IntegrationTests;

/// <summary>S1: reception.get_upcoming_appointments is a facility-scoped read tool.</summary>
public sealed class UpcomingAppointmentsToolTests : IntegrationTestBase
{
    private const string ToolName = "reception.get_upcoming_appointments";
    private static readonly FixedClock Clock = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _minute;

    public UpcomingAppointmentsToolTests(CustomWebApplicationFactory factory) : base(factory) { }

    public static IEnumerable<object[]> ForeignRoles => Enum.GetValues<AiActorRole>()
        .Where(role => role != AiActorRole.Receptionist).Select(role => new object[] { role });

    [Fact]
    public void Tool_is_wired_into_catalog_planner_allowlist_and_receptionist_only()
    {
        var definition = Assert.Single(AiRoleToolCatalog.Definitions, x => x.Name == ToolName);
        Assert.Equal(new[] { AiActorRole.Receptionist }, definition.AllowedRoles);
        Assert.Equal(AiToolRiskLevel.Low, definition.RiskLevel);
        Assert.Equal(AiToolConfirmationRequirement.None, definition.Confirmation);
        Assert.Empty(definition.ArgumentSchema);
        Assert.True(AiPlannerPolicy.IsAllowed(ToolName));
    }

    [Fact]
    public async Task Returns_tomorrow_through_day_seven_in_scope_holding_statuses_sorted_with_today_card_shape()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var today = Clock.VietnamToday;
        var dayOneLate = Appointment(data.Facility.Id, today.AddDays(1), new TimeOnly(10, 0));
        var dayOneEarly = Appointment(data.Facility.Id, today.AddDays(1), new TimeOnly(8, 0));
        var daySeven = Appointment(data.Facility.Id, today.AddDays(7), new TimeOnly(7, 0));
        var pending = Appointment(data.Facility.Id, today.AddDays(3), new TimeOnly(9, 0), AppointmentStatus.Pending);
        var excluded = new[]
        {
            Appointment(data.Facility.Id, today, new TimeOnly(9, 0)),
            Appointment(data.Facility.Id, today.AddDays(8), new TimeOnly(9, 0)),
            Appointment(data.Facility.Id, today.AddDays(-1), new TimeOnly(9, 0)),
            Appointment(data.ForeignFacility.Id, today.AddDays(2), new TimeOnly(9, 0)),
            Appointment(null, today.AddDays(2), new TimeOnly(9, 0)),
            Appointment(data.Facility.Id, today.AddDays(2), new TimeOnly(9, 0), AppointmentStatus.Cancelled),
            Appointment(data.Facility.Id, today.AddDays(2), new TimeOnly(9, 0), AppointmentStatus.Completed),
            Appointment(data.Facility.Id, today.AddDays(2), new TimeOnly(9, 0), AppointmentStatus.NoShow)
        };
        db.Appointments.AddRange(new[] { daySeven, dayOneLate, pending, dayOneEarly }.Concat(excluded));
        await db.SaveChangesAsync();

        var result = await Executor(scope, db, AiActorRole.Receptionist, ReceptionistId, data.Facility.Id)
            .ExecuteAsync(Invocation());

        Assert.True(result.Status == "completed", JsonSerializer.Serialize(result.Error));
        Assert.Equal("reception_appointments", result.ResultType);
        var rows = JsonSerializer.SerializeToElement(result.Data, JsonOptions).EnumerateArray().ToArray();
        Assert.Equal(new[] { dayOneEarly.AppointmentCode, dayOneLate.AppointmentCode, pending.AppointmentCode, daySeven.AppointmentCode },
            rows.Select(row => row.GetProperty("appointmentCode").GetString()));
        Assert.All(rows, row => Assert.Equal(
            new[] { "appointmentCode", "appointmentDate", "doctorName", "endTime", "id", "patientName", "specialtyId", "startTime", "status" },
            row.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
        Assert.Equal(today.AddDays(1).ToString("yyyy-MM-dd"), rows[0].GetProperty("appointmentDate").GetString());
        Assert.Equal(today.AddDays(7).ToString("yyyy-MM-dd"), rows[^1].GetProperty("appointmentDate").GetString());
        var card = Assert.Single(new AiGroundedResponseComposer().Compose(new(), [result]).Cards);
        Assert.Equal("reception_appointments", card.Type);
    }

    [Fact]
    public async Task Caps_results_at_one_hundred_after_filtering_in_the_database()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        for (var i = 0; i < 103; i++)
            db.Appointments.Add(Appointment(data.Facility.Id, Clock.VietnamToday.AddDays(2), new TimeOnly(6, 0)));
        // Out-of-scope rows sort first and would displace in-scope rows if filtered after Take.
        for (var i = 0; i < 20; i++)
            db.Appointments.Add(Appointment(data.ForeignFacility.Id, Clock.VietnamToday.AddDays(1), new TimeOnly(5, 0)));
        await db.SaveChangesAsync();

        var result = await Executor(scope, db, AiActorRole.Receptionist, ReceptionistId, data.Facility.Id).ExecuteAsync(Invocation());

        Assert.True(result.Status == "completed", JsonSerializer.Serialize(result.Error));
        var rows = JsonSerializer.SerializeToElement(result.Data, JsonOptions).EnumerateArray().ToArray();
        Assert.Equal(100, rows.Length);
        Assert.All(rows, row => Assert.Equal(Clock.VietnamToday.AddDays(2).ToString("yyyy-MM-dd"), row.GetProperty("appointmentDate").GetString()));
    }

    [Theory]
    [MemberData(nameof(ForeignRoles))]
    public async Task Other_roles_are_denied_before_execution(AiActorRole role)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await Executor(scope, db, role, Actor(role), null).ExecuteAsync(Invocation());
        Assert.Equal("FORBIDDEN_TOOL", result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Unassigned_facility_fails_closed_and_arguments_are_closed()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var missing = await Executor(scope, db, AiActorRole.Receptionist, ReceptionistId, long.MaxValue).ExecuteAsync(Invocation());
        // Fails closed either at capability resolution or at the tool's facility scope.
        Assert.Contains(missing.Error?.Code, new[] { "FORBIDDEN_CAPABILITY", "FACILITY_SCOPE_REQUIRED" });
        Assert.Null(missing.Data);
        foreach (var (argument, code) in new[] { ("facilityId", "FORBIDDEN_TOOL_ARGUMENT"), ("fromDate", "UNKNOWN_TOOL_ARGUMENT") })
        {
            var rejected = await Executor(scope, db, AiActorRole.Receptionist, ReceptionistId, data.Facility.Id)
                .ExecuteAsync(Invocation(JsonSerializer.Serialize(new Dictionary<string, object> { [argument] = "2026-10-03" })));
            Assert.Equal(code, rejected.Error?.Code);
            Assert.Null(rejected.Data);
        }
    }

    private AiToolExecutor Executor(IServiceScope scope, AppDbContext db, AiActorRole role, Guid actor, long? facility)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns(actor);
        var dispatcher = new RoleCopilotToolHandler(db, currentUser.Object, Clock, Mock.Of<IPharmacyService>());
        var patient = scope.ServiceProvider.GetRequiredService<PatientCopilotToolHandler>();
        var registry = new AiToolRegistry(AiRoleToolCatalog.Definitions.Select(d => new AiToolHandlerAdapter(dispatcher, d))
            .Concat(PatientCopilotToolHandler.Definitions().Select(d => new AiToolHandlerAdapter(patient, d))));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actor.ToString()), new(ClaimTypes.Role, role.ToString()) };
        if (facility.HasValue) claims.Add(new Claim("facility_id", facility.Value.ToString()));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };
        return new AiToolExecutor(registry, new AiCapabilityResolver(db), currentUser.Object, accessor,
            Mock.Of<IAiAuditService>(), NullLogger<AiToolExecutor>.Instance);
    }

    private static async Task<(Facility Facility, Facility ForeignFacility)> SeedScopeAsync(AppDbContext db)
    {
        var facility = new Facility { Code = Code(), Name = "Upcoming allowed", Address = "Test", IsActive = true };
        var foreign = new Facility { Code = Code(), Name = "Upcoming foreign", Address = "Test", IsActive = true };
        db.Facilities.AddRange(facility, foreign);
        await db.SaveChangesAsync();
        foreach (var role in Enum.GetValues<AiActorRole>().Where(r => r != AiActorRole.Patient))
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = Actor(role), FacilityId = facility.Id, Role = role.ToString() });
        await db.SaveChangesAsync();
        return (facility, foreign);
    }

    private static Appointment Appointment(long? facility, DateOnly date, TimeOnly start, AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        // Unique slot times keep the appointments independent of slot uniqueness rules.
        var time = start.AddMinutes(Interlocked.Increment(ref _minute) % 50);
        return new Appointment
        {
            AppointmentCode = Code(), PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = SlotEntityId, FacilityId = facility, AppointmentDate = date,
            StartTime = time, EndTime = time.AddMinutes(5), Status = status, Reason = "Checkup"
        };
    }

    private static Guid Actor(AiActorRole role) => role switch
    {
        AiActorRole.Admin => AdminId, AiActorRole.Doctor => DoctorId, AiActorRole.Receptionist => ReceptionistId,
        AiActorRole.Pharmacist => PharmacistId, AiActorRole.DiagnosticTechnician => TechnicianId, _ => Patient1Id
    };

    private static string Code() => Guid.NewGuid().ToString("N");
    private static AiToolInvocation Invocation(string arguments = "{}") => new() { ToolName = ToolName, ArgumentsJson = arguments };

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 10, 2, 17, 10, 0, DateTimeKind.Utc);
        public TimeZoneInfo VietnamTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Test UTC+7", TimeSpan.FromHours(7), "Test UTC+7", "Test UTC+7");
        public DateTime VietnamNow => ConvertUtcToVietnam(UtcNow);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public DateTime ConvertUtcToVietnam(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), VietnamTimeZone);
        public DateTime ConvertVietnamToUtc(DateTime value) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), VietnamTimeZone);
    }
}
