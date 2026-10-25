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

public class DemoPolishRound4ApiTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData(AppointmentChangeRequestStatus.Expired)]
    [InlineData(AppointmentChangeRequestStatus.Approved)]
    [InlineData(AppointmentChangeRequestStatus.Rejected)]
    [InlineData(AppointmentChangeRequestStatus.Withdrawn)]
    public async Task EveryProcessingEndpointRejectsNonPendingRequests(AppointmentChangeRequestStatus status)
    {
        await AuthenticateAsync("rec@test.com");
        foreach (var action in new[] { "approve-reschedule", "approve-cancellation", "reject" })
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var slot = await db.AppointmentSlots.FindAsync(SlotEntityId);
            var appointment = new Appointment
            {
                AppointmentCode = $"APT-{Guid.NewGuid():N}", DoctorId = DoctorEntityId, PatientId = Patient1EntityId,
                SpecialtyId = SpecialtyEntityId, AppointmentSlotId = slot!.Id, AppointmentDate = slot.SlotDate,
                StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.PendingCancellation
            };
            db.Appointments.Add(appointment);
            var request = new AppointmentChangeRequest
            {
                Appointment = appointment, Status = status, RequestedByUserId = Patient1Id,
                RequestType = action == "approve-reschedule" ? AppointmentChangeRequestType.Reschedule : AppointmentChangeRequestType.Cancellation,
                RequestedSlotId = slot.Id, CreatedAt = DateTime.UtcNow.AddDays(-1)
            };
            db.AppointmentChangeRequests.Add(request);
            await db.SaveChangesAsync();
            var response = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{request.Id}/{action}", new { note = "Xác nhận xử lý yêu cầu", reason = "Không thay đổi lịch" });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("CHANGE_REQUEST_ALREADY_PROCESSED", await response.Content.ReadAsStringAsync());
            db.ChangeTracker.Clear();
            Assert.Equal(status, (await db.AppointmentChangeRequests.FindAsync(request.Id))!.Status);
            Assert.Equal(AppointmentStatus.PendingCancellation, (await db.Appointments.FindAsync(appointment.Id))!.Status);
        }
    }

    [Theory]
    [InlineData(15, "PKG01", false)]
    [InlineData(15, "PKG02", false)]
    [InlineData(15, "PKG04", false)]
    [InlineData(15, "PKG05", false)]
    [InlineData(15, "PKG06", false)]
    [InlineData(16, "PKG03", false)]
    [InlineData(15, "PKG03", true)]
    [InlineData(16, "PKG02", true)]
    public async Task RegistrationEnforcesAgeAndUsesEightHexCharacters(int age, string code, bool allowed)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var patient = (await db.Patients.FindAsync(Patient1EntityId))!;
        patient.DateOfBirth = clock.VietnamToday.AddYears(-age);
        var package = await db.HealthPackages.FirstOrDefaultAsync(p => p.Code == code);
        if (package == null)
        {
            package = new HealthPackage { Code = code, Name = "Gói khám theo độ tuổi", Price = 100000, IsActive = true };
            db.HealthPackages.Add(package);
        }
        await db.SaveChangesAsync();
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", new
        {
            healthPackageId = package.Id, preferredDate = clock.VietnamToday.AddDays(2).ToString("yyyy-MM-dd"), contactPhone = "0937184265"
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(allowed == response.IsSuccessStatusCode, body);
        if (allowed)
        {
            using var json = JsonDocument.Parse(body);
            Assert.Matches(@"^REG-\d{6}-[0-9A-F]{8}$", json.RootElement.GetProperty("data").GetProperty("registrationCode").GetString()!);
        }
        else Assert.Contains("PACKAGE_AGE_MISMATCH", body);
    }

    [Fact]
    public async Task CancellingPackageInvoiceCancelsItsRegistrationAndUsesEightHexCharacters()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registration = new HealthPackageRegistration
        {
            RegistrationCode = $"REG-{Guid.NewGuid():N}", PatientId = Patient1EntityId, HealthPackageId = PackageEntityId,
            PreferredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), ContactPhone = "0901234567", Status = HealthPackageRegistrationStatus.Confirmed
        };
        db.HealthPackageRegistrations.Add(registration);
        await db.SaveChangesAsync();
        await AuthenticateAsync("rec@test.com");
        var created = await Client.PostAsJsonAsync("/api/v1/reception/billing/invoices/health-package", new { healthPackageRegistrationId = registration.Id });
        created.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Matches(@"^INV-\d{6}-[0-9A-F]{8}$", data.GetProperty("invoiceCode").GetString()!);
        var cancelled = await Client.PatchAsJsonAsync($"/api/v1/reception/billing/invoices/{data.GetProperty("id").GetInt64()}/cancel", new { reason = "Người bệnh hủy gói khám" });
        cancelled.EnsureSuccessStatusCode();
        db.ChangeTracker.Clear();
        Assert.Equal(HealthPackageRegistrationStatus.Cancelled, (await db.HealthPackageRegistrations.FindAsync(registration.Id))!.Status);
    }
}
