using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class DemoRunTimeAndStockTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData(1, "Tiền mặt")]
    [InlineData(2, "Chuyển khoản")]
    public async Task Billing_audit_uses_current_UTC_and_Vietnamese_payment_method(int method, string label)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var appointment = new Appointment
        {
            AppointmentCode = $"TIME-{Guid.NewGuid():N}", PatientId = Patient1EntityId,
            DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId,
            AppointmentDate = clock.VietnamToday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30),
            Status = AppointmentStatus.Completed
        };
        db.Appointments.Add(appointment);
        (await db.Specialties.FindAsync(SpecialtyEntityId))!.ConsultationFee = 100000;
        await db.SaveChangesAsync();
        await AuthenticateAsync("rec@test.com");
        var invoice = await DataAsync(await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/appointment", new { appointmentId = appointment.Id }));
        var invoiceId = invoice.GetProperty("id").GetInt64();
        await DataAsync(await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay",
            new { amount = invoice.GetProperty("totalAmount").GetDecimal(), method, referenceCode = "TIME-TRANSFER" }));
        var invoiceAudit = await db.SystemAuditLogs.AsNoTracking().SingleAsync(log =>
            log.EntityId == invoiceId.ToString() && log.Action == "CREATE_INVOICE");
        var payment = await db.Payments.SingleAsync(payment => payment.InvoiceId == invoiceId);
        var paymentAudit = await db.SystemAuditLogs.AsNoTracking().SingleAsync(log => log.EntityId == payment.PaymentCode && log.Action == "PROCESS_PAYMENT");
        Assert.Contains($"({label})", paymentAudit.Description);
        Assert.DoesNotContain("(Cash)", paymentAudit.Description);
        Assert.DoesNotContain("(ManualBankTransfer)", paymentAudit.Description);
        await AuthenticateAsync("admin@test.com");
        var logs = (await DataAsync(await Client.GetAsync("/api/v1/admin/audit-logs?pageSize=100"))).GetProperty("items");
        foreach (var expected in new[] { invoiceAudit, paymentAudit })
        {
            var log = Assert.Single(logs.EnumerateArray(), log => log.GetProperty("id").GetInt64() == expected.Id);
            AssertUtc(log.GetProperty("createdAt"), clock.UtcNow);
        }
    }

    [Fact]
    public async Task Pharmacy_timestamps_roundtrip_as_UTC_and_catalog_separates_usable_from_total_stock()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var now = clock.UtcNow;
        var medicine = new Medicine { Code = $"TIME-{Guid.NewGuid():N}", Name = "Thuốc kiểm tra giờ", Unit = "Viên", StockQuantity = 50, CreatedAt = now, UpdatedAt = now };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        db.MedicineBatches.Add(new MedicineBatch { MedicineId = medicine.Id, BatchNumber = "EXPIRED", QuantityReceived = 40,
            QuantityRemaining = 40, ExpiryDate = clock.VietnamToday.AddDays(-1), ReceivedAt = now, CreatedByUserId = PharmacistId });
        db.MedicineStockTransactions.Add(new MedicineStockTransaction { MedicineId = medicine.Id,
            Type = MedicineStockTransactionType.StockIn, QuantityChange = 50, BalanceAfter = 50, ActorUserId = PharmacistId, CreatedAt = now });
        var prescription = new Prescription { PatientId = Patient1EntityId, DoctorId = DoctorEntityId, CreatedAt = now,
            DispensedAt = now, Status = PrescriptionStatus.Dispensed };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await AuthenticateAsync("pharm@test.com");
        var transaction = Assert.Single((await DataAsync(await Client.GetAsync($"/api/v1/pharmacy/inventory-transactions?medicineId={medicine.Id}&pageSize=100")))
            .GetProperty("items").EnumerateArray(), item => item.GetProperty("medicineId").GetInt64() == medicine.Id);
        AssertUtc(transaction.GetProperty("createdAt"), now);
        var detail = await DataAsync(await Client.GetAsync($"/api/v1/pharmacy/prescriptions/{prescription.Id}"));
        AssertUtc(detail.GetProperty("createdAt"), now);
        AssertUtc(detail.GetProperty("dispensedAt"), now);
        var stored = await db.Medicines.AsNoTracking().SingleAsync(item => item.Id == medicine.Id);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.UpdatedAt!.Value.Kind);
        var batch = await db.MedicineBatches.AsNoTracking().SingleAsync(item => item.MedicineId == medicine.Id);
        Assert.Equal(DateTimeKind.Utc, batch.ReceivedAt.Kind);
        Assert.Equal(clock.VietnamToday.AddDays(-1), batch.ExpiryDate);
        var catalog = await DataAsync(await Client.GetAsync($"/api/v1/admin/medicines?search={medicine.Code}"));
        var item = Assert.Single(catalog.GetProperty("items").EnumerateArray());
        Assert.Equal(50, item.GetProperty("stockQuantity").GetInt32());
        Assert.Equal(10, item.GetProperty("usableStock").GetInt32());
    }

    [Fact]
    public async Task Leave_local_interval_is_identical_for_doctor_and_admin()
    {
        await AuthenticateAsync("doc@test.com");
        var day = DateTime.UtcNow.AddDays(80).ToString("yyyy-MM-dd");
        var leave = await DataAsync(await Client.PostAsJsonAsync("/api/v1/doctor/leave-requests", new
        {
            startDateTime = $"{day}T06:35:00Z", endDateTime = $"{day}T07:35:00Z", reason = "Kiểm tra giờ Việt Nam"
        }));
        Assert.Equal($"{day}T13:35:00", leave.GetProperty("startDateTime").GetString());
        Assert.Equal($"{day}T14:35:00", leave.GetProperty("endDateTime").GetString());
        var id = leave.GetProperty("id").GetInt64();
        var doctor = await DataAsync(await Client.GetAsync($"/api/v1/doctor/leave-requests/{id}"));
        await AuthenticateAsync("admin@test.com");
        var admin = await DataAsync(await Client.GetAsync($"/api/v1/admin/leave-requests/{id}"));
        foreach (var data in new[] { doctor, admin })
        {
            Assert.Equal(leave.GetProperty("startDateTime").GetString(), data.GetProperty("startDateTime").GetString());
            Assert.Equal(leave.GetProperty("endDateTime").GetString(), data.GetProperty("endDateTime").GetString());
        }
    }

    [Fact]
    public async Task Seeded_leave_intervals_use_Vietnam_clock_without_UTC_suffix()
    {
        using var seedFactory = new CustomWebApplicationFactory();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(seedFactory.Services, 30, true);
        using var scope = seedFactory.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var leave = await db.DoctorLeaveRequests.AsNoTracking().SingleAsync(leave => leave.Status == DoctorLeaveRequestStatus.Pending);
        Assert.InRange(Math.Abs((leave.StartDateTime - clock.VietnamNow.AddDays(2)).TotalSeconds), 0, 60);
        Assert.InRange(Math.Abs((leave.EndDateTime - clock.VietnamNow.AddDays(3)).TotalSeconds), 0, 60);
        Assert.Equal(DateTimeKind.Unspecified, leave.StartDateTime.Kind);
        Assert.DoesNotContain("Z", JsonSerializer.Serialize(leave.StartDateTime));
    }

    private static void AssertUtc(JsonElement value, DateTime expected)
    {
        Assert.EndsWith("Z", value.GetString());
        Assert.Equal(DateTimeKind.Utc, value.GetDateTime().Kind);
        Assert.InRange(Math.Abs((value.GetDateTime() - expected).TotalSeconds), 0, 60);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
