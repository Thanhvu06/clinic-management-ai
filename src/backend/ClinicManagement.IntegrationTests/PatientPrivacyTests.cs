using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class PatientPrivacyTests : IntegrationTestBase
{
    public PatientPrivacyTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_Patient1Appointment_When_Patient2AttemptsToAccess_Then_ReturnsNotFoundOrEmpty()
    {
        // 1. Create a dedicated slot and appointment for Patient 1
        var date = GetFutureWorkingDate(26);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(10, 0, 0), new TimeOnly(10, 30, 0));
        var slotId = slot.Id;

        await AuthenticateAsync("pat1@test.com");
        var createResponse = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slotId,
            Reason = "Khám riêng tư - Patient 1"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var patient1AppointmentId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // 2. Patient 2 authenticates
        await AuthenticateAsync("pat2@test.com");

        // Attempt 1: Direct GET by ID on Patient 1's appointment
        var directAccessResponse = await Client.GetAsync($"/api/v1/appointments/{patient1AppointmentId}");
        Assert.Equal(HttpStatusCode.NotFound, directAccessResponse.StatusCode);

        // Attempt 2: Direct GET history on Patient 1's appointment
        var historyResponse = await Client.GetAsync($"/api/v1/appointments/{patient1AppointmentId}/history");
        Assert.Equal(HttpStatusCode.NotFound, historyResponse.StatusCode);

        // Attempt 3: List appointments for Patient 2 - must NOT contain Patient 1's appointment
        var listResponse = await Client.GetAsync("/api/v1/appointments/my");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain($"\"id\":{patient1AppointmentId},", listJson);
        Assert.DoesNotContain("Khám riêng tư - Patient 1", listJson);

        // Attempt 4: Prescriptions list for Patient 2 - must be isolated
        var prescriptionsResponse = await Client.GetAsync("/api/v1/patients/me/prescriptions");
        Assert.Equal(HttpStatusCode.OK, prescriptionsResponse.StatusCode);
    }

    [Fact]
    public async Task Given_Patient1PackageRegistration_When_Patient2AttemptsToAccess_Then_ReturnsNotFound()
    {
        // 1. Patient 1 registers for a health package
        await AuthenticateAsync("pat1@test.com");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var regResponse = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", new ClinicManagement.Application.HealthPackages.DTOs.CreatePackageRegistrationRequest
        {
            HealthPackageId = PackageEntityId,
            PreferredDate = tomorrow,
            ContactPhone = "0912345678",
            Note = "Bệnh nhân 1 đăng ký riêng tư"
        });
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        var regDoc = JsonDocument.Parse(await regResponse.Content.ReadAsStringAsync());
        var regId = regDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // 2. Patient 2 authenticates
        await AuthenticateAsync("pat2@test.com");

        // Attempt 1: Direct GET by ID
        var getById = await Client.GetAsync($"/api/v1/patient/health-package-registrations/{regId}");
        Assert.Equal(HttpStatusCode.NotFound, getById.StatusCode);

        // Attempt 2: Cancel Patient 1's registration
        var cancelAttempt = await Client.PostAsJsonAsync($"/api/v1/patient/health-package-registrations/{regId}/cancel", new ClinicManagement.Application.HealthPackages.DTOs.CancelPackageRegistrationRequest
        {
            CancellationReason = "Hủy lén của Patient 1"
        });
        Assert.Equal(HttpStatusCode.NotFound, cancelAttempt.StatusCode);

        // Attempt 3: List for Patient 2 must not contain Patient 1's registration
        var listResponse = await Client.GetAsync("/api/v1/patient/health-package-registrations");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain($"\"id\":{regId},", listJson);
        Assert.DoesNotContain("Bệnh nhân 1 đăng ký riêng tư", listJson);
    }

    [Fact]
    public async Task Given_TwoPatients_When_AccessingMeProfile_Then_StrictlyIsolated()
    {
        await AuthenticateAsync("pat1@test.com");
        var p1Res = await Client.GetAsync("/api/v1/patients/me");
        Assert.Equal(HttpStatusCode.OK, p1Res.StatusCode);
        var p1Json = await p1Res.Content.ReadAsStringAsync();
        Assert.Contains("pat1@test.com", p1Json);

        await AuthenticateAsync("pat2@test.com");
        var p2Res = await Client.GetAsync("/api/v1/patients/me");
        Assert.Equal(HttpStatusCode.OK, p2Res.StatusCode);
        var p2Json = await p2Res.Content.ReadAsStringAsync();
        Assert.Contains("pat2@test.com", p2Json);
        Assert.DoesNotContain("pat1@test.com", p2Json);
    }
}
