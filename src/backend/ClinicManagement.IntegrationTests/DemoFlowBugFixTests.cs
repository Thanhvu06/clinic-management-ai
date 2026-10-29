using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class DemoFlowBugFixTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private async Task<PatientVisit> CreateVisitAsync(bool linkedAppointment = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await db.Departments.FirstAsync(d => d.IsActive);
        Appointment? appointment = null;
        if (linkedAppointment)
        {
            appointment = new Appointment
            {
                AppointmentCode = $"DEMO-{Guid.NewGuid():N}", PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId,
                FacilityId = department.FacilityId, Status = AppointmentStatus.InConsultation,
                AppointmentDate = new DateOnly(2026, 10, 26), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30), Reason = "Kiểm tra đau ngực"
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
        }
        var visit = new PatientVisit
        {
            VisitCode = $"DEMO-{Guid.NewGuid():N}", PatientId = Patient1EntityId,
            AppointmentId = appointment?.Id, AssignedDoctorId = DoctorEntityId,
            FacilityId = department.FacilityId, DepartmentId = department.Id,
            Status = VisitStatus.WaitingForDiagnostics, ChiefComplaint = "Kiểm tra đau ngực",
            VisitDate = new DateOnly(2026, 10, 26), QueueNumber = TestQueueNumbers.Next()
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveDraftWhileWaitingForDiagnostics_PersistsWithoutChangingStatus(bool prescription)
    {
        var visit = await CreateVisitAsync();
        await AuthenticateAsync("doc@test.com");
        var response = prescription
            ? await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visit.Id}/prescription-draft", new
            {
                notes = "Chờ kết quả", items = new[] { new { medicineId = MedicineEntityId, quantity = 10, dosage = "1 viên", frequency = "Ngày 2 lần", durationDays = 5 } }
            })
            : await Client.PutAsJsonAsync($"/api/v1/doctor/visits/{visit.Id}/encounter", new { diagnosis = "Theo dõi viêm phổi", summary = "Chờ kết quả" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(VisitStatus.WaitingForDiagnostics, (await db.PatientVisits.FindAsync(visit.Id))!.Status);
        if (prescription)
        {
            var draft = await db.Prescriptions.Include(p => p.Items).SingleAsync(p => p.PatientVisitId == visit.Id);
            Assert.Equal(PrescriptionStatus.Draft, draft.Status);
            Assert.Equal(10, Assert.Single(draft.Items).Quantity);
        }
        else
            Assert.Equal("Theo dõi viêm phổi", (await db.VisitSummaries.SingleAsync(s => s.PatientVisitId == visit.Id)).Diagnosis);
    }

    [Theory]
    [InlineData(false, "active")]
    [InlineData(true, "active")]
    [InlineData(false, "cancelled-order")]
    [InlineData(true, "cancelled-order")]
    [InlineData(false, "cancelled-item")]
    [InlineData(true, "cancelled-item")]
    public async Task CreateOrder_RejectsDuplicateActiveService_AndAllowsOtherOrCancelledService(bool appointmentRoute, string priorState)
    {
        var visit = await CreateVisitAsync(linkedAppointment: true);
        await AuthenticateAsync("doc@test.com");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var services = await db.DiagnosticServices.Where(s => s.IsActive).OrderBy(s => s.Id).Take(2).ToListAsync();
        Assert.Equal(2, services.Count);
        var url = appointmentRoute ? $"/api/v1/doctor/appointments/{visit.AppointmentId}/diagnostic-orders"
            : $"/api/v1/doctor/visits/{visit.Id}/diagnostic-orders";
        var first = await Client.PostAsJsonAsync(url, new { serviceIds = new[] { services[0].Id }, clinicalIndication = "Kiểm tra đau ngực" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var orderId = body.RootElement.GetProperty("data").GetProperty("id").GetInt64();
        var order = await db.DiagnosticOrders.Include(o => o.Items).SingleAsync(o => o.Id == orderId);
        if (priorState == "cancelled-order") order.Status = DiagnosticOrderStatus.Cancelled;
        if (priorState == "cancelled-item") Assert.Single(order.Items).Status = DiagnosticItemStatus.Cancelled;
        await db.SaveChangesAsync();

        var second = await Client.PostAsJsonAsync(url, new { serviceIds = new[] { services[0].Id, services[1].Id }, clinicalIndication = "Kiểm tra lại" });
        if (priorState == "active")
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
            using var error = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            Assert.Equal("DUPLICATE_DIAGNOSTIC_SERVICE", error.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal($"Dịch vụ '{services[0].Name}' đã được chỉ định trong lượt khám này.", error.RootElement.GetProperty("message").GetString());
            Assert.Equal(1, await db.DiagnosticOrders.CountAsync(o => o.PatientVisitId == visit.Id));
            var other = await Client.PostAsJsonAsync(url, new { serviceIds = new[] { services[1].Id }, clinicalIndication = "Dịch vụ khác" });
            Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        }
        else Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateOrder_DetectsDuplicateAcrossVisitAndAppointment(bool firstAppointmentRoute)
    {
        var visit = await CreateVisitAsync(linkedAppointment: true);
        await AuthenticateAsync("doc@test.com");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var serviceId = await db.DiagnosticServices.Where(s => s.IsActive).Select(s => s.Id).FirstAsync();
        var appointmentUrl = $"/api/v1/doctor/appointments/{visit.AppointmentId}/diagnostic-orders";
        var visitUrl = $"/api/v1/doctor/visits/{visit.Id}/diagnostic-orders";
        var request = new { serviceIds = new[] { serviceId }, clinicalIndication = "Kiểm tra đau ngực" };
        var first = await Client.PostAsJsonAsync(firstAppointmentRoute ? appointmentUrl : visitUrl, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var order = await db.DiagnosticOrders.SingleAsync(o => o.PatientVisitId == visit.Id);
        if (firstAppointmentRoute) order.PatientVisitId = null;
        else order.AppointmentId = null;
        await db.SaveChangesAsync();
        var duplicate = await Client.PostAsJsonAsync(firstAppointmentRoute ? visitUrl : appointmentUrl, request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
        Assert.Contains("DUPLICATE_DIAGNOSTIC_SERVICE", await duplicate.Content.ReadAsStringAsync());
    }
}
