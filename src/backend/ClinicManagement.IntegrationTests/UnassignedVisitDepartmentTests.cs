using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class UnassignedVisitDepartmentTests : IntegrationTestBase, IDisposable
{
    private readonly List<long> _createdFacilities = [];

    public UnassignedVisitDepartmentTests(CustomWebApplicationFactory factory) : base(factory) { }

    public void Dispose()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Prevent the shared base fixture from seeding assignments into retired test facilities.
        foreach (var facility in db.Facilities.Where(f => _createdFacilities.Contains(f.Id)))
            facility.IsActive = false;
        db.SaveChanges();
    }

    private async Task<PatientVisit> CreateVisitAsync(string scenario, bool completing = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var facility = new Facility { Code = $"DEP-{suffix}", Name = "Cơ sở kiểm thử", IsActive = true };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        _createdFacilities.Add(facility.Id);
        var targetDepartment = new Department
        {
            FacilityId = facility.Id, Code = $"TARGET-{suffix}", Name = "Khoa của lượt khám",
            SpecialtyId = scenario == "specialty" ? SpecialtyEntityId : null
        };
        var otherDepartment = new Department
        {
            FacilityId = facility.Id, Code = $"OTHER-{suffix}", Name = "Khoa khác", SpecialtyId = null
        };
        db.Departments.AddRange(targetDepartment, otherDepartment);
        await db.SaveChangesAsync();
        if (scenario != "other-facility")
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = DoctorId, FacilityId = facility.Id, DepartmentId = otherDepartment.Id,
                Role = "Doctor", IsActive = true, AssignedAtUtc = DateTime.UtcNow
            });
        if (scenario is "staff" or "inactive-staff" or "other-facility")
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                // Dashboard's department rule accepts any active staff role; facility access
                // still independently requires an active Doctor assignment.
                UserId = DoctorId, FacilityId = facility.Id, DepartmentId = targetDepartment.Id,
                Role = "Receptionist", IsActive = scenario != "inactive-staff", AssignedAtUtc = DateTime.UtcNow
            });
        var visit = new PatientVisit
        {
            VisitCode = $"DEP-{suffix}", PatientId = Patient1EntityId,
            FacilityId = facility.Id, DepartmentId = targetDepartment.Id,
            AssignedDoctorId = scenario == "assigned-other-department" ? DoctorEntityId :
                scenario == "assigned-other-doctor" ? Doctor2EntityId : null,
            VisitDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), CheckedInAtUtc = DateTime.UtcNow,
            ChiefComplaint = "Khám kiểm tra", Status = completing ? VisitStatus.InConsultation : VisitStatus.WaitingDoctor
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    public static IEnumerable<object[]> OperationCases()
    {
        foreach (var operation in new[] { "clinical-context", "start-consultation", "get-encounter", "save-encounter",
                     "get-vitals", "save-vitals", "get-prescription-draft", "save-prescription-draft", "complete" })
        foreach (var scenario in new[] { "specialty", "staff", "other-department", "inactive-staff", "other-facility", "assigned-other-department" })
            yield return [operation, scenario];
    }

    private static Task<HttpResponseMessage> InvokeAsync(HttpClient client, long id, string operation)
    {
        var url = $"/api/v1/doctor/visits/{id}/";
        return operation switch
        {
            "clinical-context" => client.GetAsync(url + operation),
            "start-consultation" => client.PostAsJsonAsync(url + operation, new { }),
            "get-encounter" => client.GetAsync(url + "encounter"),
            "get-vitals" => client.GetAsync(url + "vitals"),
            "get-prescription-draft" => client.GetAsync(url + "prescription-draft"),
            "save-encounter" => client.PutAsJsonAsync(url + "encounter", new { diagnosis = "Đau đầu", summary = "Theo dõi" }),
            "save-vitals" => client.PutAsJsonAsync(url + "vitals", new { weight = 60, height = 170 }),
            "save-prescription-draft" => client.PutAsJsonAsync(url + "prescription-draft", new
            {
                notes = "Theo dõi", items = new[] { new { medicineId = MedicineEntityId, quantity = 1, dosage = "1 viên", frequency = "Mỗi ngày", durationDays = 1 } }
            }),
            "complete" => client.PostAsJsonAsync(url + operation, new { diagnosis = "Đau đầu", summary = "Theo dõi", issuePrescription = false }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    [Theory]
    [MemberData(nameof(OperationCases))]
    public async Task EveryVisitOperation_UsesDashboardDepartmentRuleOnlyWhenUnassigned(string operation, string scenario)
    {
        var visit = await CreateVisitAsync(scenario, completing: operation == "complete");
        await AuthenticateAsync("doc@test.com");
        var response = await InvokeAsync(Client, visit.Id, operation);
        var expected = scenario switch
        {
            "other-department" or "inactive-staff" => HttpStatusCode.NotFound,
            "other-facility" => HttpStatusCode.UnprocessableEntity,
            _ => HttpStatusCode.OK
        };
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.NotFound)
            Assert.Equal("Lượt khám không tồn tại hoặc không thuộc quyền quản lý.", body.RootElement.GetProperty("message").GetString());
        if (expected == HttpStatusCode.UnprocessableEntity)
            Assert.Equal("FACILITY_SCOPE_DENIED", body.RootElement.GetProperty("errorCode").GetString());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.PatientVisits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
        if (expected != HttpStatusCode.OK)
        {
            Assert.Null(saved.AssignedDoctorId);
            Assert.Equal(visit.Status, saved.Status);
            Assert.False(await db.VisitSummaries.AnyAsync(s => s.PatientVisitId == visit.Id));
            Assert.False(await db.AppointmentVitalSigns.AnyAsync(s => s.PatientVisitId == visit.Id));
            Assert.False(await db.Prescriptions.AnyAsync(s => s.PatientVisitId == visit.Id));
        }
        else if (operation is "clinical-context" or "start-consultation")
            Assert.Equal(DoctorEntityId, saved.AssignedDoctorId);
        else if (operation == "save-encounter")
            Assert.Equal("Đau đầu", (await db.VisitSummaries.SingleAsync(s => s.PatientVisitId == visit.Id)).Diagnosis);
        else if (operation == "save-vitals")
            Assert.Equal(60m, (await db.AppointmentVitalSigns.SingleAsync(s => s.PatientVisitId == visit.Id)).Weight);
        else if (operation == "save-prescription-draft")
            Assert.Equal(PrescriptionStatus.Draft, (await db.Prescriptions.SingleAsync(s => s.PatientVisitId == visit.Id)).Status);
        else if (operation == "complete")
            Assert.NotEqual(VisitStatus.InConsultation, saved.Status);
    }

    [Fact]
    public async Task Dashboard_PreservesSpecialtyOrActiveStaffUnionAndAssignedDoctorVisibility()
    {
        var included = new List<long>();
        var excluded = new List<long>();
        foreach (var scenario in new[] { "specialty", "staff", "other-department", "inactive-staff", "other-facility", "assigned-other-department", "assigned-other-doctor" })
        {
            var visit = await CreateVisitAsync(scenario);
            if (scenario is "other-department" or "inactive-staff" or "assigned-other-doctor") excluded.Add(visit.Id);
            else included.Add(visit.Id);
        }
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync("/api/v1/doctor/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var visibleIds = body.RootElement.GetProperty("data").GetProperty("queue").EnumerateArray()
            .Where(item => item.GetProperty("patientVisitId").ValueKind == JsonValueKind.Number)
            .Select(item => item.GetProperty("patientVisitId").GetInt64()).ToList();
        Assert.All(included, id => Assert.Contains(id, visibleIds));
        Assert.All(excluded, id => Assert.DoesNotContain(id, visibleIds));
    }

    [Fact]
    public async Task DiagnosticOrderCreation_StillRejectsUnassignedVisitInMatchingDepartment()
    {
        var visit = await CreateVisitAsync("specialty");
        await AuthenticateAsync("doc@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/doctor/visits/{visit.Id}/diagnostic-orders",
            new { clinicalIndication = "Kiểm tra sức khỏe", serviceIds = new[] { 1L } });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Lượt khám không tồn tại hoặc không thuộc quyền quản lý.", body.RootElement.GetProperty("message").GetString());
    }
}
