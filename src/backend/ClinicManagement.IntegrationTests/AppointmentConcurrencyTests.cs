using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AppointmentConcurrencyTests : IntegrationTestBase
{
    public AppointmentConcurrencyTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_SameSlot_When_BookedConcurrentlyOrSequentially_Then_OnlyFirstSucceeds()
    {
        // Setup a fresh slot in database for this specific test with valid doctor schedule
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(14, 0, 0), new TimeOnly(14, 30, 0));
        var testSlotId = slot.Id;

        // Booking request 1 by Patient 1
        await AuthenticateAsync("pat1@test.com");
        var request1 = new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = testSlotId,
            Reason = "Khám sức khỏe tổng quát - Patient 1"
        };
        var response1 = await Client.PostAsJsonAsync("/api/v1/appointments", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Booking request 2 by Patient 2 on the SAME slot
        await AuthenticateAsync("pat2@test.com");
        var request2 = new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = testSlotId,
            Reason = "Khám đau đầu - Patient 2"
        };
        var response2 = await Client.PostAsJsonAsync("/api/v1/appointments", request2);

        // Second booking must fail (UnprocessableEntity / Slot already booked)
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response2.StatusCode);

        var errorBody = await response2.Content.ReadAsStringAsync();
        Assert.Contains("SLOT_ALREADY_BOOKED", errorBody, StringComparison.OrdinalIgnoreCase);
    }
}
