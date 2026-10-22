using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Pharmacy.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class PharmacyPaymentStatusTests : IntegrationTestBase
{
    public PharmacyPaymentStatusTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Payment_status_changes_only_after_cashier_collects_full_payment()
    {
        long prescriptionId;
        long invoiceId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var facilityId = (await db.Facilities.FirstAsync(f => f.IsActive)).Id;
            var appointment = new Appointment
            {
                AppointmentCode = $"PAY-{Guid.NewGuid():N}"[..18], PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId,
                FacilityId = facilityId, Status = AppointmentStatus.Completed,
                AppointmentDate = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday,
                StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(14, 30)
            };
            var prescription = new Prescription
            {
                Appointment = appointment, PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
                Status = PrescriptionStatus.Issued,
                Items = new List<PrescriptionItem>
                {
                    new() { MedicineId = MedicineEntityId, Quantity = 2, Dosage = "1 viên", Frequency = "2 lần/ngày" }
                }
            };
            db.Prescriptions.Add(prescription);
            await db.SaveChangesAsync();
            prescriptionId = prescription.Id;
            var invoice = new Invoice
            {
                InvoiceCode = $"PAY-{Guid.NewGuid():N}"[..18], PatientId = Patient1EntityId,
                AppointmentId = appointment.Id, SourceType = InvoiceSourceType.Appointment,
                Status = InvoiceStatus.Unpaid, CreatedByUserId = ReceptionistId, Subtotal = 20000m, TotalAmount = 20000m,
                Items = new List<InvoiceItem>
                {
                    new()
                    {
                        ItemCode = "MED-PAY", Description = "Thuốc kiểm tra thanh toán", Quantity = 2,
                        UnitPrice = 10000m, LineTotal = 20000m, ReferenceType = "PrescriptionItem",
                        ReferenceId = prescriptionId * 100000L + MedicineEntityId
                    }
                }
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        var url = $"/api/v1/pharmacy/prescriptions/{prescriptionId}/payment-status";
        var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var before = await pharmacist.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var unpaid = await before.Content.ReadFromJsonAsync<ApiResponse<PrescriptionPaymentEligibilityDto>>();
        Assert.NotNull(unpaid?.Data);
        Assert.Equal(prescriptionId, unpaid.Data.PrescriptionId);
        Assert.False(unpaid.Data.IsFullyPaid);
        Assert.Equal("unpaid", unpaid.Data.PaymentStatus);
        Assert.Equal(0, Assert.Single(unpaid.Data.Items).PaidQuantity);

        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.GetAsync(url)).StatusCode);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync(url)).StatusCode);
        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.GetAsync(url)).StatusCode);

        var payment = await reception.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new
        {
            amount = 20000m, method = PaymentMethod.Cash
        });
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);
        var after = await pharmacist.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var paid = await after.Content.ReadFromJsonAsync<ApiResponse<PrescriptionPaymentEligibilityDto>>();
        Assert.NotNull(paid?.Data);
        Assert.True(paid.Data.IsFullyPaid);
        Assert.Equal("paid_in_full", paid.Data.PaymentStatus);
        Assert.Equal(2, Assert.Single(paid.Data.Items).PaidQuantity);
        var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
    }
}
