using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiCopilotDataAccuracyTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("Hôm nay tôi khám ai?")]
    [InlineData("Danh sách bệnh nhân hôm nay")]
    public async Task Doctor_queue_matches_dashboard_including_unreceived_appointments(string question)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var appointment = await AppointmentAsync(db, clock.VietnamToday, AppointmentStatus.Confirmed);
        var department = await db.Departments.FirstAsync(d => d.FacilityId == appointment.FacilityId);
        var received = await AppointmentAsync(db, clock.VietnamToday, AppointmentStatus.Confirmed);
        db.PatientVisits.Add(new PatientVisit
        {
            VisitCode = $"AC-{Guid.NewGuid():N}", PatientId = Patient1EntityId, AppointmentId = received.Id,
            FacilityId = department.FacilityId, DepartmentId = department.Id, AssignedDoctorId = DoctorEntityId,
            VisitDate = clock.VietnamToday, QueueNumber = (await db.PatientVisits.MaxAsync(v => (int?)v.QueueNumber) ?? 900) + 1, Status = VisitStatus.WaitingDoctor, CreatedByUserId = ReceptionistId
        });
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("doc@test.com");
        var dashboard = await GetDataAsync(client, "/api/v1/doctor/dashboard");
        var reply = await ChatAsync(client, question);
        var rows = Card(reply, "doctor_queue").EnumerateArray().ToList();
        Assert.Equal(dashboard.GetProperty("totalToday").GetInt32(), rows.Count);
        Assert.Equal("Chưa tiếp nhận", rows.Single(r => r.GetProperty("appointmentId").GetInt64() == appointment.Id).GetProperty("intakeStatus").GetString());
        Assert.Equal("Đã tiếp nhận", rows.Single(r => r.GetProperty("appointmentId").GetInt64() == received.Id).GetProperty("intakeStatus").GetString());
        Assert.Contains($"{rows.Count} lượt", reply.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Pharmacy_queue_matches_default_page_and_excludes_reserved_expired_and_dispensed()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var appointment = await AppointmentAsync(db, clock.VietnamToday, AppointmentStatus.Completed);
        var prescriptions = new[] { PrescriptionStatus.Issued, PrescriptionStatus.ReservedForPurchase, PrescriptionStatus.Expired, PrescriptionStatus.Dispensed }
            .Select(status => new Prescription { PatientId = Patient1EntityId, DoctorId = DoctorEntityId, Status = status, CreatedAt = clock.UtcNow }).ToList();
        prescriptions[0].AppointmentId = appointment.Id;
        db.Prescriptions.AddRange(prescriptions);
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("pharm@test.com");
        var page = await GetDataAsync(client, "/api/v1/pharmacy/prescriptions?status=Issued&page=1&pageSize=100");
        var reply = await ChatAsync(client, "Đơn thuốc chờ xử lý");
        var rows = Card(reply, "pharmacist_prescription_queue").EnumerateArray().ToList();
        Assert.Equal(page.GetProperty("totalItems").GetInt32(), rows.Count);
        Assert.Contains(rows, r => r.GetProperty("id").GetInt64() == prescriptions[0].Id);
        Assert.DoesNotContain(rows, r => prescriptions.Skip(1).Any(p => p.Id == r.GetProperty("id").GetInt64()));
        Assert.Contains($"{rows.Count} đơn thuốc chờ cấp", reply.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Upcoming_patient_appointments_filter_before_pagination_and_keep_only_effective_dates_and_statuses()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
        var active = new[] { AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.PendingReschedule, AppointmentStatus.PendingCancellation };
        var added = new List<Appointment>();
        foreach (var status in active.Concat(new[] { AppointmentStatus.Cancelled, AppointmentStatus.Completed, AppointmentStatus.NoShow, AppointmentStatus.CheckedIn, AppointmentStatus.InConsultation }))
            added.Add(await AppointmentAsync(db, today, status));
        var past = await AppointmentAsync(db, today.AddDays(-1), AppointmentStatus.Confirmed);
        var foreign = await AppointmentAsync(db, today.AddDays(1), AppointmentStatus.Confirmed, Patient2EntityId);
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var reply = await ChatAsync(client, "Lịch hẹn sắp tới");
        var data = Card(reply, "appointments");
        var rows = data.GetProperty("items").EnumerateArray().ToList();
        Assert.All(rows, r =>
        {
            Assert.Contains(r.GetProperty("status").GetString(), active.Select(x => x.ToString()));
            Assert.True(DateOnly.Parse(r.GetProperty("appointmentDate").GetString()!) >= today);
        });
        Assert.All(added.Where(a => active.Contains(a.Status)), a => Assert.Contains(rows, r => r.GetProperty("id").GetInt64() == a.Id));
        Assert.DoesNotContain(rows, r => r.GetProperty("id").GetInt64() == past.Id || r.GetProperty("id").GetInt64() == foreign.Id);
        var paged = await client.PostAsJsonAsync("/api/v1/ai/tools/execute", new
        {
            toolName = "patient.get_my_appointments", toolVersion = "1.0",
            argumentsJson = "{\"status\":\"Upcoming\",\"page\":1,\"pageSize\":1}", sessionId = $"sess_{Guid.NewGuid():N}"
        });
        var result = (await paged.Content.ReadFromJsonAsync<AiToolExecutionResult>())!;
        var page = Assert.IsType<JsonElement>(result.Data);
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(data.GetProperty("totalItems").GetInt32(), page.GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task Latest_patient_appointment_uses_24_hour_time_without_seconds()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var date = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday.AddDays(120);
        await AppointmentAsync(db, date, AppointmentStatus.Confirmed);
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto { Message = "Lịch hẹn của tôi", Intent = AiChatIntentTypes.ViewAppointments });
        var data = (await response.Content.ReadFromJsonAsync<ClinicManagement.Application.Common.Models.ApiResponse<AiChatResponseDto>>())!.Data!;
        Assert.Contains("15:00", data.Message);
        Assert.DoesNotContain("03:00", data.Message);
        Assert.DoesNotContain("15:00:00", data.Message);
        Assert.Contains(date.ToString("dd/MM/yyyy"), data.Message);
    }

    [Fact]
    public async Task Pharmacy_preview_uses_RX_code_and_does_not_write_without_confirmation()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var appointment = await AppointmentAsync(db, clock.VietnamToday, AppointmentStatus.Completed);
        var department = await db.Departments.FirstAsync(d => d.FacilityId == appointment.FacilityId);
        var visit = new PatientVisit
        {
            VisitCode = $"RX-{Guid.NewGuid():N}", PatientId = Patient1EntityId, AppointmentId = appointment.Id,
            FacilityId = department.FacilityId, DepartmentId = department.Id, AssignedDoctorId = DoctorEntityId,
            VisitDate = clock.VietnamToday, QueueNumber = (await db.PatientVisits.MaxAsync(v => (int?)v.QueueNumber) ?? 900) + 1,
            Status = VisitStatus.InPharmacy, CreatedByUserId = ReceptionistId
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        var prescription = new Prescription
        {
            AppointmentId = appointment.Id, PatientVisitId = visit.Id, PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
            Status = PrescriptionStatus.Issued, CreatedAt = new DateTime(2026, 10, 24, 15, 0, 0, DateTimeKind.Utc),
            Items = [new PrescriptionItem { MedicineId = MedicineEntityId, Quantity = 1, Dosage = "1 viên", Frequency = "Mỗi ngày" }]
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("pharm@test.com");
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "pharmacist.prepare_reserve_prescription", toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(new { prescriptionId = prescription.Id }),
            sessionId = $"sess_{Guid.NewGuid():N}", conversationId = $"conv_{Guid.NewGuid():N}", idempotencyKey = $"idem_{Guid.NewGuid():N}"
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<AiToolExecutionResult>())!;
        Assert.True(result.Status == "pending_confirmation", JsonSerializer.Serialize(result));
        Assert.Equal($"Đơn thuốc RX-261024-{prescription.Id:0000}", result.Preview!.Resource.Identity);
        await db.Entry(prescription).ReloadAsync();
        Assert.Equal(PrescriptionStatus.Issued, prescription.Status);
    }

    [Fact]
    public async Task Facility_reply_and_contact_action_read_opening_hours_from_matching_active_location()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = await db.Facilities.OrderBy(f => f.Id).FirstAsync(f => f.IsActive);
        const string hours = "08:15–16:45 (thứ 2 – thứ 7) · Chủ nhật nghỉ";
        db.ClinicLocations.Add(new ClinicLocation { Code = facility.Code, Name = facility.Name, Address = facility.Address, City = "Test", Phone = "0280000000", OpeningHours = hours });
        db.ClinicLocations.Add(new ClinicLocation { Code = "OTHER-HOURS", Name = "Khác", Address = "Khác", City = "Test", Phone = "0280000000", OpeningHours = "09:00–10:00" });
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto { Message = "Giờ làm việc của phòng khám", Intent = AiChatIntentTypes.FacilityInquiry });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<ClinicManagement.Application.Common.Models.ApiResponse<AiChatResponseDto>>())!.Data!;
        Assert.Contains(hours, data.Message);
        Assert.Contains(hours, data.Actions.Single(a => a.Type == AiActionTypes.ContactReception).Payload!.Reason);
        Assert.DoesNotContain("07:30", data.Message);
        Assert.DoesNotContain("09:00–10:00", data.Message);
    }

    [Theory]
    [InlineData("15:00:00", "15:00")]
    [InlineData("03:05:49", "03:05")]
    public void Booking_display_formats_time_and_calendar_date(string input, string expected)
    {
        Assert.Equal(expected, AiDisplayText.Time(input));
        Assert.Equal("24/10/2026", AiDisplayText.Date("2026-10-24"));
    }

    private async Task<Appointment> AppointmentAsync(AppDbContext db, DateOnly date, AppointmentStatus status, long? patientId = null)
    {
        var facilityId = await db.StaffFacilityAssignments.Where(a => a.UserId == DoctorId && a.IsActive).Select(a => a.FacilityId).FirstAsync();
        var slot = await db.AppointmentSlots.FirstOrDefaultAsync(s => s.DoctorId == DoctorEntityId && s.SlotDate == date && s.StartTime == new TimeOnly(15, 0));
        slot ??= new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = date, StartTime = new TimeOnly(15, 0), EndTime = new TimeOnly(15, 30) };
        if (slot.Id == 0) db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentCode = $"AC-{Guid.NewGuid():N}", PatientId = patientId ?? Patient1EntityId, DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId, FacilityId = facilityId, AppointmentSlotId = slot.Id,
            AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime, Status = status
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    private static async Task<JsonElement> ChatAsync(HttpClient client, string message)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message, sessionId = $"sess_{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
    }

    private static JsonElement Card(JsonElement reply, string type) => reply.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("type").GetString() == type).GetProperty("data");

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
    }
}
