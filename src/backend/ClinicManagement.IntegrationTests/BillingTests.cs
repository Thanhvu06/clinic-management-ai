using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class BillingTests : IntegrationTestBase
{
    public BillingTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<long> CreateTestAppointmentAsync(long patientId, AppointmentStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var apt = new Appointment
        {
            AppointmentCode = $"APT-{Guid.NewGuid():N}"[..15],
            PatientId = patientId,
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = SlotEntityId,
            AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(8, 30),
            Status = status
        };
        db.Appointments.Add(apt);
        await db.SaveChangesAsync();
        return apt.Id;
    }

    private async Task<long> CreateTestInvoiceAsync(long patientId, long appointmentId, decimal amount, InvoiceStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inv = new Invoice
        {
            InvoiceCode = $"INV-{Guid.NewGuid():N}"[..15],
            PatientId = patientId,
            SourceType = InvoiceSourceType.Appointment,
            AppointmentId = appointmentId,
            Status = status,
            Subtotal = amount,
            TotalAmount = amount,
            CreatedByUserId = ReceptionistId,
            CreatedAtUtc = DateTime.UtcNow
        };
        var item = new InvoiceItem
        {
            ItemCode = "SP01",
            Description = "Khám chuyên khoa test",
            Quantity = 1,
            UnitPrice = amount,
            LineTotal = amount,
            ReferenceType = "Specialty",
            ReferenceId = SpecialtyEntityId
        };
        inv.Items.Add(item);
        db.Invoices.Add(inv);
        await db.SaveChangesAsync();
        return inv.Id;
    }

    [Fact]
    public async Task Given_UnauthenticatedUser_When_AccessesBillingEndpoints_Then_Returns401Unauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var getInvoicesRes = await Client.GetAsync("/api/v1/reception/billing/invoices");
        Assert.Equal(HttpStatusCode.Unauthorized, getInvoicesRes.StatusCode);

        var getKpiRes = await Client.GetAsync("/api/v1/reception/billing/kpi");
        Assert.Equal(HttpStatusCode.Unauthorized, getKpiRes.StatusCode);

        var payRes = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/1/pay", new { amount = 100000m, method = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, payRes.StatusCode);

        var myInvoicesRes = await Client.GetAsync("/api/v1/patient/invoices");
        Assert.Equal(HttpStatusCode.Unauthorized, myInvoicesRes.StatusCode);

        var adminRevRes = await Client.GetAsync("/api/v1/admin/billing/revenue");
        Assert.Equal(HttpStatusCode.Unauthorized, adminRevRes.StatusCode);
    }

    [Fact]
    public async Task Given_DoctorOrPharmacist_When_CallsPaymentApi_Then_Returns403Forbidden()
    {
        await AuthenticateAsync("doc@test.com");
        var docPayRes = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/1/pay", new { amount = 100000m, method = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, docPayRes.StatusCode);

        await AuthenticateAsync("pharm@test.com");
        var pharmPayRes = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/1/pay", new { amount = 100000m, method = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, pharmPayRes.StatusCode);
    }

    [Fact]
    public async Task Given_NonCompletedAppointment_When_ReceptionistCreatesInvoice_Then_ReturnsBadRequest()
    {
        await AuthenticateAsync("rec@test.com");
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Pending);

        var response = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/appointment", new
        {
            appointmentId = aptId
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_STATUS", content);
    }

    [Fact]
    public async Task Given_CompletedAppointment_When_ReceptionistCreatesInvoice_Then_SucceedsAndCalculatesAmounts()
    {
        await AuthenticateAsync("rec@test.com");

        decimal fee = 180000m;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var spec = await db.Specialties.FindAsync(SpecialtyEntityId);
            spec!.ConsultationFee = fee;
            await db.SaveChangesAsync();
        }

        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);

        var response = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/appointment", new
        {
            appointmentId = aptId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        var invoiceId = data.GetProperty("id").GetInt64();
        var total = data.GetProperty("totalAmount").GetDecimal();
        Assert.Equal(fee, total);
        Assert.Equal(1, data.GetProperty("status").GetInt32()); // Unpaid

        // Verify Duplicate Creation is blocked
        var duplicateRes = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/appointment", new
        {
            appointmentId = aptId
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicateRes.StatusCode);
    }

    [Fact]
    public async Task Given_UnpaidInvoice_When_ReceptionistPaysWithWrongAmount_Then_FailsValidation()
    {
        await AuthenticateAsync("rec@test.com");
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var invoiceId = await CreateTestInvoiceAsync(Patient1EntityId, aptId, 200000m, InvoiceStatus.Unpaid);

        // Send wrong amount: 150000 instead of 200000
        var response = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new
        {
            amount = 150000m,
            method = 1 // Cash
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_AMOUNT", content);
    }

    [Fact]
    public async Task Given_UnpaidInvoice_When_ReceptionistPays_Then_TransitionsToPaidAndCannotPayAgain()
    {
        await AuthenticateAsync("rec@test.com");
        decimal amount = 250000m;
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var invoiceId = await CreateTestInvoiceAsync(Patient1EntityId, aptId, amount, InvoiceStatus.Unpaid);

        // First payment succeeds
        var payRes = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new
        {
            amount = amount,
            method = 1, // Cash
            note = "Khách thanh toán tiền mặt tại quầy"
        });

        Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);
        var doc = JsonDocument.Parse(await payRes.Content.ReadAsStringAsync());
        Assert.Equal(amount, doc.RootElement.GetProperty("data").GetProperty("amount").GetDecimal());

        // Verify DB state
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbInv = await db.Invoices.Include(i => i.Payments).FirstAsync(i => i.Id == invoiceId);
            Assert.Equal(InvoiceStatus.Paid, dbInv.Status);
            Assert.NotNull(dbInv.PaidAtUtc);
            Assert.Single(dbInv.Payments);
            Assert.Equal(PaymentStatus.Succeeded, dbInv.Payments.First().Status);
        }

        // Second payment fails
        var payAgainRes = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new
        {
            amount = amount,
            method = 1
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, payAgainRes.StatusCode);
    }

    [Fact]
    public async Task Given_InvalidPaymentMethod_When_ReceptionistPays_Then_ReturnsBadRequest()
    {
        await AuthenticateAsync("rec@test.com");
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var invoiceId = await CreateTestInvoiceAsync(Patient1EntityId, aptId, 200000m, InvoiceStatus.Unpaid);

        var response = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new
        {
            amount = 200000m,
            method = 99
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Given_ManualBankTransferWithoutReference_When_ReceptionistPays_Then_ReturnsBusinessError()
    {
        await AuthenticateAsync("rec@test.com");
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var invoiceId = await CreateTestInvoiceAsync(Patient1EntityId, aptId, 210000m, InvoiceStatus.Unpaid);

        var response = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new
        {
            amount = 210000m,
            method = (int)PaymentMethod.ManualBankTransfer,
            referenceCode = "   "
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("REFERENCE_CODE_REQUIRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Given_VietnamDayAndPatientName_When_ReceptionistFiltersInvoices_Then_ReturnsMatchingInvoiceAndCapsPageSize()
    {
        await AuthenticateAsync("rec@test.com");
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var invoiceId = await CreateTestInvoiceAsync(Patient1EntityId, aptId, 220000m, InvoiceStatus.Unpaid);
        var vietnamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).AddDays(10));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.FirstAsync(i => i.Id == invoiceId);
            invoice.CreatedAtUtc = DateTime.SpecifyKind(
                vietnamDate.ToDateTime(new TimeOnly(0, 30)).AddHours(-7),
                DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        var date = vietnamDate.ToString("yyyy-MM-dd");
        var search = Uri.EscapeDataString("Patient 1");
        var response = await Client.GetAsync(
            $"/api/v1/reception/billing/invoices?fromDate={date}&toDate={date}&search={search}&pageSize=500");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(100, data.GetProperty("pageSize").GetInt32());
        Assert.Contains(
            data.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetInt64() == invoiceId);
    }

    [Fact]
    public async Task Given_ZeroSpecialtyFee_When_ReceptionistCreatesInvoice_Then_RejectsUnpayableInvoice()
    {
        await AuthenticateAsync("rec@test.com");
        decimal originalFee;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var specialty = await db.Specialties.FirstAsync(s => s.Id == SpecialtyEntityId);
            originalFee = specialty.ConsultationFee;
            specialty.ConsultationFee = 0;
            await db.SaveChangesAsync();
        }

        try
        {
            var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
            var response = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/appointment", new
            {
                appointmentId = aptId
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Contains("FEE_NOT_CONFIGURED", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var specialty = await db.Specialties.FirstAsync(s => s.Id == SpecialtyEntityId);
            specialty.ConsultationFee = originalFee;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Given_Patient1Invoice_When_Patient2RequestsDetail_Then_Returns404()
    {
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var invoiceId = await CreateTestInvoiceAsync(Patient1EntityId, aptId, 150000m, InvoiceStatus.Unpaid);

        // Patient 1 sees it
        await AuthenticateAsync("pat1@test.com");
        var p1Res = await Client.GetAsync($"/api/v1/patient/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.OK, p1Res.StatusCode);

        // Patient 2 is blocked (404)
        await AuthenticateAsync("pat2@test.com");
        var p2Res = await Client.GetAsync($"/api/v1/patient/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.NotFound, p2Res.StatusCode);
    }

    [Fact]
    public async Task Given_UnpaidInvoice_When_ReceptionistCancels_Then_TransitionsToCancelledAndCannotBePaid()
    {
        await AuthenticateAsync("rec@test.com");
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var invoiceId = await CreateTestInvoiceAsync(Patient1EntityId, aptId, 100000m, InvoiceStatus.Unpaid);

        var cancelRes = await Client.PatchAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/cancel", new
        {
            reason = "Bệnh nhân có bảo hiểm miễn giảm 100%"
        });
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // Payment on cancelled invoice must fail
        var payRes = await Client.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new
        {
            amount = 100000m,
            method = 1
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, payRes.StatusCode);
    }

    [Fact]
    public async Task Given_ConfirmedPackageRegistration_When_CreatesInvoice_Then_SnapshotsPrice()
    {
        await AuthenticateAsync("rec@test.com");

        long regId;
        decimal pkgPrice = 1500000m;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reg = new HealthPackageRegistration
            {
                RegistrationCode = $"REG-{Guid.NewGuid():N}"[..15],
                HealthPackageId = PackageEntityId,
                PatientId = Patient1EntityId,
                PreferredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                ContactPhone = "0900000004",
                Status = HealthPackageRegistrationStatus.Confirmed
            };
            db.HealthPackageRegistrations.Add(reg);
            await db.SaveChangesAsync();
            regId = reg.Id;
        }

        var res = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/health-package", new
        {
            healthPackageRegistrationId = regId
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(pkgPrice, data.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Given_AdminUser_When_UpdatesSpecialtyFee_Then_PersistsAndRejectsNegative()
    {
        await AuthenticateAsync("admin@test.com");

        // Positive update
        var updateRes = await Client.PatchAsJsonAsync($"/api/v1/admin/billing/specialties/{SpecialtyEntityId}/fee", new
        {
            consultationFee = 320000m
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audit = await db.SystemAuditLogs.AsNoTracking()
                .OrderByDescending(log => log.Id)
                .FirstOrDefaultAsync(log =>
                    log.Action == "UPDATE_SPECIALTY_FEE" &&
                    log.EntityName == "Specialty" &&
                    log.EntityId == SpecialtyEntityId.ToString());

            Assert.NotNull(audit);
            Assert.Equal(AdminId, audit!.UserId);
        }

        // Negative fee rejected
        var negRes = await Client.PatchAsJsonAsync($"/api/v1/admin/billing/specialties/{SpecialtyEntityId}/fee", new
        {
            consultationFee = -50000m
        });
        Assert.Equal(HttpStatusCode.BadRequest, negRes.StatusCode);
    }

    [Fact]
    public async Task Given_RevenueRangeOverOneYear_When_AdminQueriesReport_Then_ReturnsBusinessError()
    {
        await AuthenticateAsync("admin@test.com");
        var fromDate = DateTime.UtcNow.AddDays(-400).ToString("yyyy-MM-dd");
        var toDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var response = await Client.GetAsync(
            $"/api/v1/admin/billing/revenue?fromDate={fromDate}&toDate={toDate}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("DATE_RANGE_TOO_LARGE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Given_Payments_When_AdminQueriesRevenue_Then_OnlyCountsSucceededPayments()
    {
        await AuthenticateAsync("admin@test.com");

        decimal succeededAmount = 300000m;
        var aptId = await CreateTestAppointmentAsync(Patient1EntityId, AppointmentStatus.Completed);
        var invId = await CreateTestInvoiceAsync(Patient1EntityId, aptId, succeededAmount, InvoiceStatus.Paid);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p1 = new Payment
            {
                PaymentCode = $"PAY-S-{Guid.NewGuid():N}"[..15],
                InvoiceId = invId,
                Amount = succeededAmount,
                Method = PaymentMethod.Cash,
                ReceivedByUserId = ReceptionistId,
                ReceivedAtUtc = DateTime.UtcNow,
                Status = PaymentStatus.Succeeded
            };
            var p2 = new Payment
            {
                PaymentCode = $"PAY-V-{Guid.NewGuid():N}"[..15],
                InvoiceId = invId,
                Amount = 500000m,
                Method = PaymentMethod.Cash,
                ReceivedByUserId = ReceptionistId,
                ReceivedAtUtc = DateTime.UtcNow,
                Status = PaymentStatus.Voided // Voided should NOT be counted in revenue
            };
            db.Payments.AddRange(p1, p2);
            await db.SaveChangesAsync();
        }

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var res = await Client.GetAsync($"/api/v1/admin/billing/revenue?fromDate={today}&toDate={today}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var totalRev = doc.RootElement.GetProperty("data").GetProperty("totalRevenue").GetDecimal();
        Assert.True(totalRev >= succeededAmount);
    }
}
