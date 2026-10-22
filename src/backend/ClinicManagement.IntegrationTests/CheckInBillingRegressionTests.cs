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

public sealed class CheckInBillingRegressionTests : IntegrationTestBase
{
    private static int _minute;
    public CheckInBillingRegressionTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 4)]
    [InlineData(9, 1)]
    public async Task Checkin_allocates_above_legacy_visits_and_sequence(int? lastNumber, int legacyNumber)
    {
        var department = await CreateDepartmentAsync();
        var first = await CreateAppointmentAsync(department, Patient1EntityId, 8);
        var second = await CreateAppointmentAsync(department, Patient2EntityId, 9);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PatientVisits.Add(new PatientVisit
            {
                VisitCode = $"LEGACY-{Guid.NewGuid():N}", PatientId = Patient1EntityId,
                FacilityId = department.FacilityId, DepartmentId = department.Id,
                VisitDate = first.AppointmentDate, QueueNumber = legacyNumber, CreatedByUserId = ReceptionistId
            });
            db.PatientVisits.Add(new PatientVisit
            {
                VisitCode = $"YESTERDAY-{Guid.NewGuid():N}", PatientId = Patient1EntityId,
                FacilityId = department.FacilityId, DepartmentId = department.Id,
                VisitDate = first.AppointmentDate.AddDays(-1), QueueNumber = 100, CreatedByUserId = ReceptionistId
            });
            if (lastNumber.HasValue)
                db.DailyQueueSequences.Add(new DailyQueueSequence
                {
                    FacilityId = department.FacilityId, DepartmentId = department.Id,
                    Date = first.AppointmentDate, LastNumber = lastNumber.Value
                });
            await db.SaveChangesAsync();
        }

        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        var expected = Math.Max(lastNumber ?? 0, legacyNumber) + 1;
        foreach (var appointment in new[] { first, second })
        {
            var response = await reception.PostAsJsonAsync("/api/v1/patient-visits/check-in-appointment", new
            {
                appointmentId = appointment.Id, departmentId = department.Id
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expected++, json.RootElement.GetProperty("data").GetProperty("queueNumber").GetInt32());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Appointment_order_links_only_active_visit_and_is_billed(bool cancelVisit)
    {
        var department = await CreateDepartmentAsync();
        var appointment = await CreateAppointmentAsync(department, Patient1EntityId, 10);
        var reception = await CreateAuthenticatedClientAsync("rec@test.com");
        var checkin = await reception.PostAsJsonAsync("/api/v1/patient-visits/check-in-appointment", new
        {
            appointmentId = appointment.Id, departmentId = department.Id
        });
        Assert.Equal(HttpStatusCode.OK, checkin.StatusCode);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var start = await doctor.PostAsync($"/api/v1/doctor/appointments/{appointment.Id}/start-consultation", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        long visitId;
        long serviceId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.SingleAsync(v => v.AppointmentId == appointment.Id);
            visitId = visit.Id;
            if (cancelVisit) visit.Status = VisitStatus.Cancelled;
            var service = new DiagnosticService
            {
                Code = $"REG-{Guid.NewGuid():N}"[..16], Name = "Xét nghiệm kiểm tra hóa đơn", Price = 75000m
            };
            db.DiagnosticServices.Add(service);
            await db.SaveChangesAsync();
            serviceId = service.Id;
        }

        var response = await doctor.PostAsJsonAsync($"/api/v1/doctor/appointments/{appointment.Id}/diagnostic-orders", new
        {
            clinicalIndication = "Kiểm tra sau tiếp nhận", serviceIds = new[] { serviceId }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var orderJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orderId = orderJson.RootElement.GetProperty("data").GetProperty("id").GetInt64();
        long itemId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await db.DiagnosticOrders.Include(o => o.Items).SingleAsync(o => o.Id == orderId);
            Assert.Equal(cancelVisit ? (long?)null : visitId, order.PatientVisitId);
            itemId = Assert.Single(order.Items).Id;
        }
        if (cancelVisit) return;

        var invoice = await reception.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new { patientVisitId = visitId });
        Assert.Equal(HttpStatusCode.Created, invoice.StatusCode);
        using var invoiceJson = JsonDocument.Parse(await invoice.Content.ReadAsStringAsync());
        var line = Assert.Single(invoiceJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray(),
            i => i.GetProperty("referenceType").GetString() == "DiagnosticItem" && i.GetProperty("referenceId").GetInt64() == itemId);
        Assert.Equal(75000m, line.GetProperty("lineTotal").GetDecimal());
    }

    private async Task<Department> CreateDepartmentAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = new Department
        {
            FacilityId = (await db.Facilities.FirstAsync(f => f.IsActive)).Id, SpecialtyId = SpecialtyEntityId,
            Code = $"REG-{Guid.NewGuid():N}"[..16], Name = "Khoa kiểm tra tiếp nhận", IsActive = true
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department;
    }

    private async Task<Appointment> CreateAppointmentAsync(Department department, long patientId, int hour)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
        var start = new TimeOnly(hour, 0).AddMinutes(Interlocked.Increment(ref _minute));
        var appointment = new Appointment
        {
            AppointmentCode = $"REG-{Guid.NewGuid():N}"[..18], PatientId = patientId, DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId, FacilityId = department.FacilityId, AppointmentDate = today,
            StartTime = start, EndTime = start.AddMinutes(1), Status = AppointmentStatus.Confirmed,
            Reason = "Khám kiểm tra", AppointmentSlot = new AppointmentSlot
            {
                DoctorId = DoctorEntityId, SlotDate = today, StartTime = start,
                EndTime = start.AddMinutes(1), IsBooked = true
            }
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }
}
