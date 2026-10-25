using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class ExpiryApiTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Pharmacy_rejects_expired_prescription_even_before_worker_without_changing_stock(bool swept)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var facility = await db.Facilities.FirstAsync();
        var department = await db.Departments.FirstAsync(d => d.FacilityId == facility.Id);
        var visit = new PatientVisit
        {
            VisitCode = "VIS-EXP-" + Guid.NewGuid().ToString("N"), PatientId = Patient1EntityId,
            FacilityId = facility.Id, DepartmentId = department.Id, AssignedDoctorId = DoctorEntityId,
            VisitDate = clock.VietnamToday.AddDays(-6), QueueNumber = swept ? 9001 : 9002,
            Status = VisitStatus.Completed, CreatedByUserId = ReceptionistId, CheckedInAtUtc = clock.UtcNow.AddDays(-6)
        };
        var prescription = new Prescription
        {
            PatientVisit = visit, PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
            CreatedAt = clock.UtcNow.AddDays(-6), Status = swept ? PrescriptionStatus.Expired : PrescriptionStatus.Issued,
            Items = [new PrescriptionItem { MedicineId = MedicineEntityId, Quantity = 1, Dosage = "1 viên", Frequency = "1 lần/ngày" }]
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        var stock = await db.Medicines.Where(m => m.Id == MedicineEntityId).Select(m => m.StockQuantity).SingleAsync();
        var movements = await db.MedicineStockTransactions.CountAsync();
        var client = await CreateAuthenticatedClientAsync("pharm@test.com");
        foreach (var action in new[] { "dispense", "confirm-purchase" })
        {
            var response = await client.PostAsJsonAsync($"/api/v1/pharmacy/prescriptions/{prescription.Id}/{action}", new { });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Contains("PRESCRIPTION_EXPIRED", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(stock, await db.Medicines.Where(m => m.Id == MedicineEntityId).Select(m => m.StockQuantity).SingleAsync());
        Assert.Equal(movements, await db.MedicineStockTransactions.CountAsync());
    }

    [Fact]
    public async Task No_show_patient_cannot_book_online_but_reception_can_accept_them()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        for (var i = 1; i <= 3; i++)
        {
            var date = clock.VietnamToday.AddDays(-i);
            var slot = new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = date, StartTime = new TimeOnly(15, i), EndTime = new TimeOnly(15, i + 30) };
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = "APT-EXP-" + Guid.NewGuid().ToString("N"), PatientId = Patient2EntityId,
                DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlot = slot,
                AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.NoShow,
                Histories = [new AppointmentHistory { Action = AppointmentHistoryAction.NoShow, NewStatus = AppointmentStatus.NoShow, PerformedByUserId = null, CreatedAt = clock.UtcNow.AddDays(-i) }]
            });
        }
        await db.SaveChangesAsync();
        var patientClient = await CreateAuthenticatedClientAsync("pat2@test.com");
        var eligibility = await patientClient.GetAsync("/api/v1/appointments/online-eligibility");
        Assert.Equal(HttpStatusCode.OK, eligibility.StatusCode);
        using var body = JsonDocument.Parse(await eligibility.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("data").GetProperty("isBlocked").GetBoolean());
        var booking = await patientClient.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId,
            Reason = "Đau đầu kéo dài cần khám nội khoa"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, booking.StatusCode);
        Assert.Contains("ONLINE_BOOKING_BLOCKED", await booking.Content.ReadAsStringAsync());
        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        var department = await db.Departments.FirstAsync();
        var intake = await reception.PostAsJsonAsync("/api/v1/patient-visits/intake", new
        {
            IdempotencyKey = Guid.NewGuid().ToString("N"), ExistingPatientId = Patient2EntityId,
            FacilityId = department.FacilityId, DepartmentId = department.Id,
            ChiefComplaint = "Đăng ký khám tại quầy lễ tân"
        });
        Assert.Equal(HttpStatusCode.OK, intake.StatusCode);
    }
}
