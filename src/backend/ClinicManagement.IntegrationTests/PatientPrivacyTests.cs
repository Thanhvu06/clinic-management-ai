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
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
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
}
