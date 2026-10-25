using ClinicManagement.Application.Common.Interfaces;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class RealisticDevelopmentSeedTests
{
    [Fact]
    public async Task StaffDemo_IsRealistic_AndIdempotent()
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        var appointments = await db.Appointments.Include(a => a.Specialty).Include(a => a.Histories).ToListAsync();
        Assert.Contains(appointments, a => a.Specialty.SpecialtyCode == "SP03" && (a.Reason!.Contains("kinh nguyệt") || a.Reason.Contains("Khám thai")));
        Assert.Contains(appointments, a => a.Specialty.SpecialtyCode == "SP04" && (a.Reason!.Contains("mẩn ngứa") || a.Reason.Contains("Mụn")));
        Assert.True(appointments.Select(a => a.Reason).Distinct().Count() >= 10);
        Assert.All(appointments, a =>
        {
            var booking = a.Histories.Single(h => h.Action == AppointmentHistoryAction.Created);
            var bookingDate = DateOnly.FromDateTime(clock.ConvertUtcToVietnam(booking.CreatedAt));
            Assert.True(bookingDate <= a.AppointmentDate);
            Assert.StartsWith($"APT-{bookingDate:yyMMdd}-", a.AppointmentCode);
        });
        Assert.All(await db.Users.ToListAsync(), u => Assert.True(u.CreatedAt.Year > 1900));

        var invoices = await db.Invoices.Include(i => i.Items).Include(i => i.Payments).ToListAsync();
        var paid = invoices.Where(i => i.Status == InvoiceStatus.Paid).ToList();
        Assert.True(paid.Count >= 15);
        Assert.True(paid.Select(i => i.PaidAtUtc!.Value.Date).Distinct().Count() >= 15);
        Assert.All(paid, i =>
        {
            Assert.InRange(i.PaidAtUtc!.Value, clock.ConvertVietnamToUtc(DemoSeedTimeline.ActivityDate(clock).AddDays(-29).ToDateTime(TimeOnly.MinValue)), clock.UtcNow);
            Assert.Equal(i.TotalAmount, i.Items.Sum(item => item.Quantity * item.UnitPrice));
            Assert.Equal(i.TotalAmount, i.Payments.Sum(p => p.Amount));
        });
        Assert.Contains(invoices, i => i.Status == InvoiceStatus.Unpaid);
        Assert.Equal(2, invoices.Count(i => i.Status == InvoiceStatus.Cancelled));
        Assert.Contains(invoices.SelectMany(i => i.Items), i => i.ReferenceType == "DiagnosticItem");
        Assert.Contains(invoices.SelectMany(i => i.Items), i => i.ReferenceType == "PrescriptionItem:v2");
        Assert.Contains(invoices, i => i.SourceType == InvoiceSourceType.HealthPackageRegistration);

        var orders = await db.DiagnosticOrders.Include(o => o.OrderingDoctor).Include(o => o.Items).ThenInclude(i => i.Result).Where(o => o.PatientVisit != null && o.PatientVisit.QueueNumber >= 200 && o.PatientVisit.QueueNumber < 205).ToListAsync();
        Assert.Equal(2, orders.Count(o => o.Status == DiagnosticOrderStatus.Ordered));
        Assert.Single(orders, o => o.Status == DiagnosticOrderStatus.InProgress && o.VerificationStatus == DiagnosticVerificationStatus.NotRequired);
        Assert.Single(orders, o => o.VerificationStatus == DiagnosticVerificationStatus.Pending);
        Assert.Single(orders, o => o.VerificationStatus == DiagnosticVerificationStatus.Verified);
        var primaryUser = await db.Users.SingleAsync(u => u.Email == "doctor@cliniccare.local");
        var primaryDoctor = await db.Doctors.SingleAsync(d => d.UserId == primaryUser.Id);
        Assert.All(orders, o =>
        {
            Assert.Equal(DemoSeedTimeline.ActivityDate(clock), DateOnly.FromDateTime(clock.ConvertUtcToVietnam(o.OrderedAtUtc)));
            Assert.NotEqual(primaryDoctor.Id, o.OrderingDoctorId);
            if (o.VerifiedByUserId.HasValue)
            {
                Assert.NotEqual(o.OrderingDoctor.UserId, o.VerifiedByUserId);
                Assert.NotEqual(o.CompletedByUserId, o.VerifiedByUserId);
            }
        });
        var actor = (await db.Users.SingleAsync(u => u.Email == "pharmacist@cliniccare.local")).Id;
        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, actor, true);
        var batches = await db.MedicineBatches.ToListAsync();
        Assert.Single(batches, b => b.ExpiryDate < clock.VietnamToday);
        Assert.Equal(3, batches.Count(b => b.ExpiryDate > clock.VietnamToday && b.ExpiryDate < clock.VietnamToday.AddDays(60)));
        Assert.True(batches.Count(b => b.ExpiryDate >= clock.VietnamToday.AddMonths(6) && b.ExpiryDate <= clock.VietnamToday.AddMonths(24)) > batches.Count / 2);
        Assert.True(batches.Select(b => b.ExpiryDate).Distinct().Count() > 10);
        Assert.All(await db.DoctorLeaveRequests.ToListAsync(), leave =>
        {
            Assert.Equal(new TimeOnly(8, 0), TimeOnly.FromDateTime(leave.StartDateTime));
            Assert.Equal(new TimeOnly(17, 0), TimeOnly.FromDateTime(leave.EndDateTime));
            Assert.Equal(leave.StartDateTime.Date, leave.EndDateTime.Date);
        });
        var counts = (await db.Invoices.CountAsync(), await db.DiagnosticOrders.CountAsync(), await db.Prescriptions.CountAsync(), await db.Appointments.CountAsync(), batches.Count);
        var pendingOrder = Assert.Single(orders, order => order.VerificationStatus == DiagnosticVerificationStatus.Pending);
        pendingOrder.Status = DiagnosticOrderStatus.Completed;
        pendingOrder.CompletedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, actor, true);
        Assert.Equal(DiagnosticOrderStatus.InProgress, pendingOrder.Status);
        Assert.Null(pendingOrder.CompletedAtUtc);
        Assert.Equal(counts, (await db.Invoices.CountAsync(), await db.DiagnosticOrders.CountAsync(), await db.Prescriptions.CountAsync(), await db.Appointments.CountAsync(), await db.MedicineBatches.CountAsync()));
    }

    [Fact]
    public async Task InventoryTransactions_IncludePrescriptionDate_ForConsistentDisplayCode()
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 7, true);
        var prescription = await db.Prescriptions.FirstAsync();
        var medicine = await db.Medicines.FirstAsync();
        var actor = await db.Users.SingleAsync(u => u.Email == "pharmacist@cliniccare.local");
        var entry = new MedicineStockTransaction
        {
            MedicineId = medicine.Id, PrescriptionId = prescription.Id, Type = MedicineStockTransactionType.Dispense,
            QuantityChange = -1, BalanceAfter = medicine.StockQuantity, ActorUserId = actor.Id,
        };
        db.MedicineStockTransactions.Add(entry);
        await db.SaveChangesAsync();
        var page = await services.GetRequiredService<IPharmacyService>().GetStockTransactionsAsync(medicine.Id, 1, 100);
        var dto = Assert.Single(page.Items, item => item.Id == entry.Id);
        Assert.Equal(prescription.CreatedAt, dto.PrescriptionCreatedAt);
    }

    [Fact]
    public async Task SeededPendingOrder_RequiresAnIndependentReviewer_AndCanBeVerified()
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 7, true);
        var db = services.GetRequiredService<AppDbContext>();
        var pending = await db.DiagnosticOrders.SingleAsync(o => o.VerificationStatus == DiagnosticVerificationStatus.Pending);
        using var client = factory.CreateClient();
        async Task Login(string email)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Demo@12345" });
            response.EnsureSuccessStatusCode();
            using var login = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.RootElement.GetProperty("data").GetProperty("accessToken").GetString());
        }
        await Login("technician@cliniccare.local");
        var rejected = await client.PostAsJsonAsync($"/api/v1/diagnostic-verification/orders/{pending.Id}/verify", new { note = "Kiểm tra độc lập" });
        Assert.False(rejected.IsSuccessStatusCode);
        Assert.Contains("SELF_VERIFICATION_NOT_ALLOWED", await rejected.Content.ReadAsStringAsync());

        await Login("doctor@cliniccare.local");
        using var queue = JsonDocument.Parse(await client.GetStringAsync("/api/v1/diagnostic-verification/queue"));
        Assert.Contains(queue.RootElement.GetProperty("data").GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetInt64() == pending.Id);
        var verified = await client.PostAsJsonAsync($"/api/v1/diagnostic-verification/orders/{pending.Id}/verify", new { note = "Đã kiểm tra kết quả độc lập" });
        Assert.True(verified.IsSuccessStatusCode, await verified.Content.ReadAsStringAsync());
        db.ChangeTracker.Clear();
        var order = await db.DiagnosticOrders.SingleAsync(o => o.Id == pending.Id);
        Assert.Equal(DiagnosticVerificationStatus.Verified, order.VerificationStatus);
        Assert.Equal(DiagnosticOrderStatus.Completed, order.Status);

        await Login("patient@cliniccare.local");
        using var prescriptions = JsonDocument.Parse(await client.GetStringAsync("/api/v1/patients/me/prescriptions"));
        var items = prescriptions.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal($"RX-{item.GetProperty("createdAt").GetDateTime():yyMMdd}-{item.GetProperty("id").GetInt64():D4}", item.GetProperty("code").GetString()));
    }
}
