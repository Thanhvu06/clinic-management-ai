using System;
using System.Linq;
using System.Net;
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

    [Fact]
    public async Task Given_IssuedPrescription_When_DispensedWithSufficientStock_Then_StockDeductedAndStatusUpdated()
    {
        long prescriptionId;
        int initialStock;

        // 1. Setup Appointment & Prescription in database
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var medicine = await db.Medicines.FindAsync(MedicineEntityId);
            Assert.NotNull(medicine);
            initialStock = medicine.StockQuantity;

            var apt = new Appointment
            {
                AppointmentCode = $"APT-PHARM-{Guid.NewGuid():N}"[..18],
                DoctorId = DoctorEntityId,
                PatientId = Patient1EntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = SlotEntityId,
                Status = AppointmentStatus.Completed,
                AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(9, 30)
            };
            db.Appointments.Add(apt);
            await db.SaveChangesAsync();

            var prescription = new Prescription
            {
                AppointmentId = apt.Id,
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow,
                Notes = "Đơn thuốc đủ tồn kho"
            };
            db.Prescriptions.Add(prescription);
            await db.SaveChangesAsync();

            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = prescription.Id,
                MedicineId = MedicineEntityId,
                Quantity = 5,
                Dosage = "1 viên",
                Frequency = "2 lần/ngày",
                DurationDays = 5,
                Instructions = "Uống sau ăn"
            });
            await db.SaveChangesAsync();

            prescriptionId = prescription.Id;
        }

        // 2. Authenticate as Pharmacist and dispense
        await AuthenticateAsync("pharm@test.com");
        var dispenseResponse = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{prescriptionId}/dispense", null);
        Assert.Equal(HttpStatusCode.OK, dispenseResponse.StatusCode);

        // 3. Verify status, stock reduction and audit transaction in database
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var prescription = await db.Prescriptions.FindAsync(prescriptionId);
            Assert.NotNull(prescription);
            Assert.Equal(PrescriptionStatus.Dispensed, prescription.Status);
            Assert.NotNull(prescription.DispensedAt);

            var medicine = await db.Medicines.FindAsync(MedicineEntityId);
            Assert.NotNull(medicine);
            Assert.Equal(initialStock - 5, medicine.StockQuantity);

            var tx = await db.MedicineStockTransactions
                .FirstOrDefaultAsync(t => t.PrescriptionId == prescriptionId && t.MedicineId == MedicineEntityId);
            Assert.NotNull(tx);
            Assert.Equal(MedicineStockTransactionType.Dispense, tx.Type);
            Assert.Equal(-5, tx.QuantityChange);
        }

        // 4. Dispense again must fail (ALREADY_DISPENSED)
        var secondDispenseResponse = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{prescriptionId}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, secondDispenseResponse.StatusCode);
    }

    [Fact]
    public async Task Given_Prescription_When_StockIsInsufficient_Then_DispenseFailsAndStockUnchanged()
    {
        long prescriptionId;
        int currentStock;

        // Setup prescription requiring more items than current stock
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var medicine = await db.Medicines.FindAsync(MedicineEntityId);
            Assert.NotNull(medicine);
            currentStock = medicine.StockQuantity;

            var apt = new Appointment
            {
                AppointmentCode = $"APT-INSUF-{Guid.NewGuid():N}"[..18],
                DoctorId = DoctorEntityId,
                PatientId = Patient1EntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = SlotEntityId,
                Status = AppointmentStatus.Completed,
                AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(10, 30)
            };
            db.Appointments.Add(apt);
            await db.SaveChangesAsync();

            var prescription = new Prescription
            {
                AppointmentId = apt.Id,
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                Status = PrescriptionStatus.Issued,
                CreatedAt = DateTime.UtcNow,
                Notes = "Đơn thuốc thiếu tồn kho"
            };
            db.Prescriptions.Add(prescription);
            await db.SaveChangesAsync();

            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = prescription.Id,
                MedicineId = MedicineEntityId,
                Quantity = currentStock + 999, // Exceeds available stock
                Dosage = "2 viên",
                Frequency = "3 lần/ngày",
                DurationDays = 30,
                Instructions = "Uống liều cao"
            });
            await db.SaveChangesAsync();

            prescriptionId = prescription.Id;
        }

        // Attempt dispense
        await AuthenticateAsync("pharm@test.com");
        var response = await Client.PostAsync($"/api/v1/pharmacy/prescriptions/{prescriptionId}/dispense", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INSUFFICIENT_STOCK", body, StringComparison.OrdinalIgnoreCase);

        // Verify stock remains unchanged and prescription is still Issued
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var medicine = await db.Medicines.FindAsync(MedicineEntityId);
            Assert.NotNull(medicine);
            Assert.Equal(currentStock, medicine.StockQuantity);

            var prescription = await db.Prescriptions.FindAsync(prescriptionId);
            Assert.NotNull(prescription);
            Assert.Equal(PrescriptionStatus.Issued, prescription.Status);
        }
    }
}
