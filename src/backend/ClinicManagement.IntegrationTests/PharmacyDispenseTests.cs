using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using ClinicManagement.Application.Billing.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class PharmacyDispenseTests : IntegrationTestBase
{
    public PharmacyDispenseTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<Medicine> CreateTestMedicineAsync(string prefix, int initialStock, bool isActive = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var med = new Medicine
        {
            Code = $"MED-{prefix}-{Guid.NewGuid():N}"[..15].ToUpper(),
            Name = $"Thuốc Test {prefix}",
            Unit = "Viên",
            StockQuantity = initialStock,
            UnitPrice = 10000m,
            ReorderLevel = 5,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        db.Medicines.Add(med);
        await db.SaveChangesAsync();
        return med;
    }

    private async Task<Invoice> CreatePaidInvoiceForPrescriptionAsync(
        Prescription rx,
        List<(long medicineId, int quantity)> items)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var total = 0m;
        var invoiceItems = new List<InvoiceItem>();
        foreach (var (medId, qty) in items)
        {
            var med = await db.Medicines.FindAsync(medId);
            var unitPrice = med?.UnitPrice ?? 10000m;
            var lineTotal = unitPrice * qty;
            total += lineTotal;
            invoiceItems.Add(new InvoiceItem
            {
                ItemCode = med?.Code ?? "MED",
                Description = med?.Name ?? "Thuốc test",
                Quantity = qty,
                UnitPrice = unitPrice,
                LineTotal = lineTotal,
                ReferenceType = "PrescriptionItem",
                ReferenceId = rx.Id * 100000L + medId,
                IsCancelled = false
            });
        }

        var invoice = new Invoice
        {
            InvoiceCode = $"INV-RX-{Guid.NewGuid():N}"[..18].ToUpper(),
            PatientId = rx.PatientId,
            AppointmentId = rx.AppointmentId,
            Status = InvoiceStatus.Paid,
            Subtotal = total,
            TotalAmount = total,
            PaidAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            Items = invoiceItems
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private async Task<(Appointment Appointment, Prescription Prescription)> CreateTestPrescriptionAsync(
        long doctorId,
        long patientId,
        PrescriptionStatus status,
        List<(long medicineId, int quantity)> items,
        string notes = "Đơn test")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var apt = new Appointment
        {
            AppointmentCode = $"APT-RX-{Guid.NewGuid():N}"[..18],
            DoctorId = doctorId,
            PatientId = patientId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = SlotEntityId,
            Status = AppointmentStatus.Completed,
            AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(14, 30)
        };
        db.Appointments.Add(apt);
        await db.SaveChangesAsync();

        var rx = new Prescription
        {
            AppointmentId = apt.Id,
            PatientId = patientId,
            DoctorId = doctorId,
            Status = status,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };
        db.Prescriptions.Add(rx);
        await db.SaveChangesAsync();

        foreach (var (medId, qty) in items)
        {
            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = rx.Id,
                MedicineId = medId,
                Quantity = qty,
                Dosage = "1 viên",
                Frequency = "2 lần/ngày",
                DurationDays = 5,
                Instructions = "Uống sau khi ăn"
            });
        }
        await db.SaveChangesAsync();

        return (apt, rx);
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var loginResponse = await Client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Pass@123" });
        var resStr = await loginResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resStr);
        return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    // 1. Dispense thành công: đơn đổi Dispensed, stock giảm chính xác, sinh MedicineStockTransaction
    [Fact]
    public async Task Given_IssuedPrescription_When_DispensedWithSufficientStock_Then_StockDeductedStatusDispensedAndAuditLogged()
    {
        var med = await CreateTestMedicineAsync("VALID", 30);
        var items = new List<(long, int)> { (med.Id, 10) };
        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);
        await CreatePaidInvoiceForPrescriptionAsync(rx, items);

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedRx = await db.Prescriptions.FindAsync(rx.Id);
            Assert.NotNull(updatedRx);
            Assert.Equal(PrescriptionStatus.Dispensed, updatedRx.Status);
            Assert.NotNull(updatedRx.DispensedAt);
            Assert.Equal(PharmacistId, updatedRx.DispensedByUserId);

            var updatedMed = await db.Medicines.FindAsync(med.Id);
            Assert.NotNull(updatedMed);
            Assert.Equal(20, updatedMed.StockQuantity);

            var tx = await db.MedicineStockTransactions
                .FirstOrDefaultAsync(t => t.PrescriptionId == rx.Id && t.MedicineId == med.Id);
            Assert.NotNull(tx);
            Assert.Equal(MedicineStockTransactionType.Dispense, tx.Type);
            Assert.Equal(-10, tx.QuantityChange);
            Assert.Equal(20, tx.BalanceAfter);
            Assert.Equal(PharmacistId, tx.ActorUserId);
        }
    }

    // 2. Đơn gồm nhiều thuốc: toàn bộ thuốc giảm đúng số lượng
    [Fact]
    public async Task Given_PrescriptionWithMultipleMedicines_When_Dispensed_Then_AllMedicinesDeducted()
    {
        var medA = await CreateTestMedicineAsync("MULTIA", 50);
        var medB = await CreateTestMedicineAsync("MULTIB", 30);
        var items = new List<(long, int)> { (medA.Id, 15), (medB.Id, 10) };

        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);
        await CreatePaidInvoiceForPrescriptionAsync(rx, items);

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedA = await db.Medicines.FindAsync(medA.Id);
            var updatedB = await db.Medicines.FindAsync(medB.Id);
            Assert.NotNull(updatedA);
            Assert.NotNull(updatedB);
            Assert.Equal(35, updatedA.StockQuantity);
            Assert.Equal(20, updatedB.StockQuantity);

            var txCount = await db.MedicineStockTransactions.CountAsync(t => t.PrescriptionId == rx.Id);
            Assert.Equal(2, txCount);
        }
    }

    // 3. Đơn gồm nhiều thuốc: nếu 1 thuốc thiếu tồn kho thì rollback toàn bộ, không thuốc nào bị trừ tồn kho
    [Fact]
    public async Task Given_PrescriptionWithMultipleMedicines_When_OneMedicineLacksStock_Then_RollbackAndNoStockDeducted()
    {
        var medA = await CreateTestMedicineAsync("ROLA", 50);
        var medB = await CreateTestMedicineAsync("ROLB", 3);
        var items = new List<(long, int)> { (medA.Id, 10), (medB.Id, 10) }; // medB only has 3

        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);
        await CreatePaidInvoiceForPrescriptionAsync(rx, items);

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INSUFFICIENT_MEDICINE_STOCK", body, StringComparison.OrdinalIgnoreCase);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedA = await db.Medicines.FindAsync(medA.Id);
            var updatedB = await db.Medicines.FindAsync(medB.Id);
            Assert.NotNull(updatedA);
            Assert.NotNull(updatedB);
            Assert.Equal(50, updatedA.StockQuantity); // Untouched!
            Assert.Equal(3, updatedB.StockQuantity);   // Untouched!

            var updatedRx = await db.Prescriptions.FindAsync(rx.Id);
            Assert.NotNull(updatedRx);
            Assert.Equal(PrescriptionStatus.Issued, updatedRx.Status);

            var txCount = await db.MedicineStockTransactions.CountAsync(t => t.PrescriptionId == rx.Id);
            Assert.Equal(0, txCount);
        }
    }

    // 4. Thuốc không active: chặn cấp phát, báo lỗi nghiệp vụ rõ ràng, không trừ kho
    [Fact]
    public async Task Given_PrescriptionWithInactiveMedicine_When_Dispensed_Then_DispenseRejectedWithMedicineInactiveAndStockUnchanged()
    {
        var medInactive = await CreateTestMedicineAsync("INACT", 20, isActive: false);
        var items = new List<(long, int)> { (medInactive.Id, 5) };

        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);
        await CreatePaidInvoiceForPrescriptionAsync(rx, items);

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MEDICINE_INACTIVE", body, StringComparison.OrdinalIgnoreCase);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedMed = await db.Medicines.FindAsync(medInactive.Id);
            Assert.NotNull(updatedMed);
            Assert.Equal(20, updatedMed.StockQuantity);

            var updatedRx = await db.Prescriptions.FindAsync(rx.Id);
            Assert.NotNull(updatedRx);
            Assert.Equal(PrescriptionStatus.Issued, updatedRx.Status);
        }
    }

    // 5. Đơn ở trạng thái Draft hoặc Cancelled: không cho cấp phát (422)
    [Fact]
    public async Task Given_PrescriptionInDraftOrCancelledStatus_When_Dispensed_Then_DispenseRejectedWithPrescriptionNotDispensable()
    {
        var med = await CreateTestMedicineAsync("DRAFTCAN", 30);

        var (_, rxDraft) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Draft,
            new List<(long, int)> { (med.Id, 5) });
        var (_, rxCancelled) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Cancelled,
            new List<(long, int)> { (med.Id, 5) });

        await AuthenticateAsync("pharm@test.com");

        var resDraft = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxDraft.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resDraft.StatusCode);
        var bodyDraft = await resDraft.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_DISPENSABLE", bodyDraft, StringComparison.OrdinalIgnoreCase);

        var resCancelled = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxCancelled.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resCancelled.StatusCode);
        var bodyCancelled = await resCancelled.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_DISPENSABLE", bodyCancelled, StringComparison.OrdinalIgnoreCase);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedMed = await db.Medicines.FindAsync(med.Id);
            Assert.NotNull(updatedMed);
            Assert.Equal(30, updatedMed.StockQuantity);
        }
    }

    // 6. Đơn đã Dispensed trước đó: chặn cấp phát trùng lặp (409 Conflict)
    [Fact]
    public async Task Given_PrescriptionAlreadyDispensed_When_DispensedAgain_Then_ReturnsConflict()
    {
        var med = await CreateTestMedicineAsync("DUPDISP", 20);
        var items = new List<(long, int)> { (med.Id, 5) };
        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);
        await CreatePaidInvoiceForPrescriptionAsync(rx, items);

        await AuthenticateAsync("pharm@test.com");

        // First dispense -> OK
        var firstResponse = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Second dispense -> 409 Conflict
        var secondResponse = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var body = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_ALREADY_DISPENSED", body, StringComparison.OrdinalIgnoreCase);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedMed = await db.Medicines.FindAsync(med.Id);
            Assert.NotNull(updatedMed);
            Assert.Equal(15, updatedMed.StockQuantity); // Deducted exactly once!
        }
    }

    // 7. Race condition / concurrency: 2 request cấp phát đồng thời cùng 1 đơn thuốc -> đúng 1 request thành công, request kia bị chặn Conflict; tồn kho chỉ trừ 1 lần
    [Fact]
    public async Task Given_ConcurrentDispenseRequestsOnSamePrescription_When_ExecutedInParallel_Then_ExactlyOneSucceedsAndStockDeductedOnce()
    {
        var med = await CreateTestMedicineAsync("RACE1", 50);
        var items = new List<(long, int)> { (med.Id, 10) };
        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);
        await CreatePaidInvoiceForPrescriptionAsync(rx, items);

        var token = await GetTokenAsync("pharm@test.com");

        var client1 = Factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client2 = Factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var task1 = client1.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        var task2 = client2.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);

        var responses = await Task.WhenAll(task1, task2);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedMed = await db.Medicines.FindAsync(med.Id);
            Assert.NotNull(updatedMed);
            Assert.Equal(40, updatedMed.StockQuantity); // Exactly -10, never -20!
        }
    }

    // 8. Concurrency stock: 2 đơn thuốc khác nhau cùng tranh chấp số lượng tồn kho cuối cùng -> chỉ đơn xử lý trước thành công, tồn kho không bao giờ âm
    [Fact]
    public async Task Given_TwoPrescriptionsCompetingForLastStock_When_DispensedConcurrently_Then_StockNeverNegativeAndSecondIsRejected()
    {
        var med = await CreateTestMedicineAsync("COMPETESTOCK", 5);
        var items1 = new List<(long, int)> { (med.Id, 5) };
        var items2 = new List<(long, int)> { (med.Id, 5) };

        var (_, rx1) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items1);
        var (_, rx2) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient2EntityId, PrescriptionStatus.Issued, items2);

        await CreatePaidInvoiceForPrescriptionAsync(rx1, items1);
        await CreatePaidInvoiceForPrescriptionAsync(rx2, items2);

        var token = await GetTokenAsync("pharm@test.com");

        var client1 = Factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client2 = Factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var task1 = client1.PostAsync($"/api/v1/pharmacy/prescriptions/{rx1.Id}/dispense", null);
        var task2 = client2.PostAsync($"/api/v1/pharmacy/prescriptions/{rx2.Id}/dispense", null);

        var responses = await Task.WhenAll(task1, task2);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var rejectedCount = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity || r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, okCount);
        Assert.Equal(1, rejectedCount);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedMed = await db.Medicines.FindAsync(med.Id);
            Assert.NotNull(updatedMed);
            Assert.Equal(0, updatedMed.StockQuantity); // Never negative!
        }
    }

    // 9. RBAC / Authorization: Pharmacist và Admin thành công; Doctor, Receptionist, Patient, Anonymous bị chặn
    [Fact]
    public async Task Given_DispenseEndpoint_When_CalledByDifferentRoles_Then_EnforceRbac()
    {
        var med = await CreateTestMedicineAsync("RBAC", 100);

        // Pharmacist -> 200 OK
        var itemsPharm = new List<(long, int)> { (med.Id, 1) };
        var (_, rxPharm) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, itemsPharm);
        await CreatePaidInvoiceForPrescriptionAsync(rxPharm, itemsPharm);

        await AuthenticateAsync("pharm@test.com");
        var resPharm = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxPharm.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, resPharm.StatusCode);

        // Admin -> 200 OK
        var itemsAdmin = new List<(long, int)> { (med.Id, 1) };
        var (_, rxAdmin) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, itemsAdmin);
        await CreatePaidInvoiceForPrescriptionAsync(rxAdmin, itemsAdmin);

        await AuthenticateAsync("admin@test.com");
        var resAdmin = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxAdmin.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, resAdmin.StatusCode);

        // Doctor -> 403 Forbidden
        var (_, rxDoc) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued,
            new List<(long, int)> { (med.Id, 1) });
        await AuthenticateAsync("doc@test.com");
        var resDoc = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxDoc.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.Forbidden, resDoc.StatusCode);

        // Receptionist -> 403 Forbidden
        var (_, rxRec) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued,
            new List<(long, int)> { (med.Id, 1) });
        await AuthenticateAsync("rec@test.com");
        var resRec = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxRec.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.Forbidden, resRec.StatusCode);

        // Patient -> 403 Forbidden
        var (_, rxPat) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued,
            new List<(long, int)> { (med.Id, 1) });
        await AuthenticateAsync("pat1@test.com");
        var resPat = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxPat.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.Forbidden, resPat.StatusCode);

        // Unauthenticated -> 401 Unauthorized
        Client.DefaultRequestHeaders.Authorization = null;
        var resAnon = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxPharm.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resAnon.StatusCode);
    }

    // 10. Notification: sau khi cấp phát thành công, bệnh nhân nhận được thông báo in-app thật với route /patient/prescriptions
    [Fact]
    public async Task Given_SuccessfulDispense_When_Completed_Then_PatientReceivesRealNotificationWithRouteAndEntityId()
    {
        var med = await CreateTestMedicineAsync("NOTIF", 20);
        var items = new List<(long, int)> { (med.Id, 2) };
        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);
        await CreatePaidInvoiceForPrescriptionAsync(rx, items);

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify notification in DB
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notif = await db.Notifications
                .FirstOrDefaultAsync(n => n.UserId == Patient1Id && n.DedupeKey == $"rx_dispensed_{rx.Id}");

            Assert.NotNull(notif);
            Assert.Equal("/patient/prescriptions", notif.Route);
            Assert.Equal("Prescription", notif.RelatedEntityType);
            Assert.Equal(rx.Id.ToString(), notif.RelatedEntityId);
            Assert.Equal(NotificationType.Prescription, notif.Type);
            Assert.False(notif.IsRead);
        }

        // Verify patient sees notification via API
        await AuthenticateAsync("pat1@test.com");
        var notifResponse = await Client.GetAsync("/api/v1/notifications?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, notifResponse.StatusCode);
        var notifBody = await notifResponse.Content.ReadAsStringAsync();
        Assert.Contains("/patient/prescriptions", notifBody);
        Assert.Contains(rx.Id.ToString(), notifBody);
    }

    // 11. Data isolation & privacy: Patient không xem được tồn kho nội bộ; Patient chỉ xem đơn thuốc của mình; Doctor chỉ quản lý lịch của mình
    [Fact]
    public async Task Given_DataIsolationAndPrivacy_When_AccessedByDifferentRoles_Then_Enforced()
    {
        var med = await CreateTestMedicineAsync("PRIVACY", 30);
        var (_, rxPat1) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued,
            new List<(long, int)> { (med.Id, 1) });
        var (aptDoc1, rxPat2) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient2EntityId, PrescriptionStatus.Issued,
            new List<(long, int)> { (med.Id, 2) });

        // 1. Patient cannot view internal drug stock or pharmacy queue
        await AuthenticateAsync("pat1@test.com");
        var patMedRes = await Client.GetAsync("/api/v1/medicines/active");
        Assert.Equal(HttpStatusCode.Forbidden, patMedRes.StatusCode);

        var patPharmRes = await Client.GetAsync("/api/v1/pharmacy/prescriptions");
        Assert.Equal(HttpStatusCode.Forbidden, patPharmRes.StatusCode);

        // 2. Patient 1 only sees their own prescriptions
        var pat1RxRes = await Client.GetAsync("/api/v1/patients/me/prescriptions");
        Assert.Equal(HttpStatusCode.OK, pat1RxRes.StatusCode);
        var pat1RxBody = await pat1RxRes.Content.ReadAsStringAsync();
        Assert.Contains($"\"id\":{rxPat1.Id}", pat1RxBody);
        Assert.DoesNotContain($"\"id\":{rxPat2.Id}", pat1RxBody);

        // 3. Patient 2 only sees their own prescriptions
        await AuthenticateAsync("pat2@test.com");
        var pat2RxRes = await Client.GetAsync("/api/v1/patients/me/prescriptions");
        Assert.Equal(HttpStatusCode.OK, pat2RxRes.StatusCode);
        var pat2RxBody = await pat2RxRes.Content.ReadAsStringAsync();
        Assert.Contains($"\"id\":{rxPat2.Id}", pat2RxBody);
        Assert.DoesNotContain($"\"id\":{rxPat1.Id}", pat2RxBody);

        // 4. Doctor 2 cannot view or edit prescription draft of Doctor 1's appointment
        await AuthenticateAsync("doc2@test.com");
        var doc2Res = await Client.GetAsync($"/api/v1/doctor/appointments/{aptDoc1.Id}/prescription-draft");
        Assert.Equal(HttpStatusCode.OK, doc2Res.StatusCode);
        var doc2Body = await doc2Res.Content.ReadAsStringAsync();
        Assert.Contains("\"data\":null", doc2Body);

        var doc2EditRes = await Client.PutAsJsonAsync($"/api/v1/doctor/appointments/{aptDoc1.Id}/prescription-draft", new
        {
            PrescriptionNotes = "Forbidden update",
            PrescriptionItems = new List<object>()
        });
        Assert.Equal(HttpStatusCode.NotFound, doc2EditRes.StatusCode);
    }

    // 12. Chặn cấp phát nếu đơn thuốc chưa có hóa đơn (422 PRESCRIPTION_NOT_PAID)
    [Fact]
    public async Task Given_PrescriptionWithoutInvoice_When_Dispensed_Then_RejectedWithPrescriptionNotPaid()
    {
        var med = await CreateTestMedicineAsync("NOINV", 20);
        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued,
            new List<(long, int)> { (med.Id, 5) });

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_PAID", body, StringComparison.OrdinalIgnoreCase);

        // Verify stock untouched
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedMed = await db.Medicines.FindAsync(med.Id);
        Assert.Equal(20, updatedMed!.StockQuantity);
    }

    // 13. Chặn cấp phát nếu hóa đơn đơn thuốc chưa thanh toán (Unpaid -> 422 PRESCRIPTION_NOT_PAID)
    [Fact]
    public async Task Given_PrescriptionWithUnpaidInvoice_When_Dispensed_Then_RejectedWithPrescriptionNotPaid()
    {
        var med = await CreateTestMedicineAsync("UNPAIDINV", 20);
        var items = new List<(long, int)> { (med.Id, 5) };
        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);

        // Create Unpaid invoice
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invoices.Add(new Invoice
            {
                InvoiceCode = $"INV-UNPAID-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = rx.PatientId,
                AppointmentId = rx.AppointmentId,
                Status = InvoiceStatus.Unpaid,
                Subtotal = 50000m,
                TotalAmount = 50000m,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 5,
                        UnitPrice = 10000m,
                        LineTotal = 50000m,
                        ReferenceType = "PrescriptionItem",
                        ReferenceId = rx.Id * 100000L + med.Id,
                        IsCancelled = false
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_PAID", body, StringComparison.OrdinalIgnoreCase);
    }

    // 14. Chặn cấp phát nếu lượt khám chỉ mới thanh toán tiền khám hoặc CLS, chưa thanh toán tiền thuốc
    [Fact]
    public async Task Given_PrescriptionWhereOnlyConsultationIsPaid_When_Dispensed_Then_RejectedWithPrescriptionNotPaid()
    {
        var med = await CreateTestMedicineAsync("ONLYCONSULT", 20);
        var items = new List<(long, int)> { (med.Id, 5) };
        var (apt, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);

        // Create Paid invoice with ONLY Consultation item, no prescription items
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invoices.Add(new Invoice
            {
                InvoiceCode = $"INV-CONSULT-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = rx.PatientId,
                AppointmentId = apt.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 150000m,
                TotalAmount = 150000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = "KHAM",
                        Description = "Khám chuyên khoa",
                        Quantity = 1,
                        UnitPrice = 150000m,
                        LineTotal = 150000m,
                        ReferenceType = "Consultation",
                        ReferenceId = apt.Id,
                        IsCancelled = false
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_PAID", body, StringComparison.OrdinalIgnoreCase);
    }

    // 15. Đơn thuốc đã ReservedForPurchase: cấp phát sau khi thanh toán không bị trừ tồn kho lần hai, ghi log xác nhận
    [Fact]
    public async Task Given_ReservedPrescription_When_PaidAndDispensed_Then_DispenseConfirmationLoggedWithoutDoubleStockDeduction()
    {
        var med = await CreateTestMedicineAsync("RESERVE", 50);
        var items = new List<(long, int)> { (med.Id, 10) };
        var (_, rx) = await CreateTestPrescriptionAsync(DoctorEntityId, Patient1EntityId, PrescriptionStatus.Issued, items);

        await AuthenticateAsync("pharm@test.com");

        // 1. Confirm purchase -> status ReservedForPurchase, stock 50 -> 40
        var confirmRes = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/confirm-purchase", null);
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var m = await db.Medicines.FindAsync(med.Id);
            Assert.Equal(40, m!.StockQuantity); // Deducted during reservation
        }

        // 2. Cannot dispense before payment
        var earlyDispense = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, earlyDispense.StatusCode);

        // 3. Invoice is paid
        await CreatePaidInvoiceForPrescriptionAsync(rx, items);

        // 4. Dispense succeeds without second deduction
        var dispenseRes = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rx.Id}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, dispenseRes.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var m = await db.Medicines.FindAsync(med.Id);
            Assert.Equal(40, m!.StockQuantity); // Still 40! Never 30!

            var updatedRx = await db.Prescriptions.FindAsync(rx.Id);
            Assert.Equal(PrescriptionStatus.Dispensed, updatedRx!.Status);

            var txs = await db.MedicineStockTransactions
                .Where(t => t.PrescriptionId == rx.Id)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();

            Assert.Equal(2, txs.Count);
            Assert.Equal(MedicineStockTransactionType.Reservation, txs[0].Type);
            Assert.Equal(-10, txs[0].QuantityChange);

            Assert.Equal(MedicineStockTransactionType.Dispense, txs[1].Type);
            Assert.Equal(0, txs[1].QuantityChange);
        }
    }

    // 16. Đơn thuốc Dispensed trong quá khứ nhưng chưa lập hóa đơn: không bị bỏ sót khi truy vấn unbilled-visits và lập được hóa đơn
    [Fact]
    public async Task Given_DispensedPrescription_When_Unbilled_Then_IncludedInUnbilledVisits()
    {
        var med = await CreateTestMedicineAsync("UNBILLEDDISP", 50);

        long visitId;
        long rxId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-TEST-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.Completed,
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Priority = VisitPriority.Normal,
                QueueNumber = 999,
                CheckedInAtUtc = DateTime.UtcNow
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Dispensed,
                Notes = "Đã phát thuốc nhưng chưa lập hóa đơn",
                CreatedAt = DateTime.UtcNow
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            rxId = rx.Id;

            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = rx.Id,
                MedicineId = med.Id,
                Quantity = 5,
                Dosage = "1 viên",
                Frequency = "1 lần/ngày",
                DurationDays = 5
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("rec@test.com");

        // Query unbilled visits
        var unbilledRes = await Client.GetAsync("/api/v1/reception/billing/unbilled-visits");
        Assert.Equal(HttpStatusCode.OK, unbilledRes.StatusCode);
        var unbilledDoc = JsonDocument.Parse(await unbilledRes.Content.ReadAsStringAsync());
        var unbilledList = unbilledDoc.RootElement.GetProperty("data");

        bool found = false;
        foreach (var item in unbilledList.EnumerateArray())
        {
            if (item.GetProperty("visitId").GetInt64() == visitId)
            {
                found = true;
                Assert.True(item.GetProperty("unbilledItemCount").GetInt32() > 0);
                break;
            }
        }
        Assert.True(found, "Dispensed unbilled prescription must be included in unbilled visits");

        // Bill the visit
        var createInvRes = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new CreateVisitInvoiceRequest
        {
            PatientVisitId = visitId
        });
        Assert.Equal(HttpStatusCode.Created, createInvRes.StatusCode);
        var invDoc = JsonDocument.Parse(await createInvRes.Content.ReadAsStringAsync());
        var invItems = invDoc.RootElement.GetProperty("data").GetProperty("items");

        bool rxItemBilled = false;
        foreach (var i in invItems.EnumerateArray())
        {
            if (i.GetProperty("referenceType").GetString() == "PrescriptionItem")
            {
                rxItemBilled = true;
                Assert.Equal(50000m, i.GetProperty("lineTotal").GetDecimal());
                break;
            }
        }
        Assert.True(rxItemBilled, "Unbilled items of dispensed prescription must be included in generated invoice");
    }
}
