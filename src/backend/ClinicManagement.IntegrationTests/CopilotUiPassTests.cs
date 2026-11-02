using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class CopilotUiPassTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CompletedToday_MatchesScreenStats_AcrossDepartmentsAndVietnamDayBoundaries()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var assignment = await db.StaffFacilityAssignments.FirstAsync(a => a.UserId == TechnicianId && a.Role == "DiagnosticTechnician");
        var department = new Department { Code = Code("DEP"), Name = "Khoa xét nghiệm khác", FacilityId = assignment.FacilityId, DepartmentType = DepartmentType.Paraclinical };
        var foreign = new Facility { Code = Code("FAC"), Name = "Cơ sở khác", Address = "Test" };
        db.AddRange(department, foreign);
        await db.SaveChangesAsync();
        var midnight = clock.ConvertVietnamToUtc(clock.VietnamToday.ToDateTime(TimeOnly.MinValue));
        var orders = new[]
        {
            Order(assignment.FacilityId, department.Id, midnight),
            Order(assignment.FacilityId, department.Id, midnight.AddHours(23)),
            Order(assignment.FacilityId, department.Id, midnight.AddTicks(-1)),
            Order(assignment.FacilityId, department.Id, midnight.AddDays(1)),
            Order(foreign.Id, null, midnight.AddHours(1))
        };
        db.AddRange(orders);
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("tech@test.com");
        var stats = await GetAsync(client, "/api/v1/diagnostics/orders/stats");
        var response = await ChatAsync(client, "hôm nay đã hoàn tất bao nhiêu phiếu");
        var rows = Card(response, "technician_completed_today").EnumerateArray().ToArray();
        Assert.Equal(stats.GetProperty("completedTodayCount").GetInt32(), rows.Length);
        Assert.Contains($"Có {rows.Length} chỉ định", response.GetProperty("message").GetString());
        Assert.All(orders.Take(2), order => Assert.Contains(rows, r => r.GetProperty("orderCode").GetString() == order.OrderCode));
        Assert.All(orders.Skip(2), order => Assert.DoesNotContain(rows, r => r.GetProperty("orderCode").GetString() == order.OrderCode));
    }

    [Theory]
    [InlineData("Nguyễn Đình Thành")]
    [InlineData("nguyen dinh thanh")]
    [InlineData("0983123004")]
    [InlineData("MRN-UIPASS")]
    public async Task PatientSearch_UsesMpiWithinFacility_AndMasksContact(string query)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
        var facility = await FacilityAsync(db, ReceptionistId, "Receptionist");
        var patient = new Patient { FullName = "Nguyễn Đình Thành", MedicalRecordNumber = Code("MRN-UIPASS"), PhoneNumber = "0983123004", DateOfBirth = new DateOnly(1987, 2, 3), PrimaryFacilityId = facility };
        var outside = new Patient { FullName = "Nguyễn Đình Thành", MedicalRecordNumber = Code("FOREIGN"), PhoneNumber = "0983123004" };
        db.AddRange(patient, outside);
        await db.SaveChangesAsync();
        var appointment = await AppointmentAsync(db, facility, today.AddDays(1), patient.Id);
        var response = await ChatAsync(await CreateAuthenticatedClientAsync("rec@test.com"), $"tìm bệnh nhân {query}");
        var rows = Card(response, "reception_patient_lookup").EnumerateArray().ToArray();
        Assert.InRange(rows.Length, 1, 5);
        var row = Assert.Single(rows, r => r.GetProperty("medicalRecordNumber").GetString() == patient.MedicalRecordNumber);
        Assert.Equal("Nguyễn Đình Thành", row.GetProperty("fullName").GetString());
        Assert.Equal(1987, row.GetProperty("birthYear").GetInt32());
        Assert.Equal("0983***004", row.GetProperty("phone").GetString());
        Assert.Equal(appointment.AppointmentCode, row.GetProperty("upcomingAppointment").GetProperty("appointmentCode").GetString());
        Assert.DoesNotContain(outside.MedicalRecordNumber, response.GetRawText());
        Assert.DoesNotContain("0983123004", response.GetRawText());
        Assert.False(row.TryGetProperty("nationalId", out _));
    }

    [Fact]
    public async Task PatientSearch_ReturnsAtMostFivePatients()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = await FacilityAsync(db, ReceptionistId, "Receptionist");
        db.Patients.AddRange(Enumerable.Range(1, 7).Select(i => new Patient { FullName = $"Bệnh nhân giới hạn {i}", MedicalRecordNumber = Code("LIMIT"), PrimaryFacilityId = facility }));
        await db.SaveChangesAsync();
        var response = await ChatAsync(await CreateAuthenticatedClientAsync("rec@test.com"), "tìm bệnh nhân giới hạn");
        Assert.Equal(5, Card(response, "reception_patient_lookup").GetArrayLength());
    }

    [Theory]
    [InlineData("admin@test.com")]
    [InlineData("doc@test.com")]
    [InlineData("pat1@test.com")]
    [InlineData("tech@test.com")]
    [InlineData("pharm@test.com")]
    public async Task OtherRoles_CannotSearchPatients(string email)
    {
        var client = await CreateAuthenticatedClientAsync(email);
        var response = await client.PostAsJsonAsync("/api/v1/ai/tools/execute", new { toolName = "reception.search_patients", toolVersion = "1.0", argumentsJson = "{\"query\":\"Thành\"}" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NextPatient_IsExactlyDashboardSelection_WithExaminationAction()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var facility = await FacilityAsync(db, DoctorId, "Doctor");
        var department = await db.Departments.FirstAsync(d => d.FacilityId == facility);
        var visit = new PatientVisit { VisitCode = Code("VISIT"), PatientId = Patient1EntityId, FacilityId = facility, DepartmentId = department.Id, AssignedDoctorId = DoctorEntityId,
            VisitDate = clock.VietnamToday, Status = VisitStatus.InConsultation, QueueNumber = (await db.PatientVisits.MaxAsync(v => (int?)v.QueueNumber) ?? 0) + 1,
            ChiefComplaint = "Lý do khám đã ghi nhận", CreatedByUserId = ReceptionistId, ConsultationStartedAtUtc = clock.UtcNow };
        db.Add(visit);
        await db.SaveChangesAsync();
        await AppointmentAsync(db, facility, clock.VietnamToday, Patient2EntityId);
        var client = await CreateAuthenticatedClientAsync("doc@test.com");
        var dashboard = await GetAsync(client, "/api/v1/doctor/dashboard");
        var next = dashboard.GetProperty("nextPatient");
        var response = await ChatAsync(client, "bệnh nhân tiếp theo là ai");
        var row = Assert.Single(Card(response, "doctor_next_patient").GetProperty("items").EnumerateArray());
        Assert.Equal(next.GetProperty("patientName").GetString(), row.GetProperty("patientName").GetString());
        Assert.Equal(next.GetProperty("patientVisitId").GetInt64(), row.GetProperty("patientVisitId").GetInt64());
        Assert.Equal($"/doctor/visits/{next.GetProperty("patientVisitId").GetInt64()}/examination", response.GetProperty("navigationRoute").GetString());
        Assert.Contains(row.GetProperty("visitCode").GetString()!, response.GetProperty("message").GetString());
        Assert.Contains(row.GetProperty("reason").GetString()!, response.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("công thức máu", "LAB-CBC")]
    [InlineData("xét nghiệm máu tổng quát", "LAB-CBC")]
    [InlineData("đường huyết", "LAB-GLU")]
    [InlineData("chức năng gan", "LAB-LFT")]
    [InlineData("men gan", "LAB-LFT")]
    [InlineData("chức năng thận", "LAB-RFT")]
    [InlineData("điện tim", "IMG-ECG")]
    [InlineData("x quang phổi", "IMG-CXR")]
    [InlineData("siêu âm bụng", "US-ABD")]
    public async Task ServiceAliases_OnlyReturnConfiguredGuidance(string alias, string code)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.DiagnosticServices.FirstOrDefaultAsync(s => s.Code == code);
        if (service is null) db.Add(service = new DiagnosticService { Code = code, Name = $"Dịch vụ {code}", IsActive = true });
        service.PreparationInstructions = "Hướng dẫn riêng đã cấu hình";
        service.SpecimenType = "Bệnh phẩm đã cấu hình";
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("tech@test.com");
        var response = await ChatAsync(client, $"hướng dẫn {alias}");
        var row = Assert.Single(Card(response, "technician_service_guidance").EnumerateArray());
        Assert.Equal(code, row.GetProperty("code").GetString());
        Assert.Contains(service.PreparationInstructions, response.GetProperty("message").GetString());
        Assert.Contains(service.SpecimenType, response.GetProperty("message").GetString());
        service.PreparationInstructions = null;
        service.SpecimenType = null;
        await db.SaveChangesAsync();
        var missing = await ChatAsync(client, $"cách lấy mẫu xét nghiệm {alias}");
        Assert.Contains("không tự suy diễn", missing.GetProperty("message").GetString());
        Assert.DoesNotContain("Hướng dẫn riêng đã cấu hình", missing.GetProperty("message").GetString());
    }

    [Fact]
    public async Task StaffMessages_UseVietnamese_AndCompletedIsNotReceived()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var facility = await FacilityAsync(db, DoctorId, "Doctor");
        var completed = await AppointmentAsync(db, facility, clock.VietnamToday, Patient1EntityId);
        completed.Status = AppointmentStatus.Completed;
        var department = await db.Departments.FirstAsync(d => d.FacilityId == facility);
        db.Add(new PatientVisit { VisitCode = Code("DONE"), PatientId = Patient1EntityId, AppointmentId = completed.Id, FacilityId = facility, DepartmentId = department.Id,
            AssignedDoctorId = DoctorEntityId, VisitDate = clock.VietnamToday, Status = VisitStatus.Completed, CreatedByUserId = ReceptionistId,
            QueueNumber = (await db.PatientVisits.MaxAsync(v => (int?)v.QueueNumber) ?? 0) + 1 });
        await db.SaveChangesAsync();
        var doctor = await ChatAsync(await CreateAuthenticatedClientAsync("doc@test.com"), "hôm nay tôi khám ai");
        var row = Assert.Single(Card(doctor, "doctor_queue").EnumerateArray(), r => r.GetProperty("appointmentId").ValueKind == JsonValueKind.Number && r.GetProperty("appointmentId").GetInt64() == completed.Id);
        Assert.Equal("Đã khám xong", row.GetProperty("intakeStatus").GetString());
        Assert.Contains("Đã khám xong:", doctor.GetProperty("message").GetString());
        var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var active = await ChatAsync(admin, "có bao nhiêu bác sĩ đang hoạt động");
        var revenue = await ChatAsync(admin, "doanh thu hôm nay");
        var unsupported = await ChatAsync(admin, "xóa tài khoản bác sĩ Khải");
        Assert.Equal("WRITE_NOT_SUPPORTED", unsupported.GetProperty("errorCode").GetString());
        foreach (var response in new[] { doctor, active, revenue, unsupported })
        {
            var displayed = response.GetProperty("message").GetString() + string.Join(" ", response.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("description").GetString()));
            foreach (var token in new[] { "Completed", "CheckedIn", "Confirmed", "IsActive", "Paid", "ConfigurationDisabled", "WRITE_NOT_SUPPORTED" }) Assert.DoesNotContain(token, displayed);
        }
    }

    private static DiagnosticOrder Order(long facility, long? department, DateTime completed) => new() { OrderCode = Code("DX"), FacilityId = facility, PerformingDepartmentId = department,
        PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId, Status = DiagnosticOrderStatus.Completed, CompletedAtUtc = completed };
    private async Task<Appointment> AppointmentAsync(AppDbContext db, long facility, DateOnly date, long patient)
    {
        var time = new TimeOnly(10, 0);
        while (await db.AppointmentSlots.AnyAsync(s => s.DoctorId == DoctorEntityId && s.SlotDate == date && s.StartTime == time)) time = time.AddMinutes(5);
        var slot = new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = date, StartTime = time, EndTime = time.AddMinutes(5) };
        db.Add(slot);
        await db.SaveChangesAsync();
        var appointment = new Appointment { AppointmentCode = Code("APT"), PatientId = patient, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, FacilityId = facility,
            AppointmentSlotId = slot.Id, AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.Confirmed, Reason = "Khám kiểm thử" };
        db.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }
    private static Task<long> FacilityAsync(AppDbContext db, Guid actor, string role) => db.StaffFacilityAssignments.Where(a => a.UserId == actor && a.Role == role && a.IsActive).Select(a => a.FacilityId).FirstAsync();
    private static string Code(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
    private static JsonElement Card(JsonElement response, string type) => Assert.Single(response.GetProperty("cards").EnumerateArray(), c => c.GetProperty("type").GetString() == type).GetProperty("data");
    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").Clone();
    }
    private static async Task<JsonElement> ChatAsync(HttpClient client, string message)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("data").Clone();
        Assert.Equal("NotCalled", result.GetProperty("providerStatus").GetString());
        return result;
    }
}
