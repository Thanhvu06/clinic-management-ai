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
public class CopilotAccuracyDataTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task TechnicianPendingOrders_MatchWorklistAcrossDepartments_AndExcludeForeignFacility()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var assignment = await db.StaffFacilityAssignments.FirstAsync(a => a.UserId == TechnicianId && a.Role == "DiagnosticTechnician");
        var otherDepartment = new Department { Code = Code("DEP"), Name = "Khoa thực hiện khác", FacilityId = assignment.FacilityId, DepartmentType = DepartmentType.Paraclinical };
        var foreign = new Facility { Code = Code("FAC"), Name = "Cơ sở ngoài phạm vi", Address = "Test" };
        db.AddRange(otherDepartment, foreign);
        await db.SaveChangesAsync();
        var first = new DiagnosticOrder { OrderCode = Code("DX"), FacilityId = assignment.FacilityId, PerformingDepartmentId = otherDepartment.Id, PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId, OrderedAtUtc = clock.UtcNow, Status = DiagnosticOrderStatus.Ordered };
        var second = new DiagnosticOrder { OrderCode = Code("DX"), FacilityId = assignment.FacilityId, PerformingDepartmentId = otherDepartment.Id, PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId, OrderedAtUtc = clock.UtcNow, Status = DiagnosticOrderStatus.InProgress };
        var excluded = new DiagnosticOrder { OrderCode = Code("DX"), FacilityId = foreign.Id, PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId, Status = DiagnosticOrderStatus.Ordered };
        db.DiagnosticOrders.AddRange(first, second, excluded);
        await db.SaveChangesAsync();

        var client = await CreateAuthenticatedClientAsync("tech@test.com");
        var ordered = await GetDataAsync(client, "/api/v1/diagnostics/orders?status=Ordered&pageSize=100");
        var progress = await GetDataAsync(client, "/api/v1/diagnostics/orders?status=InProgress&pageSize=100");
        var response = await ChatAsync(client, "phiếu nào đang chờ thực hiện", "technician.get_worklist");
        var rows = Card(response, "technician_worklist").EnumerateArray().ToArray();
        Assert.Equal(ordered.GetProperty("totalItems").GetInt32() + progress.GetProperty("totalItems").GetInt32(), rows.Length);
        Assert.Contains(rows, r => r.GetProperty("orderCode").GetString() == first.OrderCode);
        Assert.Contains(rows, r => r.GetProperty("orderCode").GetString() == second.OrderCode);
        Assert.DoesNotContain(rows, r => r.GetProperty("orderCode").GetString() == excluded.OrderCode);
        Assert.Contains($"Có {rows.Length} chỉ định", response.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TechnicianSampleQuestion_UsesStoredGuidance_InsteadOfWorklist()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DiagnosticServices.Add(new DiagnosticService { Code = Code("GLU"), Name = "Xét nghiệm glucose kiểm thử", IsActive = true, PreparationInstructions = "Chuẩn bị theo chỉ dẫn đã xác nhận của cơ sở", SpecimenType = "Máu tĩnh mạch" });
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("tech@test.com");
        var response = await ChatAsync(client, "cách lấy mẫu xét nghiệm glucose", "technician.get_service_guidance");
        Assert.Contains("Chuẩn bị theo chỉ dẫn đã xác nhận của cơ sở", response.GetProperty("message").GetString());
        Assert.Contains("Máu tĩnh mạch", response.GetProperty("message").GetString());
        Assert.DoesNotContain(response.GetProperty("cards").EnumerateArray(), c => c.GetProperty("type").GetString() == "technician_worklist");
        var missing = await ChatAsync(client, "cách lấy mẫu xét nghiệm dịch vụ không tồn tại xyz", "technician.get_service_guidance");
        Assert.Contains("không tự suy diễn", missing.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ReceptionPendingChanges_MatchChangeRequestService_AndExcludeForeignFacility()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var facility = await FacilityAsync(db, ReceptionistId, "Receptionist");
        var own = await AppointmentAsync(db, facility, clock.VietnamToday);
        var foreign = new Facility { Code = Code("FAC"), Name = "Ngoài phạm vi", Address = "Test" };
        db.Facilities.Add(foreign);
        await db.SaveChangesAsync();
        var other = await AppointmentAsync(db, foreign.Id, clock.VietnamToday);
        db.AppointmentChangeRequests.AddRange(new AppointmentChangeRequest { AppointmentId = own.Id, RequestType = AppointmentChangeRequestType.Reschedule, Status = AppointmentChangeRequestStatus.Pending, RequestedByUserId = Patient1Id, CreatedAt = clock.UtcNow },
            new AppointmentChangeRequest { AppointmentId = other.Id, RequestType = AppointmentChangeRequestType.Cancellation, Status = AppointmentChangeRequestStatus.Pending, RequestedByUserId = Patient1Id, CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var expected = await GetDataAsync(client, "/api/v1/reception/change-requests?status=Pending&pageSize=50");
        var response = await ChatAsync(client, "có yêu cầu đổi lịch nào đang chờ", "reception.get_pending_change_requests");
        var data = Card(response, "reception_pending_change_requests");
        Assert.Equal(expected.GetProperty("totalItems").GetInt32(), data.GetProperty("totalItems").GetInt32());
        Assert.Contains(data.GetProperty("items").EnumerateArray(), r => r.GetProperty("appointmentCode").GetString() == own.AppointmentCode);
        Assert.DoesNotContain(other.AppointmentCode, data.GetRawText());
    }

    [Fact]
    public async Task ReceptionUnpaidInvoices_MatchBillingService_IncludingAppointmentWithoutVisit()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var appointment = await AppointmentAsync(db, await FacilityAsync(db, ReceptionistId, "Receptionist"), clock.VietnamToday);
        var invoice = Invoice(appointment.Id, clock.UtcNow, InvoiceStatus.Unpaid, 12345m);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var expected = await GetDataAsync(client, "/api/v1/reception/billing/invoices?status=Unpaid&pageSize=50");
        var response = await ChatAsync(client, "hóa đơn nào chưa thanh toán", "reception.get_unpaid_invoices");
        var data = Card(response, "reception_unpaid_invoices");
        Assert.Equal(expected.GetProperty("totalItems").GetInt32(), data.GetProperty("totalItems").GetInt32());
        var row = Assert.Single(data.GetProperty("items").EnumerateArray(), r => r.GetProperty("invoiceCode").GetString() == invoice.InvoiceCode);
        Assert.Equal(12345m, row.GetProperty("totalAmount").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("patientName").GetString()));
        Assert.Contains(invoice.InvoiceCode, response.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PharmacyStockAndLowStock_MatchUsableStock_WithoutExpiredBatches()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var medicine = new Medicine { Code = Code("MED"), Name = "Paracetamol kiểm thử tồn khả dụng", Unit = "viên", StockQuantity = 20, ReorderLevel = 15, IsActive = true };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        db.MedicineBatches.Add(new MedicineBatch { MedicineId = medicine.Id, BatchNumber = Code("EXP"), ExpiryDate = clock.VietnamToday.AddDays(-1), QuantityReceived = 10, QuantityRemaining = 10, ReceivedAt = clock.UtcNow, CreatedByUserId = PharmacistId });
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("pharm@test.com");
        var expected = await GetDataAsync(client, $"/api/v1/admin/medicines/{medicine.Id}");
        var response = await ChatAsync(client, "paracetamol còn bao nhiêu viên", "pharmacist.get_medicine_stock");
        var row = Assert.Single(Card(response, "pharmacy_medicine_stock").EnumerateArray(), r => r.GetProperty("code").GetString() == medicine.Code);
        Assert.Equal(expected.GetProperty("usableStock").GetInt32(), row.GetProperty("availableStock").GetInt32());
        Assert.Equal(10, row.GetProperty("availableStock").GetInt32());
        var low = await ChatAsync(client, "thuốc nào sắp hết hàng", "pharmacist.get_low_stock");
        var lowRows = Card(low, "pharmacy_low_stock").EnumerateArray().ToArray();
        Assert.Contains(lowRows, r => r.GetProperty("code").GetString() == medicine.Code);
        Assert.All(lowRows, r => Assert.True(r.GetProperty("stockQuantity").GetInt32() <= r.GetProperty("reorderLevel").GetInt32()));
    }

    [Theory]
    [InlineData("lô thuốc nào sắp hết hạn", "expiring", 30)]
    [InlineData("lô thuốc nào hết hạn", "expired", -1)]
    public async Task PharmacyBatches_MatchBatchService(string question, string status, int expiryDays)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var batch = new MedicineBatch { MedicineId = MedicineEntityId, BatchNumber = Code("BATCH"), ExpiryDate = clock.VietnamToday.AddDays(expiryDays), QuantityReceived = 5, QuantityRemaining = 5, CreatedByUserId = PharmacistId, ReceivedAt = clock.UtcNow };
        db.MedicineBatches.Add(batch);
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("pharm@test.com");
        var expected = await GetDataAsync(client, $"/api/v1/pharmacy/batches?status={status}&pageSize=100");
        var response = await ChatAsync(client, question, "pharmacist.get_medicine_batches");
        var data = Card(response, "pharmacy_medicine_batches");
        Assert.Equal(expected.GetProperty("totalItems").GetInt32(), data.GetProperty("totalItems").GetInt32());
        Assert.Contains(data.GetProperty("items").EnumerateArray(), r => r.GetProperty("batchNumber").GetString() == batch.BatchNumber);
        Assert.Contains(batch.BatchNumber, response.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DoctorWaitingCount_MatchesOwnDashboardQueue_AndReportsStatuses()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        await AppointmentAsync(db, await FacilityAsync(db, DoctorId, "Doctor"), clock.VietnamToday);
        var client = await CreateAuthenticatedClientAsync("doc@test.com");
        var dashboard = await GetDataAsync(client, "/api/v1/doctor/dashboard");
        var response = await ChatAsync(client, "hôm nay tôi còn bao nhiêu bệnh nhân chờ khám", "doctor.get_my_queue");
        var rows = Card(response, "doctor_queue").EnumerateArray().ToArray();
        Assert.Equal(dashboard.GetProperty("queue").GetArrayLength(), rows.Length);
        foreach (var group in rows.GroupBy(r => r.GetProperty("status").GetString()))
            Assert.Contains($"{group.Key switch { "Confirmed" => "Chờ tiếp nhận", "CheckedIn" => "Đã tiếp nhận", "InConsultation" => "Đang khám", "InDiagnostics" => "Chờ CLS", "ResultsReady" => "Có kết quả CLS", "Completed" => "Đã khám xong", "NoShow" => "Vắng mặt", _ => "Không xác định" }}: {group.Count()}", response.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    public async Task AdminRevenue_MatchesPaymentBasedRevenueReport(int days)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var appointment = await AppointmentAsync(db, await db.Facilities.Select(f => f.Id).FirstAsync(), clock.VietnamToday);
        var invoice = Invoice(appointment.Id, clock.UtcNow, InvoiceStatus.Unpaid, 100m);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Payments.Add(new Payment { PaymentCode = Code("PAY"), InvoiceId = invoice.Id, Amount = 40m, Status = PaymentStatus.Succeeded, ReceivedByUserId = ReceptionistId, ReceivedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("admin@test.com");
        var expected = await GetDataAsync(client, $"/api/v1/admin/billing/revenue?fromDate={clock.VietnamToday.AddDays(1 - days):yyyy-MM-dd}&toDate={clock.VietnamToday:yyyy-MM-dd}");
        var response = await ChatAsync(client, $"doanh thu {days} ngày gần đây", "admin.get_revenue_report");
        var report = Card(response, "admin_revenue_report");
        Assert.Equal(expected.GetProperty("totalRevenue").GetDecimal(), report.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(expected.GetProperty("totalSucceededTransactions").GetInt32(), report.GetProperty("totalSucceededTransactions").GetInt32());
        Assert.True(report.GetProperty("totalRevenue").GetDecimal() >= 40m);
    }

    [Fact]
    public async Task AdminActiveDoctorsAndPendingLeave_MatchCurrentDatabaseAndLeaveService()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        db.DoctorLeaveRequests.Add(new DoctorLeaveRequest { DoctorId = DoctorEntityId, Status = DoctorLeaveRequestStatus.Pending, StartDateTime = clock.VietnamToday.AddDays(10).ToDateTime(new TimeOnly(8, 0)), EndDateTime = clock.VietnamToday.AddDays(10).ToDateTime(new TimeOnly(17, 0)), Reason = "Kiểm thử dữ liệu nghỉ phép" });
        await db.SaveChangesAsync();
        var client = await CreateAuthenticatedClientAsync("admin@test.com");
        var doctors = await ChatAsync(client, "có bao nhiêu bác sĩ đang hoạt động", "admin.get_active_doctors");
        Assert.Equal(await db.Doctors.CountAsync(d => d.IsActive), Card(doctors, "admin_active_doctors").GetProperty("totalItems").GetInt32());
        var activeDoctors = await GetDataAsync(client, "/api/v1/admin/doctors?isActive=true&pageSize=100");
        Assert.Equal(activeDoctors.GetProperty("totalItems").GetInt32(), Card(doctors, "admin_active_doctors").GetProperty("totalItems").GetInt32());
        var expected = await GetDataAsync(client, "/api/v1/admin/leave-requests?status=Pending&pageSize=50");
        var leave = await ChatAsync(client, "đơn nghỉ phép nào đang chờ duyệt", "admin.get_pending_leave_requests");
        Assert.Equal(expected.GetProperty("totalItems").GetInt32(), Card(leave, "admin_pending_leave_requests").GetProperty("totalItems").GetInt32());
    }

    private async Task<Appointment> AppointmentAsync(AppDbContext db, long facility, DateOnly date)
    {
        var time = new TimeOnly(10, 0);
        while (await db.AppointmentSlots.AnyAsync(s => s.DoctorId == DoctorEntityId && s.SlotDate == date && s.StartTime == time)) time = time.AddMinutes(5);
        var slot = new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = date, StartTime = time, EndTime = time.AddMinutes(5) };
        db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();
        var appointment = new Appointment { AppointmentCode = Code("APT"), PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, FacilityId = facility, AppointmentSlotId = slot.Id, AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.Confirmed, Reason = "Khám kiểm thử" };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    private static Task<long> FacilityAsync(AppDbContext db, Guid actor, string role) => db.StaffFacilityAssignments.Where(a => a.UserId == actor && a.Role == role && a.IsActive).Select(a => a.FacilityId).FirstAsync();
    private static Invoice Invoice(long appointment, DateTime now, InvoiceStatus status, decimal amount) => new() { InvoiceCode = Code("INV"), PatientId = Patient1EntityId, AppointmentId = appointment, SourceType = InvoiceSourceType.Appointment, Status = status, Subtotal = amount, TotalAmount = amount, CreatedByUserId = ReceptionistId, CreatedAtUtc = now };
    private static string Code(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
    private static JsonElement Card(JsonElement response, string type) => Assert.Single(response.GetProperty("cards").EnumerateArray(), c => c.GetProperty("type").GetString() == type).GetProperty("data");

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> ChatAsync(HttpClient client, string message, string tool)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("data").Clone();
        Assert.Contains(result.GetProperty("executedToolNames").EnumerateArray(), t => t.GetString() == tool);
        Assert.Equal("NotCalled", result.GetProperty("providerStatus").GetString());
        return result;
    }
}
