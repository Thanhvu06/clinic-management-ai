using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AppointmentFlowTests : IntegrationTestBase
{
    public AppointmentFlowTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_ValidRequest_When_PatientBooks_Then_SuccessAndSlotIsBooked()
    {
        await AuthenticateAsync("pat1@test.com");

        var testDate = GetFutureWorkingDate(20);
        var testSlot = await CreateAvailableSlotAsync(DoctorEntityId, testDate, new TimeOnly(10, 0, 0), new TimeOnly(10, 30, 0));
        var testSlotId = testSlot.Id;

        var request = new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = testSlotId,
            Reason = "Đau lưng quá"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/appointments", request);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Status was {response.StatusCode}. Response: {json}");
        Assert.Contains("Pending", json);

        // Extract appointment ID
        var doc = JsonDocument.Parse(json);
        var appointmentId = doc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Check the slot is booked
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();
        var slot = await db.AppointmentSlots.FindAsync(testSlotId);
        Assert.NotNull(slot);
        Assert.True(slot.IsBooked);

        // Test 1: Doctor 1 should be able to get history for this appointment (they own it)
        await AuthenticateAsync("doc@test.com");
        var doc1Response = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}/history");
        Assert.Equal(HttpStatusCode.OK, doc1Response.StatusCode);

        // Test 2: Doctor 2 should NOT be able to get history for this appointment (throws 404/403)
        await AuthenticateAsync("doc2@test.com");
        var doc2Response = await Client.GetAsync($"/api/v1/doctor/appointments/{appointmentId}/history");
        Assert.Equal(HttpStatusCode.NotFound, doc2Response.StatusCode);
    }
}
