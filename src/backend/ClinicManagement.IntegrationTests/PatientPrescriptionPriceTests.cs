using System.Net;
using System.Text.Json;
using ClinicManagement.Application.Patients.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class PatientPrescriptionPriceTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task SavedInvoicePriceIsUsedInsteadOfCurrentMedicinePrice()
    {
        var prescription = await CreatePrescriptionAsync(5000m);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = CreateInvoice(prescription, 1200m, InvoiceStatus.Paid, Patient1EntityId);
            var cancelledItem = CreateInvoice(prescription, 9000m, InvoiceStatus.Paid, Patient1EntityId).Items.Single();
            cancelledItem.IsCancelled = true;
            invoice.Items.Add(cancelledItem);
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var result = await ReadPrescriptionAsync(prescription.Id);
        var item = Assert.Single(result.Items);
        Assert.Equal(1200m, item.UnitPrice);
        Assert.Equal(3600m, item.LineTotal);
        Assert.Equal(3600m, result.TotalAmount);
        Assert.False(result.PriceIsReference);
    }

    [Fact]
    public async Task CurrentMedicinePriceIsReturnedAsReferenceWhenNoValidInvoicePriceExists()
    {
        var prescription = await CreatePrescriptionAsync(2500m);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invoices.Add(CreateInvoice(prescription, 9000m, InvoiceStatus.Cancelled, Patient1EntityId));
            await db.SaveChangesAsync();
        }

        var result = await ReadPrescriptionAsync(prescription.Id);
        var item = Assert.Single(result.Items);
        Assert.Equal(2500m, item.UnitPrice);
        Assert.Equal(7500m, item.LineTotal);
        Assert.Equal(7500m, result.TotalAmount);
        Assert.True(result.PriceIsReference);
    }

    [Fact]
    public async Task MissingMedicineAndInvoicePricesAreReturnedAsNull()
    {
        var prescription = await CreatePrescriptionAsync(null);
        var result = await ReadPrescriptionAsync(prescription.Id);
        var item = Assert.Single(result.Items);
        Assert.Null(item.UnitPrice);
        Assert.Null(item.LineTotal);
        Assert.Null(result.TotalAmount);
        Assert.True(result.PriceIsReference);
    }

    [Fact]
    public async Task SavedInvoiceUnitPriceUsesPrescribedQuantityWhenInvoiceQuantityDiffers()
    {
        var prescription = await CreatePrescriptionAsync(5000m);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = CreateInvoice(prescription, 1200m, InvoiceStatus.Paid, Patient1EntityId);
            var invoiceItem = invoice.Items.Single();
            invoiceItem.Quantity = 2;
            invoiceItem.LineTotal = 2400m;
            invoice.Subtotal = 2400m;
            invoice.TotalAmount = 2400m;
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var result = await ReadPrescriptionAsync(prescription.Id);
        var item = Assert.Single(result.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(1200m, item.UnitPrice);
        Assert.Equal(3600m, item.LineTotal);
        Assert.Equal(3600m, result.TotalAmount);
        Assert.False(result.PriceIsReference);
    }

    private async Task<Prescription> CreatePrescriptionAsync(decimal? unitPrice)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = new Medicine
        {
            Code = $"PRICE-{Guid.NewGuid():N}"[..18], Name = "Thuốc kiểm thử giá", Unit = "Viên",
            StockQuantity = 20, ReorderLevel = 5, UnitPrice = unitPrice, IsActive = true,
        };
        var appointment = new Appointment
        {
            AppointmentCode = $"APT-PRICE-{Guid.NewGuid():N}"[..22],
            DoctorId = DoctorEntityId, PatientId = Patient1EntityId, SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = SlotEntityId, Status = AppointmentStatus.Completed,
            AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow), StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(14, 30),
        };
        var prescription = new Prescription
        {
            Appointment = appointment, PatientId = Patient1EntityId, DoctorId = DoctorEntityId, Status = PrescriptionStatus.Issued,
            Items = [new PrescriptionItem { Medicine = medicine, Quantity = 3, Dosage = "1 viên", Frequency = "3 lần/ngày" }],
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        return prescription;
    }

    private static Invoice CreateInvoice(Prescription prescription, decimal price, InvoiceStatus status, long patientId) => new()
    {
        InvoiceCode = $"INV-PRICE-{Guid.NewGuid():N}"[..22], PatientId = patientId, AppointmentId = prescription.AppointmentId,
        Status = status, CreatedByUserId = AdminId, Subtotal = price * 3, TotalAmount = price * 3,
        Items = [new InvoiceItem
        {
            ItemCode = prescription.Items.Single().Medicine!.Code, Description = "Thuốc kiểm thử giá", Quantity = 3,
            UnitPrice = price, LineTotal = price * 3, ReferenceType = PrescriptionItemBillingReference.ModernReferenceType,
            ReferenceId = PrescriptionItemBillingReference.Encode(prescription.Id, prescription.Items.Single().MedicineId),
        }],
    };

    private async Task<PatientPrescriptionDto> ReadPrescriptionAsync(long id)
    {
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.GetAsync("/api/v1/patients/me/prescriptions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var prescriptions = json.RootElement.GetProperty("data").Deserialize<List<PatientPrescriptionDto>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Assert.Single(prescriptions!, prescription => prescription.Id == id);
    }
}
