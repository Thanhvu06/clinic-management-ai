using System.Globalization;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class UtcHistorySerializationTests : IntegrationTestBase
{
    public UtcHistorySerializationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Stored_history_audit_and_dispense_times_are_returned_as_UTC_without_shifting_ticks()
    {
        var historyTime = new DateTime(2026, 10, 18, 3, 15, 20, DateTimeKind.Utc);
        var auditTime = historyTime.AddMinutes(1);
        var dispenseTime = historyTime.AddMinutes(2);
        var action = $"UtcSerialization-{Guid.NewGuid():N}";
        long appointmentId;
        long prescriptionId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointment = new Appointment
            {
                AppointmentCode = $"UTC-{Guid.NewGuid():N}"[..18],
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = SlotEntityId,
                AppointmentDate = new DateOnly(2026, 10, 18),
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(9, 30),
                Status = AppointmentStatus.Completed
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            appointmentId = appointment.Id;

            db.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointmentId,
                Action = AppointmentHistoryAction.Completed,
                NewStatus = AppointmentStatus.Completed,
                PerformedByUserId = DoctorId,
                CreatedAt = historyTime
            });
            db.SystemAuditLogs.Add(new SystemAuditLog
            {
                UserId = DoctorId,
                Action = action,
                EntityName = nameof(Appointment),
                EntityId = appointmentId.ToString(CultureInfo.InvariantCulture),
                Description = "UTC serialization regression",
                CreatedAt = auditTime
            });
            var prescription = new Prescription
            {
                AppointmentId = appointmentId,
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                Status = PrescriptionStatus.Dispensed,
                DispensedAt = dispenseTime,
                DispensedByUserId = PharmacistId
            };
            db.Prescriptions.Add(prescription);
            await db.SaveChangesAsync();
            prescriptionId = prescription.Id;
        }

        await AuthenticateAsync("pat1@test.com");
        using var historyResponse = await Client.GetAsync($"/api/v1/appointments/{appointmentId}/history");
        historyResponse.EnsureSuccessStatusCode();
        using var historyJson = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        var history = Assert.Single(historyJson.RootElement.GetProperty("data").EnumerateArray());
        AssertUtcTime(history.GetProperty("createdAt"), historyTime);

        await AuthenticateAsync("admin@test.com");
        using var auditResponse = await Client.GetAsync($"/api/v1/admin/audit-logs?action={action}");
        auditResponse.EnsureSuccessStatusCode();
        using var auditJson = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
        var audit = Assert.Single(auditJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        AssertUtcTime(audit.GetProperty("createdAt"), auditTime);

        using var prescriptionResponse = await Client.GetAsync($"/api/v1/pharmacy/prescriptions/{prescriptionId}");
        prescriptionResponse.EnsureSuccessStatusCode();
        using var prescriptionJson = JsonDocument.Parse(await prescriptionResponse.Content.ReadAsStringAsync());
        AssertUtcTime(prescriptionJson.RootElement.GetProperty("data").GetProperty("dispensedAt"), dispenseTime);
    }

    private static void AssertUtcTime(JsonElement value, DateTime expected)
    {
        Assert.EndsWith("Z", value.GetString());
        var actual = value.GetDateTime();
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
        Assert.Equal(expected, actual);
    }
}
