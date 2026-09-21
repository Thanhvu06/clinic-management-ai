using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class OutpatientHardenFlowTests : IntegrationTestBase
{
    public OutpatientHardenFlowTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<Medicine> CreateTestMedicineAsync(string prefix, int initialStock)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var med = new Medicine
        {
            Code = $"MED-{prefix}-{Guid.NewGuid():N}"[..15].ToUpper(),
            Name = $"Thuốc Test {prefix}",
            Unit = "Viên",
            StockQuantity = initialStock,
            UnitPrice = 15000m,
            ReorderLevel = 5,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Medicines.Add(med);
        await db.SaveChangesAsync();
        return med;
    }

    [Fact]
    public async Task Given_PrescriptionWithLegacyWholeRxPaidOnly_When_Dispensed_Then_RejectedDueToNoItemLevelPayment()
    {
        var med = await CreateTestMedicineAsync("LEGACY", 50);
        long rxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-LEG-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
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
                Frequency = "2 lần/ngày",
                DurationDays = 5
            });

            // Legacy whole-Rx invoice item: ReferenceId == rx.Id (WITHOUT medicineId encoding)
            var inv = new Invoice
            {
                InvoiceCode = $"INV-LEG-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 75000m,
                TotalAmount = 75000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        ItemCode = "MED-LEGACY",
                        Description = "Legacy whole prescription payment",
                        Quantity = 1,
                        UnitPrice = 75000m,
                        LineTotal = 75000m,
                        ReferenceType = "PrescriptionItem",
                        ReferenceId = rx.Id, // NOT encoded with medicine ID
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var res = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxId}/dispense", null);

        // Since hasLegacyWholeRxPaid bypass was removed, dispensing must be rejected
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_NOT_PAID", body);
    }

    [Fact]
    public async Task Given_PrescriptionWithModern64BitEncodedItem_When_Dispensed_Then_Succeeds()
    {
        var med = await CreateTestMedicineAsync("MOD64", 50);
        long rxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-M64-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
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
                Frequency = "2 lần/ngày",
                DurationDays = 5
            });

            // Modern 64-bit encoded reference: PrescriptionItemBillingReference.Encode(rx.Id, med.Id)
            var modernRefId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id);
            var inv = new Invoice
            {
                InvoiceCode = $"INV-M64-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 75000m,
                TotalAmount = 75000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 5,
                        UnitPrice = 15000m,
                        LineTotal = 75000m,
                        ReferenceType = "PrescriptionItem",
                        ReferenceId = modernRefId,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var res = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxId}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedMed = await db.Medicines.FindAsync(med.Id);
            Assert.Equal(45, updatedMed!.StockQuantity);

            var updatedRx = await db.Prescriptions.FindAsync(rxId);
            Assert.Equal(PrescriptionStatus.Dispensed, updatedRx!.Status);
        }
    }

    [Fact]
    public async Task Given_VisitWithPendingPrescriptions_When_InvoicePaid_Then_VisitStatusBecomesInPharmacyAndNotCompleted()
    {
        var med = await CreateTestMedicineAsync("BILLSAFE", 50);
        long visitId;
        long invoiceId;
        long rxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-BS-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            rxId = rx.Id;

            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = rx.Id,
                MedicineId = med.Id,
                Quantity = 4,
                Dosage = "1 viên",
                Frequency = "2 lần/ngày",
                DurationDays = 2
            });

            var modernRefId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id);
            var inv = new Invoice
            {
                InvoiceCode = $"INV-BS-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Unpaid,
                Subtotal = 60000m,
                TotalAmount = 60000m,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 4,
                        UnitPrice = 15000m,
                        LineTotal = 60000m,
                        ReferenceType = "PrescriptionItem",
                        ReferenceId = modernRefId,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
            invoiceId = inv.Id;
        }

        // Cashier pays invoice
        await AuthenticateAsync("rec@test.com");
        var payRes = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new ProcessPaymentRequest
        {
            Amount = 60000m,
            Method = PaymentMethod.Cash,
            ReferenceCode = "CASH-SAFE-01"
        });
        Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);

        // Verification: Visit must transition to InPharmacy, NOT Completed!
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.FindAsync(visitId);
            Assert.Equal(VisitStatus.InPharmacy, visit!.Status);
            Assert.Null(visit.CompletedAtUtc);
        }

        // Now Pharmacist dispenses prescription
        await AuthenticateAsync("pharm@test.com");
        var dispRes = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxId}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, dispRes.StatusCode);

        // Now that prescription is dispensed and invoice is paid -> Visit must be Completed!
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.FindAsync(visitId);
            Assert.Equal(VisitStatus.Completed, visit!.Status);
            Assert.NotNull(visit.CompletedAtUtc);
        }
    }

    [Fact]
    public async Task Given_PrescriptionDraft_When_BilledInActiveInvoice_Then_DoctorCannotEditPrescriptionDraft()
    {
        var med = await CreateTestMedicineAsync("LOCKMED", 50);
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
                VisitCode = $"VIS-LCK-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InConsultation,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            rxId = rx.Id;

            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = rx.Id,
                MedicineId = med.Id,
                Quantity = 3,
                Dosage = "1 viên",
                Frequency = "1 lần/ngày",
                DurationDays = 3
            });

            // Invoice generated with active item referencing this prescription
            var refId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id);
            var inv = new Invoice
            {
                InvoiceCode = $"INV-LCK-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Unpaid,
                Subtotal = 45000m,
                TotalAmount = 45000m,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 3,
                        UnitPrice = 15000m,
                        LineTotal = 45000m,
                        ReferenceType = "PrescriptionItem",
                        ReferenceId = refId,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        // Doctor attempts to edit prescription draft
        await AuthenticateAsync("doc@test.com");
        var editRes = await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visitId}/prescription-draft", new SavePrescriptionDraftRequest
        {
            Notes = "Cập nhật liều dùng",
            Items = new List<SavePrescriptionItemRequest>
            {
                new()
                {
                    MedicineId = med.Id,
                    Quantity = 5,
                    Dosage = "2 viên",
                    Frequency = "2 lần/ngày",
                    DurationDays = 5
                }
            }
        });

        Assert.True(editRes.StatusCode == HttpStatusCode.BadRequest || editRes.StatusCode == HttpStatusCode.UnprocessableEntity);
        var errBody = await editRes.Content.ReadAsStringAsync();
        Assert.Contains("PRESCRIPTION_ALREADY_BILLED", errBody);
    }

    [Fact]
    public async Task Given_PrescriptionItemPartiallyPaid_When_Dispensed_Then_RejectedWithInsufficientPaidQuantity()
    {
        var med = await CreateTestMedicineAsync("PARTIAL", 50);
        long rxId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = await db.Patients.FirstAsync();
            var doc = await db.Doctors.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var visit = new PatientVisit
            {
                VisitCode = $"VIS-PART-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                FacilityId = dept.FacilityId,
                DepartmentId = dept.Id,
                AssignedDoctorId = doc.Id,
                Status = VisitStatus.InPharmacy,
                Priority = VisitPriority.Normal,
                QueueNumber = Random.Shared.Next(1000, 999999),
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();

            var rx = new Prescription
            {
                PatientVisitId = visit.Id,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow
            };
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync();
            rxId = rx.Id;

            // Prescription requests 10 units
            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = rx.Id,
                MedicineId = med.Id,
                Quantity = 10,
                Dosage = "1 viên",
                Frequency = "2 lần/ngày",
                DurationDays = 5
            });

            // Invoice only covers 6 units
            var refId = PrescriptionItemBillingReference.Encode(rx.Id, med.Id);
            var inv = new Invoice
            {
                InvoiceCode = $"INV-PART-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                PatientVisitId = visit.Id,
                Status = InvoiceStatus.Paid,
                Subtotal = 90000m,
                TotalAmount = 90000m,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                Items = new List<InvoiceItem>
                {
                    new InvoiceItem
                    {
                        ItemCode = med.Code,
                        Description = med.Name,
                        Quantity = 6, // only 6 of 10 paid
                        UnitPrice = 15000m,
                        LineTotal = 90000m,
                        ReferenceType = "PrescriptionItem",
                        ReferenceId = refId,
                        IsCancelled = false
                    }
                }
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("pharm@test.com");
        var res = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{rxId}/dispense", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var msg = jsonDoc.RootElement.GetProperty("message").GetString();
        Assert.Contains("chưa được thanh toán đủ số lượng", msg);
    }
}
