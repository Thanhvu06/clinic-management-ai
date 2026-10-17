using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Pharmacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class UsableStockTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private DateOnly Today => Factory.Services.GetRequiredService<IDateTimeProvider>().VietnamToday;

    [Fact]
    public async Task Doctor_draft_and_pharmacy_detail_exclude_expired_stock()
    {
        var medicine = await CreateMedicineAsync(50, 40);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = new Appointment { AppointmentCode = $"USABLE-{Guid.NewGuid():N}", DoctorId = DoctorEntityId,
            PatientId = Patient1EntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId,
            AppointmentDate = Today, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(14, 30), Status = AppointmentStatus.Completed };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        var prescription = new Prescription { AppointmentId = appointment.Id, DoctorId = DoctorEntityId, PatientId = Patient1EntityId,
            Status = PrescriptionStatus.Draft, Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 2, Dosage = "1 viên", Frequency = "2 lần/ngày" }] };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        using var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var draft = await DataAsync(await doctor.GetAsync($"/api/v1/doctor/appointments/{appointment.Id}/prescription"));
        Assert.Equal(10, draft.GetProperty("items")[0].GetProperty("availableStock").GetInt32());
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var detail = await DataAsync(await pharmacist.GetAsync($"/api/v1/pharmacy/prescriptions/{prescription.Id}"));
        Assert.Equal(10, detail.GetProperty("items")[0].GetProperty("availableStock").GetInt32());
        Assert.Equal(50, (await db.Medicines.AsNoTracking().SingleAsync(m => m.Id == medicine.Id)).StockQuantity);
    }

    [Theory]
    [InlineData(20, 20, "out_of_stock")]
    [InlineData(50, 45, "low")]
    [InlineData(50, 10, "in_stock")]
    public async Task Public_list_and_detail_use_non_expired_availability(int total, int expired, string expected)
    {
        var medicine = await CreateMedicineAsync(total, expired);
        var detail = await DataAsync(await Client.GetAsync($"/api/v1/public/medicines/{medicine.Id}"));
        Assert.Equal(expected, detail.GetProperty("availability").GetString());
        Assert.False(detail.TryGetProperty("stockQuantity", out _));
        var list = await DataAsync(await Client.GetAsync($"/api/v1/public/medicines?search={medicine.Code}"));
        Assert.Equal(expected, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("availability").GetString());
    }

    [Fact]
    public async Task Active_medicines_keep_total_stock_and_add_usable_stock()
    {
        var medicine = await CreateMedicineAsync(50, 40);
        using var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var active = await DataAsync(await doctor.GetAsync("/api/v1/medicines/active"));
        var item = Assert.Single(active.EnumerateArray(), item => item.GetProperty("id").GetInt64() == medicine.Id);
        Assert.Equal(50, item.GetProperty("stockQuantity").GetInt32());
        Assert.Equal(10, item.GetProperty("usableStock").GetInt32());
    }

    [Fact]
    public async Task Today_batches_and_disposed_batches_are_not_subtracted_and_legacy_stock_is_preserved()
    {
        var medicine = await CreateMedicineAsync(50, 40);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batches = await db.MedicineBatches.Where(batch => batch.MedicineId == medicine.Id).ToListAsync();
        batches.Single(batch => batch.BatchNumber == "VALID").ExpiryDate = Today;
        var disposed = batches.Single(batch => batch.BatchNumber == "EXPIRED");
        disposed.DisposedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var legacy = new Medicine { Code = $"LEGACY-{Guid.NewGuid():N}", Name = "Legacy usable stock", Unit = "Viên", StockQuantity = 17, IsActive = true };
        db.Medicines.Add(legacy);
        await db.SaveChangesAsync();
        using var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var active = await DataAsync(await doctor.GetAsync("/api/v1/medicines/active"));
        Assert.Equal(50, Assert.Single(active.EnumerateArray(), item => item.GetProperty("id").GetInt64() == medicine.Id).GetProperty("usableStock").GetInt32());
        Assert.Equal(17, Assert.Single(active.EnumerateArray(), item => item.GetProperty("id").GetInt64() == legacy.Id).GetProperty("usableStock").GetInt32());
    }

    [Fact]
    public async Task Pharmacy_and_admin_low_stock_counts_use_usable_stock_and_keep_active_filter_semantics()
    {
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var pharmacyBefore = (await DataAsync(await pharmacist.GetAsync("/api/v1/pharmacy/dashboard"))).GetProperty("lowStockCount").GetInt32();
        var adminBefore = (await DataAsync(await admin.GetAsync("/api/v1/admin/stats"))).GetProperty("lowStockMedicinesCount").GetInt32();
        await CreateMedicineAsync(50, 40);
        await CreateMedicineAsync(50, 50);
        await CreateMedicineAsync(50, 10);
        await CreateMedicineAsync(50, 45, active: false);
        var pharmacyAfter = (await DataAsync(await pharmacist.GetAsync("/api/v1/pharmacy/dashboard"))).GetProperty("lowStockCount").GetInt32();
        var adminAfter = (await DataAsync(await admin.GetAsync("/api/v1/admin/stats"))).GetProperty("lowStockMedicinesCount").GetInt32();
        Assert.Equal(pharmacyBefore + 2, pharmacyAfter);
        Assert.Equal(adminBefore + 3, adminAfter);
    }

    [Fact]
    public void Shared_stock_projection_and_filter_translate_to_SQL_Server()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=TranslationOnly;Integrated Security=true;TrustServerCertificate=true").Options);
        var sql = db.Medicines.WithUsableStock(db, Today)
            .Where(stock => stock.Quantity <= stock.Medicine.ReorderLevel)
            .Select(stock => new { stock.Medicine.Id, stock.Quantity }).ToQueryString();
        Assert.Contains("SUM(", sql);
        Assert.Contains("[DisposedAt] IS NULL", sql);
        Assert.Contains("[ExpiryDate] <", sql);
    }

    private async Task<Medicine> CreateMedicineAsync(int total, int expired, bool active = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = new Medicine { Code = $"USABLE-{Guid.NewGuid():N}", Name = "Thuốc tồn dùng được", Unit = "Viên", StockQuantity = total,
            ReorderLevel = 10, IsActive = active, UnitPrice = 1000 };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        db.MedicineBatches.AddRange(new MedicineBatch { MedicineId = medicine.Id, BatchNumber = "EXPIRED", ExpiryDate = Today.AddDays(-1),
            QuantityReceived = expired, QuantityRemaining = expired, ReceivedAt = DateTime.UtcNow, CreatedByUserId = PharmacistId },
            new MedicineBatch { MedicineId = medicine.Id, BatchNumber = "VALID", ExpiryDate = Today.AddDays(30),
                QuantityReceived = total - expired, QuantityRemaining = total - expired, ReceivedAt = DateTime.UtcNow, CreatedByUserId = PharmacistId });
        await db.SaveChangesAsync();
        return medicine;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
