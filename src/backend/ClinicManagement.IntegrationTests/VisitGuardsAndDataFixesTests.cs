using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Doctors;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class VisitGuardsAndDataFixesTests : IntegrationTestBase, IDisposable
{
    public VisitGuardsAndDataFixesTests(CustomWebApplicationFactory factory) : base(factory) { }
    private readonly List<long> _createdFacilities = [];

    public void Dispose()
    {
        // The shared base fixture seeds assignments for every active facility.
        // Retire this test's facilities before the next test seeds its own data.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var facility in db.Facilities.Where(f => _createdFacilities.Contains(f.Id)))
            facility.IsActive = false;
        db.SaveChanges();
    }

    private async Task<PatientVisit> CreateVisitAsync(long? assignedDoctorId, bool callerAccess = true,
        bool doctorActive = true, string doctorRole = "Doctor")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var facility = new Facility { Code = $"FIX-{suffix}", Name = "Cơ sở kiểm thử", IsActive = true };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        _createdFacilities.Add(facility.Id);
        var department = new Department
        {
            FacilityId = facility.Id, Code = $"FIX-{suffix}", Name = "Khoa khám bệnh",
            SpecialtyId = SpecialtyEntityId, IsActive = true
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
        {
            UserId = DoctorId, FacilityId = facility.Id, DepartmentId = department.Id,
            Role = doctorRole, IsActive = doctorActive, AssignedAtUtc = DateTime.UtcNow
        });
        if (callerAccess)
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = ReceptionistId, FacilityId = facility.Id, DepartmentId = department.Id,
                Role = "Receptionist", IsActive = true, AssignedAtUtc = DateTime.UtcNow
            });
        var visit = new PatientVisit
        {
            VisitCode = $"FIX-{suffix}", PatientId = Patient1EntityId,
            FacilityId = facility.Id, DepartmentId = department.Id, AssignedDoctorId = assignedDoctorId,
            VisitDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
            CheckedInAtUtc = DateTime.UtcNow, ChiefComplaint = "Đau đầu cần kiểm tra",
            Status = VisitStatus.WaitingDoctor
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    public static IEnumerable<object[]> VisitAccessCases()
    {
        string[] operations = ["clinical-context", "start-consultation", "get-encounter", "encounter",
            "get-vitals", "vitals", "get-prescription-draft", "prescription-draft", "complete"];
        foreach (var operation in operations)
        foreach (var scenario in new[] { "other-doctor", "assigned", "unassigned", "inactive", "wrong-role", "other-facility" })
            yield return [operation, scenario];
    }

    private static Task<HttpResponseMessage> InvokeVisitAsync(HttpClient client, long id, string operation)
    {
        var url = $"/api/v1/doctor/visits/{id}/";
        return operation switch
        {
            "clinical-context" => client.GetAsync(url + operation),
            "start-consultation" => client.PostAsJsonAsync(url + operation, new { }),
            "get-encounter" => client.GetAsync(url + "encounter"),
            "get-vitals" => client.GetAsync(url + "vitals"),
            "get-prescription-draft" => client.GetAsync(url + "prescription-draft"),
            "encounter" => client.PutAsJsonAsync(url + operation, new { diagnosis = "Đau đầu", summary = "Theo dõi" }),
            "vitals" => client.PutAsJsonAsync(url + operation, new { weight = 60, height = 170 }),
            "prescription-draft" => client.PutAsJsonAsync(url + operation, new
            {
                notes = "Theo dõi", items = new[] { new { medicineId = MedicineEntityId, quantity = 1, dosage = "1 viên", frequency = "Mỗi ngày", durationDays = 1 } }
            }),
            "complete" => client.PostAsJsonAsync(url + operation, new { diagnosis = "Đau đầu", summary = "Theo dõi", issuePrescription = false }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    [Theory]
    [MemberData(nameof(VisitAccessCases))]
    public async Task L1_EveryVisitOperation_EnforcesOwnershipAndActiveDoctorFacility(string operation, string scenario)
    {
        long? assignedDoctor = scenario switch
        {
            "other-doctor" => Doctor2EntityId,
            "assigned" or "other-facility" => DoctorEntityId,
            _ => null
        };
        var visit = await CreateVisitAsync(assignedDoctor, doctorActive: scenario != "inactive",
            doctorRole: scenario == "wrong-role" ? "Receptionist" : "Doctor");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (operation == "complete")
            {
                var tracked = await db.PatientVisits.FindAsync(visit.Id);
                tracked!.Status = VisitStatus.InConsultation;
                visit.Status = tracked.Status;
            }
            if (scenario == "other-facility")
            {
                // The caller still has active Doctor assignments at other facilities.
                var assignment = await db.StaffFacilityAssignments.SingleAsync(a => a.FacilityId == visit.FacilityId && a.UserId == DoctorId);
                db.StaffFacilityAssignments.Remove(assignment);
            }
            await db.SaveChangesAsync();
        }
        await AuthenticateAsync("doc@test.com");
        var response = await InvokeVisitAsync(Client, visit.Id, operation);
        var expected = scenario == "other-doctor" ? HttpStatusCode.NotFound :
            scenario is "assigned" or "unassigned" ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity;
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var responseBody = JsonDocument.Parse(body);
        if (scenario == "other-doctor")
            Assert.Equal("Lượt khám không tồn tại hoặc không thuộc quyền quản lý.", responseBody.RootElement.GetProperty("message").GetString());
        else if (expected != HttpStatusCode.OK)
            Assert.Contains("FACILITY_SCOPE_DENIED", body);

        using var verifyScope = Factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await verify.PatientVisits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
        if (expected != HttpStatusCode.OK)
        {
            Assert.Equal(assignedDoctor, saved.AssignedDoctorId);
            Assert.Equal(visit.Status, saved.Status);
            Assert.False(await verify.VisitSummaries.AnyAsync(s => s.PatientVisitId == visit.Id));
            Assert.False(await verify.AppointmentVitalSigns.AnyAsync(s => s.PatientVisitId == visit.Id));
            Assert.False(await verify.Prescriptions.AnyAsync(s => s.PatientVisitId == visit.Id));
        }
        else if (operation is "clinical-context" or "start-consultation")
        {
            Assert.Equal(DoctorEntityId, saved.AssignedDoctorId);
            if (operation == "start-consultation") Assert.Equal(VisitStatus.InConsultation, saved.Status);
        }
        else if (operation == "encounter")
            Assert.Equal("Đau đầu", (await verify.VisitSummaries.SingleAsync(s => s.PatientVisitId == visit.Id)).Diagnosis);
        else if (operation == "vitals")
            Assert.Equal(60m, (await verify.AppointmentVitalSigns.SingleAsync(s => s.PatientVisitId == visit.Id)).Weight);
        else if (operation == "prescription-draft")
            Assert.Equal(PrescriptionStatus.Draft, (await verify.Prescriptions.SingleAsync(s => s.PatientVisitId == visit.Id)).Status);
        else if (operation == "complete")
            Assert.NotEqual(VisitStatus.InConsultation, saved.Status);
    }

    [Theory]
    [InlineData("caller-denied", HttpStatusCode.Forbidden)]
    [InlineData("doctor-other-facility", HttpStatusCode.UnprocessableEntity)]
    [InlineData("doctor-inactive", HttpStatusCode.UnprocessableEntity)]
    [InlineData("doctor-wrong-role", HttpStatusCode.UnprocessableEntity)]
    [InlineData("valid", HttpStatusCode.OK)]
    public async Task L2_AssignDoctor_RequiresCallerAccessAndDoctorAssignment(string scenario, HttpStatusCode expected)
    {
        var visit = await CreateVisitAsync(null, callerAccess: scenario != "caller-denied",
            doctorActive: scenario != "doctor-inactive", doctorRole: scenario == "doctor-wrong-role" ? "Receptionist" : "Doctor");
        if (scenario == "doctor-other-facility")
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var assignment = await db.StaffFacilityAssignments.SingleAsync(a => a.FacilityId == visit.FacilityId && a.UserId == DoctorId);
            db.StaffFacilityAssignments.Remove(assignment);
            await db.SaveChangesAsync();
        }
        await AuthenticateAsync("rec@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/patient-visits/{visit.Id}/assign-doctor", new { doctorId = DoctorEntityId });
        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK)
            Assert.Contains(scenario == "caller-denied" ? "ACCESS_DENIED_TO_FACILITY_RESOURCE" : "FACILITY_SCOPE_DENIED",
                await response.Content.ReadAsStringAsync());
        using var verifyScope = Factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(expected == HttpStatusCode.OK ? DoctorEntityId : (long?)null,
            (await verify.PatientVisits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).AssignedDoctorId);
    }

    [Fact]
    public async Task L3_CompleteVisit_DbConcurrency_Returns409AndRollsBack()
    {
        var visit = await CreateVisitAsync(DoctorEntityId);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PatientVisits.FindAsync(visit.Id))!.Status = VisitStatus.InConsultation;
            await db.SaveChangesAsync();
        }
        var interceptor = new SaveFailureInterceptor();
        using var failureFactory = Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = failureFactory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = "doc@test.com", password = "Pass@123" });
        login.EnsureSuccessStatusCode();
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new("Bearer", loginBody.RootElement.GetProperty("data").GetProperty("accessToken").GetString());
        interceptor.ExceptionFactory = () => new DbUpdateConcurrencyException("Injected concurrency failure");
        interceptor.FailOnSaveNumber = 1;
        var response = await InvokeVisitAsync(client, visit.Id, "complete");
        Assert.True(interceptor.WasTriggered);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("STATE_CONFLICT", body);
        using var responseBody = JsonDocument.Parse(body);
        Assert.Equal("Hồ sơ khám đã được cập nhật bởi một phiên làm việc khác. Vui lòng tải lại trang.", responseBody.RootElement.GetProperty("message").GetString());
        using var verifyScope = Factory.Services.CreateScope();
        var dbVerify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(VisitStatus.InConsultation, (await dbVerify.PatientVisits.FindAsync(visit.Id))!.Status);
        Assert.False(await dbVerify.VisitSummaries.AnyAsync(s => s.PatientVisitId == visit.Id));
    }

    [Fact]
    public async Task L4_DoctorContext_IsStrictUtf8AndReturnsRestoredMessage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src/backend/ClinicManagement.Infrastructure/Doctors/DoctorContextService.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory.FullName, "src/backend/ClinicManagement.Infrastructure/Doctors/DoctorContextService.cs"));
        var source = new UTF8Encoding(false, true).GetString(bytes);
        Assert.Contains("Chưa đăng nhập.", source);
        Assert.Contains("Tài khoản người dùng đã bị vô hiệu hóa.", source);
        Assert.Contains("Người dùng không có quyền Bác sĩ.", source);
        Assert.Contains("Hồ sơ bác sĩ không tồn tại.", source);
        Assert.Contains("Hồ sơ bác sĩ đã bị vô hiệu hóa.", source);
        using var scope = Factory.Services.CreateScope();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(u => u.UserId).Returns((Guid?)null);
        var service = new DoctorContextService(scope.ServiceProvider.GetRequiredService<AppDbContext>(), currentUser.Object,
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() => service.GetCurrentActiveDoctorAsync());
        Assert.Equal("Chưa đăng nhập.", exception.Message);
    }

    [Theory]
    [InlineData("  BHYT-123  ", "  patient@example.com  ", "BHYT-123", "patient@example.com")]
    [InlineData("   ", "   ", null, null)]
    [InlineData(null, null, null, null)]
    public async Task L5_NewPatientIntake_PersistsTrimmedInsuranceAndEmail(string? bhyt, string? email, string? expectedBhyt, string? expectedEmail)
    {
        var visit = await CreateVisitAsync(null);
        await AuthenticateAsync("rec@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/patient-visits/intake", new ReceptionIntakeRequest
        {
            IdempotencyKey = Guid.NewGuid().ToString(), FacilityId = visit.FacilityId, DepartmentId = visit.DepartmentId,
            ChiefComplaint = "Khám sức khỏe", NewPatient = new NewPatientProfileDto
            {
                FullName = "Bệnh nhân mới", PhoneNumber = "0901234567", BhytNumber = bhyt, Email = email
            }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var patientId = body.RootElement.GetProperty("data").GetProperty("patientId").GetInt64();
        using var scope = Factory.Services.CreateScope();
        var patient = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients.AsNoTracking().SingleAsync(p => p.Id == patientId);
        Assert.Equal(expectedBhyt, patient.BhytNumber);
        Assert.Equal(expectedEmail, patient.Email);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task L6_AcceptRevisit_InheritsOriginalFacilityIncludingLegacyNull(bool legacyNull)
    {
        var visit = await CreateVisitAsync(DoctorEntityId);
        var date = GetFutureWorkingDate(legacyNull ? 93 : 90);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(10, 0), new TimeOnly(10, 30));
        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sourceSlot = await db.AppointmentSlots.AsNoTracking().FirstAsync(s => s.Id == SlotEntityId);
            var original = new Appointment
            {
                AppointmentCode = $"FIX-{Guid.NewGuid():N}"[..18], PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = sourceSlot.Id,
                FacilityId = legacyNull ? null : visit.FacilityId, AppointmentDate = sourceSlot.SlotDate,
                StartTime = sourceSlot.StartTime, EndTime = sourceSlot.EndTime, Reason = "Lịch khám gốc", Status = AppointmentStatus.Completed
            };
            db.Appointments.Add(original);
            await db.SaveChangesAsync();
            var request = new RevisitRequest
            {
                AppointmentId = original.Id, PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
                SuggestedDate = date, Note = "Tái khám", Status = RevisitRequestStatus.PendingPatientResponse
            };
            db.RevisitRequests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/revisit-requests/{requestId}/accept", new { targetSlotId = slot.Id, reason = "Tái khám theo đề xuất của bác sĩ" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var appointmentId = body.RootElement.GetProperty("data").GetProperty("id").GetInt64();
        using var verifyScope = Factory.Services.CreateScope();
        var appointment = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        Assert.Equal(legacyNull ? null : (long?)visit.FacilityId, appointment.FacilityId);
    }
}
