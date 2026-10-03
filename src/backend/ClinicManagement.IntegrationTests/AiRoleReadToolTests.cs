using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
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

public sealed class AiRoleReadToolTests : IntegrationTestBase
{
    private static readonly FixedClock Clock = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly (string Name, AiActorRole Role, string Type, string Intent)[] Tools =
    [
        ("admin.get_revenue_summary", AiActorRole.Admin, "admin_revenue_summary", "AdminMetrics"),
        ("reception.get_pending_payments", AiActorRole.Receptionist, "reception_pending_payments", "QueueLookup"),
        ("doctor.get_my_appointments_today", AiActorRole.Doctor, "doctor_appointments_today", "ViewAppointments"),
        ("technician.get_completed_today", AiActorRole.DiagnosticTechnician, "technician_completed_today", "DiagnosticLookup"),
        ("pharmacist.get_low_stock", AiActorRole.Pharmacist, "pharmacy_low_stock", "PharmacyInventory"),
        ("patient.get_my_invoices", AiActorRole.Patient, "patient_invoices", "ViewAppointments")
    ];

    public AiRoleReadToolTests(CustomWebApplicationFactory factory) : base(factory) { }

    public static IEnumerable<object[]> EachTool => Tools.Select(t => new object[] { t.Name, t.Role, t.Type, t.Intent });
    public static IEnumerable<object[]> ToolRoles => Tools.Select(t => new object[] { t.Name, t.Role });
    public static IEnumerable<object[]> ForeignRoles => Tools.SelectMany(t => Enum.GetValues<AiActorRole>()
        .Where(r => r != t.Role).Select(r => new object[] { t.Name, r }));
    public static IEnumerable<object[]> RejectedArguments => Tools.SelectMany(t =>
        new[] { "userId", "actorId", "role", "facilityId", "facilityAuthorization", "unknown", "patientId", "fromDate" }
            .Select(arg => new object[] { t.Name, t.Role, arg, arg is "unknown" or "patientId" or "fromDate" ? "UNKNOWN_TOOL_ARGUMENT" : "FORBIDDEN_TOOL_ARGUMENT" }));

    [Theory]
    [MemberData(nameof(EachTool))]
    public async Task Read_tool_is_registered_role_scoped_and_reachable_through_existing_provider_planner(
        string name, AiActorRole role, string resultType, string intent)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var expected = await SeedToolDataAsync(db, data, name);
        var executor = Executor(scope, db, role, Actor(role), data.Facility.Id);

        // Use the existing synthetic HTTP provider fixture: no real LLM call,
        // new intent label, deterministic rule or alternate execution path.
        using var http = new AiRolePlannerContractTests.CapturingHandler(_ => AiRolePlannerContractTests.Envelope(
            AiRolePlannerContractTests.PlannerJson(intent, AiRolePlannerContractTests.ToolCall(name, Arguments(name)))));
        var plan = await AiRolePlannerContractTests.CreatePlanner(http).PlanAsync(AiRolePlannerContractTests.Request(role, "synthetic"));
        Assert.True(plan.IsSuccess, JsonSerializer.Serialize(plan.Diagnostic));
        Assert.Contains(name, Assert.Single(http.Bodies));
        var definitions = AiRolePlannerContractTests.ToolsFor(role);
        var binding = AiToolBindingRegistry.ValidateAndBindPlan(plan.Decision.ToolCalls, definitions, new AiResolvedResourceContext());
        Assert.True(binding.IsValid);
        var result = Assert.Single(await executor.ExecutePlannerPlanAsync(binding.BoundCalls, null));
        Assert.True(result.Status == "completed", JsonSerializer.Serialize(result.Error));
        Assert.Equal(resultType, result.ResultType);
        var json = JsonSerializer.SerializeToElement(result.Data, JsonOptions);
        if (role == AiActorRole.Admin)
        {
            Assert.Equal(123.45m, json.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(1, json.GetProperty("invoiceCount").GetInt32());
            Assert.DoesNotContain("patient", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(expected, json.GetRawText());
            Assert.DoesNotContain(data.ForeignCode, json.GetRawText());
            Assert.DoesNotContain("private clinical note", json.GetRawText());
            var fields = role switch
            {
                AiActorRole.Receptionist => new[] { "invoiceCode", "patientName", "totalAmount", "status", "createdAtUtc" },
                AiActorRole.Doctor => new[] { "appointmentCode", "startTime", "endTime", "status", "patientName", "reason" },
                AiActorRole.DiagnosticTechnician => new[] { "orderCode", "services", "status", "completedAtUtc" },
                AiActorRole.Pharmacist => new[] { "code", "name", "unit", "stockQuantity", "reorderLevel" },
                _ => new[] { "invoiceCode", "status", "totalAmount", "createdAtUtc", "paidAtUtc" }
            };
            Assert.All(json.EnumerateArray(), row => Assert.Equal(fields.Order(), row.EnumerateObject().Select(p => p.Name).Order()));
            if (role is AiActorRole.Receptionist or AiActorRole.Doctor or AiActorRole.DiagnosticTechnician)
                Assert.Single(json.EnumerateArray());
            if (role == AiActorRole.Pharmacist)
                Assert.All(json.EnumerateArray(), row => Assert.True(row.GetProperty("stockQuantity").GetInt32() <= row.GetProperty("reorderLevel").GetInt32()));
        }
        if (role == AiActorRole.Pharmacist)
            Assert.Contains("chưa phân tách tồn kho theo cơ sở", result.DisplayText);
        var card = Assert.Single(new AiGroundedResponseComposer().Compose(plan.Decision, [result]).Cards);
        Assert.Equal(resultType, card.Type);
        Assert.Same(result.Data, card.Data);
        Assert.Equal(Clock.VietnamToday.AddDays(-1), DateOnly.FromDateTime(Clock.UtcNow));
    }

    [Theory]
    [MemberData(nameof(ForeignRoles))]
    public async Task Every_other_role_is_denied_before_execution(string name, AiActorRole role)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await Executor(scope, db, role, Actor(role), null).ExecuteAsync(Invocation(name));
        Assert.Equal("FORBIDDEN_TOOL", result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Theory]
    [MemberData(nameof(RejectedArguments))]
    public async Task Closed_arguments_reject_unknown_and_client_supplied_authority(string name, AiActorRole role, string argument, string error)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var args = new Dictionary<string, object> { [argument] = "untrusted" };
        if (role == AiActorRole.Admin) args["period"] = "today";
        var result = await Executor(scope, db, role, Actor(role), data.Facility.Id).ExecuteAsync(Invocation(name, JsonSerializer.Serialize(args)));
        Assert.Equal(error, result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Theory]
    [MemberData(nameof(ToolRoles))]
    public async Task Missing_facility_or_patient_owner_fails_closed(string name, AiActorRole role)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = role == AiActorRole.Patient ? Guid.NewGuid() : Actor(role);
        var result = await Executor(scope, db, role, actor, long.MaxValue).ExecuteAsync(Invocation(name));
        Assert.Equal("FACILITY_SCOPE_REQUIRED", result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("today", "2026-10-02T17:00:00Z", "2026-10-03T17:00:00Z")]
    [InlineData("yesterday", "2026-10-01T17:00:00Z", "2026-10-02T17:00:00Z")]
    [InlineData("last_7_days", "2026-09-26T17:00:00Z", "2026-10-03T17:00:00Z")]
    [InlineData("this_month", "2026-09-30T17:00:00Z", "2026-10-31T17:00:00Z")]
    [InlineData("last_month", "2026-08-31T17:00:00Z", "2026-09-30T17:00:00Z")]
    public async Task Revenue_periods_use_paid_timestamp_and_inclusive_exclusive_Vietnam_boundaries(string period, string from, string until)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var start = DateTime.Parse(from, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var end = DateTime.Parse(until, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var visit = Visit(data.Facility.Id, data.Department.Id);
        var foreign = Visit(data.ForeignFacility.Id, data.ForeignDepartment.Id);
        db.PatientVisits.AddRange(visit, foreign);
        await db.SaveChangesAsync();
        db.Invoices.AddRange(
            Invoice(visit.Id, InvoiceStatus.Paid, start, 10.01m),
            Invoice(visit.Id, InvoiceStatus.Paid, end.AddTicks(-1), 20.02m),
            Invoice(visit.Id, InvoiceStatus.Paid, start.AddTicks(-1), 100m),
            Invoice(visit.Id, InvoiceStatus.Paid, end, 100m),
            Invoice(visit.Id, InvoiceStatus.Unpaid, start, 100m),
            Invoice(visit.Id, InvoiceStatus.Cancelled, start, 100m),
            Invoice(visit.Id, InvoiceStatus.Paid, null, 100m),
            Invoice(foreign.Id, InvoiceStatus.Paid, start, 100m),
            Invoice(null, InvoiceStatus.Paid, start, 100m));
        await db.SaveChangesAsync();
        var result = await Executor(scope, db, AiActorRole.Admin, AdminId, data.Facility.Id)
            .ExecuteAsync(Invocation("admin.get_revenue_summary", JsonSerializer.Serialize(new { period })));
        Assert.Equal("completed", result.Status);
        var json = JsonSerializer.SerializeToElement(result.Data, JsonOptions);
        Assert.Equal(30.03m, json.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(2, json.GetProperty("invoiceCount").GetInt32());
        var facility = Assert.Single(json.GetProperty("byFacility").EnumerateArray());
        Assert.Equal(data.Facility.Id, facility.GetProperty("facilityId").GetInt64());
    }

    [Theory]
    [InlineData("{}", "MISSING_TOOL_ARGUMENT")]
    [InlineData("{\"period\":\"\"}", "INVALID_TOOL_ARGUMENTS")]
    [InlineData("{\"period\":\"2026-10-03\"}", "INVALID_TOOL_ARGUMENTS")]
    [InlineData("{\"period\":\"year\"}", "INVALID_TOOL_ARGUMENTS")]
    [InlineData("{\"period\":\"TODAY\"}", "INVALID_TOOL_ARGUMENTS")]
    [InlineData("{\"period\":1}", "INVALID_TOOL_ARGUMENTS")]
    [InlineData("{\"period\":null}", "INVALID_TOOL_ARGUMENTS")]
    public async Task Revenue_rejects_missing_or_non_allowlisted_periods(string arguments, string error)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var result = await Executor(scope, db, AiActorRole.Admin, AdminId, data.Facility.Id)
            .ExecuteAsync(Invocation("admin.get_revenue_summary", arguments));
        Assert.Equal(error, result.Error?.Code);
    }

    [Theory]
    [InlineData("reception.get_pending_payments", AiActorRole.Receptionist, 50)]
    [InlineData("doctor.get_my_appointments_today", AiActorRole.Doctor, 100)]
    [InlineData("technician.get_completed_today", AiActorRole.DiagnosticTechnician, 100)]
    [InlineData("pharmacist.get_low_stock", AiActorRole.Pharmacist, 100)]
    [InlineData("patient.get_my_invoices", AiActorRole.Patient, 20)]
    public async Task Read_lists_enforce_server_limits(string name, AiActorRole role, int limit)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var visit = Visit(data.Facility.Id, data.Department.Id);
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        for (var i = 0; i < limit + 3; i++)
        {
            switch (role)
            {
                case AiActorRole.Receptionist:
                case AiActorRole.Patient:
                    var invoice = Invoice(visit.Id, InvoiceStatus.Unpaid, null);
                    invoice.CreatedAtUtc = new DateTime(2101, 1, 1, 0, 0, i, DateTimeKind.Utc);
                    db.Invoices.Add(invoice);
                    break;
                case AiActorRole.Doctor: db.Appointments.Add(Appointment(data.Facility.Id)); break;
                case AiActorRole.DiagnosticTechnician: db.DiagnosticOrders.Add(Order(data.Facility.Id, data.Department.Id)); break;
                case AiActorRole.Pharmacist:
                    var medicine = Medicine();
                    medicine.StockQuantity = 5;
                    medicine.ReorderLevel = 5;
                    db.Medicines.Add(medicine);
                    break;
            }
        }
        await db.SaveChangesAsync();
        var result = await Executor(scope, db, role, Actor(role), data.Facility.Id).ExecuteAsync(Invocation(name));
        Assert.True(result.Status == "completed", JsonSerializer.Serialize(result.Error));
        var rows = JsonSerializer.SerializeToElement(result.Data, JsonOptions).EnumerateArray().ToArray();
        Assert.Equal(limit, rows.Length);
        if (role == AiActorRole.Patient)
        {
            Assert.All(rows, row => Assert.Equal(2101, row.GetProperty("createdAtUtc").GetDateTime().Year));
            Assert.True(rows[0].GetProperty("createdAtUtc").GetDateTime() >= rows[^1].GetProperty("createdAtUtc").GetDateTime());
        }
    }

    [Fact]
    public async Task Revenue_aggregates_across_bounded_pages_and_only_authorized_facilities_including_appointment_invoices()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var actor = Guid.NewGuid();
        db.StaffFacilityAssignments.AddRange(
            new StaffFacilityAssignment { UserId = actor, FacilityId = data.Facility.Id, Role = "Admin" },
            new StaffFacilityAssignment { UserId = actor, FacilityId = data.ForeignFacility.Id, Role = "Admin" });
        var visit = Visit(data.Facility.Id, data.Department.Id);
        var appointment = Appointment(data.ForeignFacility.Id);
        db.PatientVisits.Add(visit);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        for (var i = 0; i < 503; i++) db.Invoices.Add(Invoice(visit.Id, InvoiceStatus.Paid, Clock.UtcNow, 0.01m));
        var paid = Invoice(null, InvoiceStatus.Paid, Clock.UtcNow, 2m);
        paid.AppointmentId = appointment.Id;
        paid.Appointment = appointment;
        db.Invoices.Add(paid);
        await db.SaveChangesAsync();
        var result = await Executor(scope, db, AiActorRole.Admin, actor, null).ExecuteAsync(Invocation("admin.get_revenue_summary"));
        Assert.Equal("completed", result.Status);
        var json = JsonSerializer.SerializeToElement(result.Data, JsonOptions);
        Assert.Equal(7.03m, json.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(504, json.GetProperty("invoiceCount").GetInt32());
        Assert.Equal(new[] { 503, 1 }, json.GetProperty("byFacility").EnumerateArray().Select(x => x.GetProperty("invoiceCount").GetInt32()));
    }

    [Fact]
    public async Task Technician_completed_reuses_worklist_department_scope_and_Vietnam_day_without_clinical_fields()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var data = await SeedScopeAsync(db);
        var extraDepartment = new Department { FacilityId = data.Facility.Id, Code = Code(), Name = "Other", DepartmentType = DepartmentType.Paraclinical };
        db.Departments.Add(extraDepartment);
        await db.SaveChangesAsync();
        var valid = Order(data.Facility.Id, data.Department.Id);
        valid.CompletedAtUtc = Clock.ConvertVietnamToUtc(Clock.VietnamToday.ToDateTime(TimeOnly.MinValue));
        valid.Items.Add(new DiagnosticOrderItem { DiagnosticServiceId = await db.DiagnosticServices.Select(x => x.Id).FirstAsync() });
        var wrongDepartment = Order(data.Facility.Id, extraDepartment.Id);
        var before = Order(data.Facility.Id, data.Department.Id);
        before.CompletedAtUtc = valid.CompletedAtUtc.Value.AddTicks(-1);
        var after = Order(data.Facility.Id, data.Department.Id);
        after.CompletedAtUtc = Clock.ConvertVietnamToUtc(Clock.VietnamToday.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var notCompleted = Order(data.Facility.Id, data.Department.Id);
        notCompleted.Status = DiagnosticOrderStatus.InProgress;
        db.DiagnosticOrders.AddRange(valid, wrongDepartment, before, after, notCompleted);
        await db.SaveChangesAsync();
        var result = await Executor(scope, db, AiActorRole.DiagnosticTechnician, TechnicianId, data.Facility.Id)
            .ExecuteAsync(Invocation("technician.get_completed_today"));
        Assert.True(result.Status == "completed", JsonSerializer.Serialize(result.Error));
        var rows = JsonSerializer.SerializeToElement(result.Data, JsonOptions);
        Assert.Equal(valid.OrderCode, Assert.Single(rows.EnumerateArray()).GetProperty("orderCode").GetString());
        Assert.Single(rows[0].GetProperty("services").EnumerateArray());
        Assert.DoesNotContain("clinical", rows.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var worklist = await Executor(scope, db, AiActorRole.DiagnosticTechnician, TechnicianId, data.Facility.Id)
            .ExecuteAsync(Invocation("technician.get_worklist"));
        Assert.DoesNotContain(wrongDepartment.OrderCode, JsonSerializer.Serialize(worklist.Data));
        Assert.Contains(notCompleted.OrderCode, JsonSerializer.Serialize(worklist.Data));
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

    private static async Task<ScopeData> SeedScopeAsync(AppDbContext db)
    {
        var facility = new Facility { Code = Code(), Name = "Allowed", Address = "Test", IsActive = true };
        var foreign = new Facility { Code = Code(), Name = "Foreign", Address = "Test", IsActive = true };
        db.Facilities.AddRange(facility, foreign);
        await db.SaveChangesAsync();
        var department = new Department { FacilityId = facility.Id, Code = Code(), Name = "Allowed", DepartmentType = DepartmentType.Paraclinical };
        var foreignDepartment = new Department { FacilityId = foreign.Id, Code = Code(), Name = "Foreign", DepartmentType = DepartmentType.Paraclinical };
        db.Departments.AddRange(department, foreignDepartment);
        await db.SaveChangesAsync();
        foreach (var role in Enum.GetValues<AiActorRole>().Where(r => r != AiActorRole.Patient))
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = Actor(role), FacilityId = facility.Id, DepartmentId = department.Id, Role = role.ToString() });
        await db.SaveChangesAsync();
        return new ScopeData(facility, foreign, department, foreignDepartment, Code());
    }

    private static async Task<string> SeedToolDataAsync(AppDbContext db, ScopeData data, string name)
    {
        var expected = Code();
        if (name.Contains("revenue") || name.Contains("payments") || name.Contains("invoices"))
        {
            var visit = Visit(data.Facility.Id, data.Department.Id);
            var foreign = Visit(data.ForeignFacility.Id, data.ForeignDepartment.Id);
            db.PatientVisits.AddRange(visit, foreign);
            await db.SaveChangesAsync();
            var invoice = Invoice(visit.Id, name.Contains("revenue") ? InvoiceStatus.Paid : InvoiceStatus.Unpaid, Clock.UtcNow, 123.45m);
            invoice.InvoiceCode = expected;
            invoice.CreatedAtUtc = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var excluded = Invoice(foreign.Id, InvoiceStatus.Paid, Clock.UtcNow, 999m);
            excluded.InvoiceCode = data.ForeignCode;
            excluded.PatientId = Patient2EntityId;
            if (name.Contains("payments")) excluded.Status = InvoiceStatus.Unpaid;
            db.Invoices.AddRange(invoice, excluded);
            if (name.Contains("payments"))
            {
                db.Invoices.Add(Invoice(visit.Id, InvoiceStatus.Paid, Clock.UtcNow));
                db.Invoices.Add(Invoice(visit.Id, InvoiceStatus.Cancelled, null));
                db.Invoices.Add(Invoice(null, InvoiceStatus.Unpaid, null));
            }
        }
        else if (name.Contains("appointments"))
        {
            var appointment = Appointment(data.Facility.Id);
            appointment.AppointmentCode = expected;
            var excluded = Appointment(data.ForeignFacility.Id);
            excluded.AppointmentCode = data.ForeignCode;
            var otherDoctor = Appointment(data.Facility.Id);
            otherDoctor.DoctorId = Doctor2EntityId;
            var cancelled = Appointment(data.Facility.Id);
            cancelled.Status = AppointmentStatus.Cancelled;
            var completed = Appointment(data.Facility.Id);
            completed.Status = AppointmentStatus.Completed;
            var yesterday = Appointment(data.Facility.Id);
            yesterday.AppointmentDate = DateOnly.FromDateTime(Clock.UtcNow);
            db.Appointments.AddRange(appointment, excluded, otherDoctor, cancelled, completed, yesterday);
        }
        else if (name.Contains("completed_today"))
        {
            var order = Order(data.Facility.Id, data.Department.Id);
            order.OrderCode = expected;
            var excluded = Order(data.ForeignFacility.Id, data.ForeignDepartment.Id);
            excluded.OrderCode = data.ForeignCode;
            db.DiagnosticOrders.AddRange(order, excluded);
        }
        else
        {
            var medicine = Medicine();
            medicine.Code = expected;
            medicine.StockQuantity = medicine.ReorderLevel;
            var inactive = Medicine();
            inactive.Code = data.ForeignCode;
            inactive.IsActive = false;
            var sufficient = Medicine();
            sufficient.StockQuantity = sufficient.ReorderLevel + 1;
            db.Medicines.AddRange(medicine, inactive, sufficient);
        }
        await db.SaveChangesAsync();
        return expected;
    }

    private static Guid Actor(AiActorRole role) => role switch
    {
        AiActorRole.Admin => AdminId, AiActorRole.Doctor => DoctorId, AiActorRole.Receptionist => ReceptionistId,
        AiActorRole.Pharmacist => PharmacistId, AiActorRole.DiagnosticTechnician => TechnicianId, _ => Patient1Id
    };
    private static string Code() => Guid.NewGuid().ToString("N");
    private static string Arguments(string name) => name == "admin.get_revenue_summary" ? "{\"period\":\"today\"}" : "{}";
    private static AiToolInvocation Invocation(string name, string? arguments = null) => new() { ToolName = name, ArgumentsJson = arguments ?? Arguments(name) };
    private static PatientVisit Visit(long facility, long department) => new()
    {
        VisitCode = Code(), PatientId = Patient1EntityId, FacilityId = facility, DepartmentId = department,
        VisitDate = Clock.VietnamToday, QueueNumber = 1, CreatedByUserId = ReceptionistId, ChiefComplaint = "private clinical note"
    };
    private static Invoice Invoice(long? visit, InvoiceStatus status, DateTime? paid, decimal total = 10m) => new()
    {
        InvoiceCode = Code(), PatientId = Patient1EntityId, PatientVisitId = visit, Status = status, PaidAtUtc = paid,
        CreatedByUserId = ReceptionistId, CreatedAtUtc = Clock.UtcNow, TotalAmount = total, Subtotal = total,
        Appointment = visit.HasValue ? null : Appointment(null)
    };
    private static Appointment Appointment(long? facility) => new()
    {
        AppointmentCode = Code(), PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId,
        AppointmentSlotId = SlotEntityId, FacilityId = facility, AppointmentDate = Clock.VietnamToday,
        StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30), Status = AppointmentStatus.Confirmed, Reason = "Checkup"
    };
    private static DiagnosticOrder Order(long facility, long department) => new()
    {
        OrderCode = Code(), PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId, FacilityId = facility,
        PerformingDepartmentId = department, Status = DiagnosticOrderStatus.Completed, CompletedAtUtc = Clock.UtcNow,
        ClinicalIndication = "private clinical note"
    };
    private static Medicine Medicine() => new() { Code = Code(), Name = "Low stock", Unit = "Tablet", IsActive = true, StockQuantity = 0, ReorderLevel = 0, UnitPrice = 1m };
    private sealed record ScopeData(Facility Facility, Facility ForeignFacility, Department Department, Department ForeignDepartment, string ForeignCode);

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
