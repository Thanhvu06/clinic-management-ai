using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Doctors.DTOs;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class SlotContractTests : IntegrationTestBase
{
    public SlotContractTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public void AvailableSlotDto_ShouldHaveCanonicalSlotIdProperty_AndNotDuplicateId()
    {
        // Assert that AvailableSlotDto has SlotId
        var slotIdProp = typeof(AvailableSlotDto).GetProperty("SlotId", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(slotIdProp);
        Assert.Equal(typeof(long), slotIdProp.PropertyType);

        // Assert that AvailableSlotDto does NOT declare property named "Id"
        var idProp = typeof(AvailableSlotDto).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(idProp);

        // Test serialization
        var slotDto = new AvailableSlotDto
        {
            SlotId = 12345,
            DoctorId = 1,
            SlotDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 30)
        };

        var json = JsonSerializer.Serialize(slotDto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("slotId", out var slotIdElem));
        Assert.Equal(12345, slotIdElem.GetInt64());
        Assert.False(root.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Given_DoctorSlotsEndpoint_When_Queried_Then_ReturnsCanonicalSlotId()
    {
        var response = await Client.GetAsync($"/api/v1/doctors/{DoctorEntityId}/available-slots");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        // The response data is an array of slots
        if (data.GetArrayLength() > 0)
        {
            var firstSlot = data[0];
            Assert.True(firstSlot.TryGetProperty("slotId", out var slotIdProp));
            Assert.True(slotIdProp.GetInt64() > 0);
            Assert.False(firstSlot.TryGetProperty("id", out _));
        }
    }
}
