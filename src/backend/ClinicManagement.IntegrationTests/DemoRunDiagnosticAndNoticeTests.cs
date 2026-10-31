using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class DemoRunDiagnosticAndNoticeTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static int _sequence = 9000;

    [Theory]
    [InlineData(false, "Viêm họng", "Ho", "Viêm họng")]
    [InlineData(true, "Viêm họng", "Ho", "Viêm họng")]
    [InlineData(false, null, "Ho", "Ho")]
    [InlineData(true, null, "Ho", "Ho")]
    [InlineData(false, null, null, "")]
    [InlineData(true, null, null, "")]
    public async Task Blank_indication_uses_encounter_diagnosis_then_reason_then_empty(bool visitRoute, string? diagnosis, string? reason, string expected)
    {
        var (order, _) = await CreateOrderAsync(visitRoute, diagnosis, reason);
        Assert.Equal(expected, order.GetProperty("clinicalIndication").GetString());
        var id = order.GetProperty("id").GetInt64();
        await AuthenticateAsync("tech@test.com");
        Assert.Equal(expected, (await DataAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{id}"))).GetProperty("clinicalIndication").GetString());
        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DiagnosticOrders.AsNoTracking().SingleAsync(order => order.Id == id);
        Assert.Equal(expected, stored.ClinicalIndication);
    }

    [Fact]
    public async Task Diagnostic_flow_keeps_doctor_KPI_active_and_audit_descriptions_readable()
    {
        var (order, visit) = await CreateOrderAsync(true, "Viêm họng", "Ho");
        var orderId = order.GetProperty("id").GetInt64();
        await AssertActiveKpiAsync(visit);
        await AuthenticateAsync("tech@test.com");
        order = await DataAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{orderId}/start", new { rowVersion = order.GetProperty("rowVersion").GetString() }));
        var item = order.GetProperty("items")[0];
        var itemId = item.GetProperty("id").GetInt64();
        foreach (var step in new[] { "collect", "receive" })
        {
            order = await DataAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{orderId}/items/{itemId}/specimen/{step}",
                new { rowVersion = item.GetProperty("rowVersion").GetString() }));
            item = order.GetProperty("items")[0];
        }
        order = await DataAsync(await Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{orderId}/items/{itemId}/result",
            new { resultText = "Bình thường", rowVersion = item.GetProperty("rowVersion").GetString() }));
        order = await DataAsync(await Client.PostAsJsonAsync($"/api/v1/diagnostics/orders/{orderId}/complete", new { rowVersion = order.GetProperty("rowVersion").GetString() }));
        Assert.Equal("Completed", order.GetProperty("status").GetString());
        await AssertActiveKpiAsync(visit);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var specimenLogs = await db.SystemAuditLogs.AsNoTracking().Where(log => log.EntityName == "DiagnosticOrderItem" && log.EntityId == itemId.ToString() && log.Action.StartsWith("Specimen")).ToListAsync();
        Assert.Equal(2, specimenLogs.Count);
        Assert.Contains(specimenLogs, log => log.Description.StartsWith($"Lấy mẫu S{itemId:D7}-1 của phiếu #"));
        Assert.Contains(specimenLogs, log => log.Description.StartsWith($"Nhận mẫu S{itemId:D7}-1 của phiếu #"));
        Assert.All(specimenLogs, log => Assert.DoesNotContain("lần lấy lại", log.Description));
        var logs = await db.SystemAuditLogs.AsNoTracking().Where(log => log.EntityName == "DiagnosticOrder" && log.EntityId == orderId.ToString()).ToListAsync();
        Assert.All(logs, log => Assert.DoesNotContain("VerifiedByRole", log.Description));
        Assert.Contains(logs, log => log.Action == "DiagnosticOrderCompleted" && log.Description == $"Phiếu chỉ định #{order.GetProperty("orderCode").GetString()} đã hoàn tất.");
    }

    [Fact]
    public async Task Appointment_notifications_distinguish_submission_and_confirmation()
    {
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, GetFutureWorkingDate(120), new TimeOnly(8, 0), new TimeOnly(8, 30));
        await AuthenticateAsync("pat1@test.com");
        var appointment = await DataAsync(await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            doctorId = DoctorEntityId, specialtyId = SpecialtyEntityId, appointmentSlotId = slot.Id, reason = "Ho khan kéo dài"
        }));
        var id = appointment.GetProperty("id").GetInt64();
        await AuthenticateAsync("rec@test.com");
        (await Client.PostAsync($"/api/v1/reception/appointments/{id}/confirm", null)).EnsureSuccessStatusCode();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = await db.Notifications.AsNoTracking().Where(notification => notification.UserId == Patient1Id && notification.RelatedEntityId == id.ToString()).ToListAsync();
        Assert.Contains(notifications, notification => notification.Message == $"Lịch khám #{appointment.GetProperty("appointmentCode").GetString()} ngày {slot.SlotDate:dd/MM/yyyy} lúc {slot.StartTime:HH\\:mm} đã được gửi, đang chờ phòng khám xác nhận.");
        Assert.Contains(notifications, notification => notification.Message.EndsWith("đã được xác nhận."));
        Assert.All(notifications, notification => Assert.DoesNotContain("tiếp nhận", notification.Message));
    }

    private async Task<(JsonElement Order, PatientVisit Visit)> CreateOrderAsync(bool visitRoute, string? diagnosis, string? reason)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var assignment = await db.StaffFacilityAssignments.FirstAsync(item => item.UserId == TechnicianId && item.IsActive);
        var appointment = new Appointment { AppointmentCode = $"DEMO2-{Guid.NewGuid():N}", PatientId = Patient1EntityId,
            DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId, FacilityId = assignment.FacilityId,
            AppointmentDate = clock.VietnamToday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30), Status = AppointmentStatus.InConsultation, Reason = reason };
        var visit = new PatientVisit { VisitCode = $"DEMO2-{Guid.NewGuid():N}", PatientId = Patient1EntityId, Appointment = appointment,
            AssignedDoctorId = DoctorEntityId, FacilityId = assignment.FacilityId, DepartmentId = assignment.DepartmentId!.Value,
            VisitDate = clock.VietnamToday, QueueNumber = Interlocked.Increment(ref _sequence), Status = VisitStatus.InConsultation, ChiefComplaint = reason, CreatedByUserId = ReceptionistId };
        var service = new DiagnosticService { Code = $"DEMO2-{Guid.NewGuid():N}", Name = "Xét nghiệm máu", Category = DiagnosticCategory.Laboratory, SpecimenType = "Máu", IsActive = true };
        db.PatientVisits.Add(visit);
        db.DiagnosticServices.Add(service);
        await db.SaveChangesAsync();
        if (diagnosis != null)
        {
            db.VisitSummaries.Add(new VisitSummary { PatientVisitId = visit.Id, AppointmentId = appointment.Id, DoctorId = DoctorEntityId, Diagnosis = diagnosis });
            await db.SaveChangesAsync();
        }
        await AuthenticateAsync("doc@test.com");
        var route = visitRoute ? $"/api/v1/doctor/visits/{visit.Id}/diagnostic-orders" : $"/api/v1/doctor/appointments/{appointment.Id}/diagnostic-orders";
        return (await DataAsync(await Client.PostAsJsonAsync(route, new { clinicalIndication = "", serviceIds = new[] { service.Id } })), visit);
    }

    private async Task AssertActiveKpiAsync(PatientVisit visit)
    {
        await AuthenticateAsync("doc@test.com");
        var dashboard = await DataAsync(await Client.GetAsync($"/api/v1/doctor/dashboard?date={visit.VisitDate:yyyy-MM-dd}"));
        Assert.True(dashboard.GetProperty("inConsultationCount").GetInt32() >= 1);
        var queueItem = Assert.Single(dashboard.GetProperty("queue").EnumerateArray(), item => item.GetProperty("patientVisitId").GetInt64() == visit.Id);
        Assert.Contains(queueItem.GetProperty("status").GetString(), new[] { "WaitingForDiagnostics", "InDiagnostics", "ResultsReady" });
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
