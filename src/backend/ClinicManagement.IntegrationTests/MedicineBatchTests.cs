using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class MedicineBatchTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private DateOnly Today => Factory.Services.GetRequiredService<IDateTimeProvider>().VietnamToday;
    private static string Code() => $"BATCH-{Guid.NewGuid():N}";

    [Fact]
    public async Task Reservation_uses_FEFO_and_exposes_exact_batch_allocations()
    {
        var medicine = await MedicineAsync(40);
        // Insert the later batch first to prove selection uses expiry rather than insertion order.
        var later = await BatchAsync(medicine.Id, "LATER", 20, 300);
        var near = await BatchAsync(medicine.Id, "NEAR", 10, 30);
        var expired = await BatchAsync(medicine.Id, "EXPIRED", 5, -1);
        var rx = await PrescriptionAsync(medicine.Id, 25);
        await AssertStockAsync(medicine.Id, 40, 5);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");

        var data = await DataAsync(await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null));

        await AssertStockAsync(medicine.Id, 15, 5);
        Assert.Equal(0, await RemainingAsync(near.Id));
        Assert.Equal(5, await RemainingAsync(later.Id));
        Assert.Equal(5, await RemainingAsync(expired.Id));
        var allocations = data.GetProperty("items")[0].GetProperty("batchAllocations");
        Assert.Equal(2, allocations.GetArrayLength());
        Assert.Equal("NEAR", allocations[0].GetProperty("batchNumber").GetString());
        Assert.Equal(10, allocations[0].GetProperty("quantity").GetInt32());
        Assert.Equal("LATER", allocations[1].GetProperty("batchNumber").GetString());
        Assert.Equal(15, allocations[1].GetProperty("quantity").GetInt32());
        Assert.Equal(10, data.GetProperty("items")[0].GetProperty("availableStock").GetInt32());
        var transactions = await TransactionsAsync(medicine.Id);
        Assert.Equal(new[] { 30, 15 }, transactions.Select(transaction => transaction.BalanceAfter));
        Assert.Equal(new long?[] { near.Id, later.Id }, transactions.Select(transaction => transaction.MedicineBatchId));
        await DataAsync(await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null));
        Assert.Equal(2, (await TransactionsAsync(medicine.Id)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expired_stock_is_excluded_from_reservation_and_direct_dispense(bool dispense)
    {
        var medicine = await MedicineAsync(30);
        var usable = await BatchAsync(medicine.Id, "VALID", 5, 30);
        var expired = await BatchAsync(medicine.Id, "EXPIRED", 25, -1);
        var rx = await PrescriptionAsync(medicine.Id, 6);
        if (dispense) await InvoiceAsync(rx, medicine, 6, paid: true);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var response = await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/{(dispense ? "dispense" : "confirm-purchase")}", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INSUFFICIENT_MEDICINE_STOCK", error.GetProperty("errorCode").GetString());
        Assert.Contains("hiện còn dùng được: 5", error.GetProperty("message").GetString());
        await AssertStockAsync(medicine.Id, 30, 0);
        Assert.Equal(5, await RemainingAsync(usable.Id));
        Assert.Equal(25, await RemainingAsync(expired.Id));
        Assert.Empty(await TransactionsAsync(medicine.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_unbatched_stock_can_be_reserved_and_dispensed(bool reserve)
    {
        var medicine = await MedicineAsync(30);
        var rx = await PrescriptionAsync(medicine.Id, 8);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        if (reserve)
        {
            await DataAsync(await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null));
            await AssertStockAsync(medicine.Id, 22, 22);
        }
        await InvoiceAsync(rx, medicine, 8, paid: true);
        await DataAsync(await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null));
        await AssertStockAsync(medicine.Id, 22, 22);
        var transactions = await TransactionsAsync(medicine.Id);
        Assert.All(transactions, transaction => Assert.Null(transaction.MedicineBatchId));
        Assert.Equal(reserve ? 0 : -8, transactions.Last().QuantityChange);
    }

    [Fact]
    public async Task Invoice_cancellation_returns_each_original_batch_and_unbatched_portion()
    {
        var medicine = await MedicineAsync(25);
        var first = await BatchAsync(medicine.Id, "FIRST", 5, 10);
        var second = await BatchAsync(medicine.Id, "SECOND", 10, 100);
        var rx = await PrescriptionAsync(medicine.Id, 20);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        await DataAsync(await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null));
        await AssertStockAsync(medicine.Id, 5, 5);
        var invoice = await InvoiceAsync(rx, medicine, 20, paid: false);
        using var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        await DataAsync(await receptionist.PatchAsJsonAsync($"/api/v1/reception/billing/invoices/{invoice.Id}/cancel", new { reason = "Không mua thuốc theo đơn" }));
        await AssertStockAsync(medicine.Id, 25, 10);
        Assert.Equal(5, await RemainingAsync(first.Id));
        Assert.Equal(10, await RemainingAsync(second.Id));
        var returned = (await TransactionsAsync(medicine.Id)).Where(transaction => transaction.Type == MedicineStockTransactionType.ReservationCancelled).ToList();
        Assert.Equal(3, returned.Count);
        Assert.Equal(5, returned.Single(transaction => transaction.MedicineBatchId == first.Id).QuantityChange);
        Assert.Equal(10, returned.Single(transaction => transaction.MedicineBatchId == second.Id).QuantityChange);
        Assert.Equal(5, returned.Single(transaction => transaction.MedicineBatchId == null).QuantityChange);
        var detail = await DataAsync(await pharmacist.GetAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}"));
        Assert.Equal("Issued", detail.GetProperty("status").GetString());
        Assert.Empty(detail.GetProperty("items")[0].GetProperty("batchAllocations").EnumerateArray());
    }

    [Fact]
    public async Task Direct_dispense_records_batch_allocations_without_double_deduction()
    {
        var medicine = await MedicineAsync(20);
        var batch = await BatchAsync(medicine.Id, "DISPENSE", 20, 60);
        var rx = await PrescriptionAsync(medicine.Id, 7);
        await InvoiceAsync(rx, medicine, 7, paid: true);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        await DataAsync(await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null));
        await AssertStockAsync(medicine.Id, 13, 0);
        Assert.Equal(13, await RemainingAsync(batch.Id));
        var detail = await DataAsync(await pharmacist.GetAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}"));
        Assert.Equal(7, detail.GetProperty("items")[0].GetProperty("batchAllocations")[0].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Reservation_that_has_expired_cannot_be_dispensed()
    {
        var medicine = await MedicineAsync(20);
        var batch = await BatchAsync(medicine.Id, "AGES", 20, 1);
        var rx = await PrescriptionAsync(medicine.Id, 7);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        await DataAsync(await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null));
        await InvoiceAsync(rx, medicine, 7, paid: true);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.MedicineBatches.FindAsync(batch.Id))!.ExpiryDate = Today.AddDays(-1);
            await db.SaveChangesAsync();
        }
        var response = await pharmacist.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("INSUFFICIENT_MEDICINE_STOCK", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        await AssertStockAsync(medicine.Id, 13, 0);
        Assert.DoesNotContain(await TransactionsAsync(medicine.Id), transaction => transaction.Type == MedicineStockTransactionType.Dispense);
    }

    [Fact]
    public async Task Negative_adjustment_consumes_unbatched_then_expired_then_valid_batches()
    {
        var medicine = await MedicineAsync(30);
        var expired = await BatchAsync(medicine.Id, "EXPIRED", 10, -1);
        var valid = await BatchAsync(medicine.Id, "VALID", 10, 30);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var response = await pharmacist.PostAsJsonAsync("/api/v1/pharmacy/inventory/adjust", new { medicineId = medicine.Id, type = MedicineStockTransactionType.Adjustment, quantity = -25, reason = "Kiểm kê tồn kho" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await AssertStockAsync(medicine.Id, 5, 0);
        Assert.Equal(0, await RemainingAsync(expired.Id));
        Assert.Equal(5, await RemainingAsync(valid.Id));
        var transactions = await TransactionsAsync(medicine.Id);
        Assert.Equal(new long?[] { null, expired.Id, valid.Id }, transactions.Select(transaction => transaction.MedicineBatchId));
        Assert.Equal(new[] { -10, -10, -5 }, transactions.Select(transaction => transaction.QuantityChange));
        Assert.Equal(new[] { 20, 10, 5 }, transactions.Select(transaction => transaction.BalanceAfter));
        var incoming = await pharmacist.PostAsJsonAsync("/api/v1/pharmacy/inventory/adjust", new { medicineId = medicine.Id, type = MedicineStockTransactionType.StockIn, quantity = 3 });
        Assert.True(incoming.IsSuccessStatusCode, await incoming.Content.ReadAsStringAsync());
        await AssertStockAsync(medicine.Id, 8, 3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Import_rejects_expired_or_today_batches(int days)
    {
        var medicine = await MedicineAsync(10);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var response = await pharmacist.PostAsJsonAsync("/api/v1/pharmacy/batches", new { medicineId = medicine.Id, batchNumber = "PAST", expiryDate = Today.AddDays(days), quantity = 5 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("INVALID_EXPIRY_DATE", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        await AssertStockAsync(medicine.Id, 10, 10);
    }

    [Fact]
    public async Task Import_is_atomic_and_rejects_duplicate_batches()
    {
        var medicine = await MedicineAsync(10);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var payload = new { medicineId = medicine.Id, batchNumber = "  IMPORT-1  ", expiryDate = Today.AddDays(60), quantity = 5, unitCost = 123.45m };
        var batch = await DataAsync(await pharmacist.PostAsJsonAsync("/api/v1/pharmacy/batches", payload));
        Assert.Equal("IMPORT-1", batch.GetProperty("batchNumber").GetString());
        await AssertStockAsync(medicine.Id, 15, 10);
        var transaction = Assert.Single(await TransactionsAsync(medicine.Id));
        Assert.Equal(MedicineStockTransactionType.StockIn, transaction.Type);
        Assert.Equal(batch.GetProperty("id").GetInt64(), transaction.MedicineBatchId);
        var duplicate = await pharmacist.PostAsJsonAsync("/api/v1/pharmacy/batches", payload);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
        Assert.Equal("BATCH_EXISTS", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        await AssertStockAsync(medicine.Id, 15, 10);
        Assert.Single(await TransactionsAsync(medicine.Id));
    }

    [Fact]
    public async Task Disposing_expired_batch_decreases_stock_and_writes_disposal()
    {
        var medicine = await MedicineAsync(30);
        var expired = await BatchAsync(medicine.Id, "DISPOSE", 10, -1);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var invalid = await pharmacist.PostAsJsonAsync($"/api/v1/pharmacy/batches/{expired.Id}/dispose", new { reason = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await AssertStockAsync(medicine.Id, 30, 20);
        var disposed = await DataAsync(await pharmacist.PostAsJsonAsync($"/api/v1/pharmacy/batches/{expired.Id}/dispose", new { reason = "  Thuốc đã hết hạn  " }));
        Assert.Equal("Disposed", disposed.GetProperty("status").GetString());
        await AssertStockAsync(medicine.Id, 20, 20);
        Assert.Equal(0, await RemainingAsync(expired.Id));
        var transaction = Assert.Single(await TransactionsAsync(medicine.Id));
        Assert.Equal(MedicineStockTransactionType.Disposal, transaction.Type);
        Assert.Equal(-10, transaction.QuantityChange);
        Assert.Equal(expired.Id, transaction.MedicineBatchId);
        var again = await pharmacist.PostAsJsonAsync($"/api/v1/pharmacy/batches/{expired.Id}/dispose", new { reason = "Hủy lô lần nữa" });
        Assert.False(again.IsSuccessStatusCode);
        Assert.Single(await TransactionsAsync(medicine.Id));
    }

    [Theory]
    [InlineData("pat1@test.com")]
    [InlineData("doc@test.com")]
    public async Task Batch_endpoints_forbid_patient_and_doctor(string email)
    {
        using var client = await CreateAuthenticatedClientAsync(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/pharmacy/batches")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/pharmacy/batches", new { medicineId = 1, batchNumber = "X", expiryDate = Today.AddDays(60), quantity = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/pharmacy/batches/1/dispose", new { reason = "Thuốc hết hạn" })).StatusCode);
    }

    [Fact]
    public async Task Batch_filters_use_Vietnam_today_and_inclusive_90_day_boundary()
    {
        var medicine = await MedicineAsync(30);
        await BatchAsync(medicine.Id, "TODAY", 5, 0);
        await BatchAsync(medicine.Id, "NINETY", 5, 90);
        await BatchAsync(medicine.Id, "LATER", 5, 91);
        await BatchAsync(medicine.Id, "EXPIRED", 5, -1);
        await BatchAsync(medicine.Id, "EMPTY", 0, 20);
        using var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        foreach (var (status, count) in new[] { ("all", 5), ("valid", 3), ("expiring", 2), ("expired", 1) })
        {
            var data = await DataAsync(await pharmacist.GetAsync($"/api/v1/pharmacy/batches?status={status}&search={medicine.Code}"));
            Assert.Equal(count, data.GetProperty("totalItems").GetInt32());
        }
    }

    [Fact]
    public async Task Demo_seed_is_gated_idempotent_and_preserves_stock_invariant()
    {
        using var isolated = new FixedSeedWebApplicationFactory();
        using var scope = isolated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        db.Medicines.AddRange(new Medicine { Code = Code(), Name = "Demo one", Unit = "Viên", StockQuantity = 10 }, new Medicine { Code = Code(), Name = "Demo two", Unit = "Viên", StockQuantity = 11 });
        await db.SaveChangesAsync();
        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, PharmacistId, false);
        Assert.False(await db.MedicineBatches.AnyAsync());
        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, PharmacistId, true);
        Assert.Equal(5, await db.MedicineBatches.CountAsync());
        Assert.Single(await db.MedicineBatches.Where(batch => batch.ExpiryDate < clock.VietnamToday).ToListAsync());
        var medicines = await db.Medicines.Include(medicine => medicine.StockTransactions).ToListAsync();
        Assert.Equal(26, medicines.Sum(medicine => medicine.StockQuantity));
        foreach (var medicine in medicines)
            Assert.Equal(medicine.StockQuantity, await db.MedicineBatches.Where(batch => batch.MedicineId == medicine.Id).SumAsync(batch => batch.QuantityRemaining));
        var transaction = Assert.Single(await db.MedicineStockTransactions.ToListAsync());
        Assert.Equal(MedicineStockTransactionType.StockIn, transaction.Type);
        Assert.NotNull(transaction.MedicineBatchId);
        await MedicineBatchDevelopmentSeed.SeedAsync(db, clock, PharmacistId, true);
        Assert.Equal(5, await db.MedicineBatches.CountAsync());
        Assert.Equal(26, medicines.Sum(medicine => medicine.StockQuantity));
    }

    private async Task<Medicine> MedicineAsync(int stock)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = new Medicine { Code = Code(), Name = "Thuốc kiểm tra lô", Unit = "Viên", StockQuantity = stock, UnitPrice = 10000m, IsActive = true };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        return medicine;
    }

    private async Task<MedicineBatch> BatchAsync(long medicineId, string number, int quantity, int days)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = new MedicineBatch { MedicineId = medicineId, BatchNumber = number, ExpiryDate = Today.AddDays(days), QuantityReceived = quantity,
            QuantityRemaining = quantity, ReceivedAt = DateTime.UtcNow, CreatedByUserId = PharmacistId };
        db.MedicineBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    private async Task<Prescription> PrescriptionAsync(long medicineId, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = new Appointment { AppointmentCode = Code(), PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId, AppointmentDate = Today,
            StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(14, 30), Status = AppointmentStatus.Completed };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        var rx = new Prescription { AppointmentId = appointment.Id, PatientId = Patient1EntityId, DoctorId = DoctorEntityId, Status = PrescriptionStatus.Issued,
            Items = [new PrescriptionItem { MedicineId = medicineId, Quantity = quantity, Dosage = "1 viên", Frequency = "2 lần/ngày" }] };
        db.Prescriptions.Add(rx);
        await db.SaveChangesAsync();
        return rx;
    }

    private async Task<Invoice> InvoiceAsync(Prescription rx, Medicine medicine, int quantity, bool paid)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = new Invoice { InvoiceCode = Code(), PatientId = rx.PatientId, AppointmentId = rx.AppointmentId, Status = paid ? InvoiceStatus.Paid : InvoiceStatus.Unpaid,
            CreatedByUserId = ReceptionistId, Subtotal = quantity * 10000m, TotalAmount = quantity * 10000m,
            Items = [new InvoiceItem { ItemCode = medicine.Code, Description = medicine.Name, Quantity = quantity, UnitPrice = 10000m, LineTotal = quantity * 10000m,
                ReferenceType = PrescriptionItemBillingReference.ModernReferenceType, ReferenceId = PrescriptionItemBillingReference.Encode(rx.Id, medicine.Id) }] };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private async Task AssertStockAsync(long medicineId, int stock, int unbatched)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = await db.Medicines.AsNoTracking().SingleAsync(medicine => medicine.Id == medicineId);
        var batches = await db.MedicineBatches.AsNoTracking().Where(batch => batch.MedicineId == medicineId).ToListAsync();
        Assert.Equal(stock, medicine.StockQuantity);
        Assert.All(batches, batch => Assert.True(batch.QuantityRemaining >= 0));
        var actualUnbatched = medicine.StockQuantity - batches.Sum(batch => batch.QuantityRemaining);
        Assert.True(actualUnbatched >= 0);
        Assert.Equal(unbatched, actualUnbatched);
    }

    private async Task<int> RemainingAsync(long id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MedicineBatches.Where(batch => batch.Id == id).Select(batch => batch.QuantityRemaining).SingleAsync();
    }

    private async Task<List<MedicineStockTransaction>> TransactionsAsync(long medicineId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MedicineStockTransactions.AsNoTracking()
            .Where(transaction => transaction.MedicineId == medicineId).OrderBy(transaction => transaction.Id).ToListAsync();
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
