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

        var request = new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = SlotEntityId,
            Reason = "Đau lưng quá trời luôn"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/appointments", request);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("Pending", json);

        // Check the slot is booked
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicManagement.Infrastructure.Persistence.AppDbContext>();
        var slot = await db.AppointmentSlots.FindAsync(SlotEntityId);
        Assert.True(slot.IsBooked);
    }
}
