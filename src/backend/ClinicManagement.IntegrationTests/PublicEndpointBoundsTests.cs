using System.Globalization;
using System.Net;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class PublicEndpointBoundsTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            return document.RootElement.GetProperty("data").Clone();
        }
    }

    [Fact]
    public async Task OnlyAdminCanIncludeInactiveFacilities()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = "PUBLIC-" + Guid.NewGuid().ToString("N");
        var active = new Facility { Code = code + "-A", Name = "Public active fixture", IsActive = true };
        var inactive = new Facility { Code = code + "-I", Name = "Public inactive fixture", IsActive = false };
        db.Facilities.AddRange(active, inactive);
        await db.SaveChangesAsync();

        using var anonymous = Factory.CreateClient();
        var publicData = await Data(await anonymous.GetAsync("/api/v1/facilities?includeInactive=true"));
        Assert.Contains(publicData.EnumerateArray(), item => item.GetProperty("id").GetInt64() == active.Id);
        Assert.DoesNotContain(publicData.EnumerateArray(), item => item.GetProperty("id").GetInt64() == inactive.Id);

        await AuthenticateAsync("rec@test.com");
        var staffData = await Data(await Client.GetAsync("/api/v1/facilities?includeInactive=true"));
        Assert.DoesNotContain(staffData.EnumerateArray(), item => item.GetProperty("id").GetInt64() == inactive.Id);

        await AuthenticateAsync("admin@test.com");
        var adminData = await Data(await Client.GetAsync("/api/v1/facilities?includeInactive=true"));
        Assert.Contains(adminData.EnumerateArray(), item => item.GetProperty("id").GetInt64() == inactive.Id);
        var activeOnly = await Data(await Client.GetAsync("/api/v1/facilities?includeInactive=false"));
        Assert.DoesNotContain(activeOnly.EnumerateArray(), item => item.GetProperty("id").GetInt64() == inactive.Id);
    }

    private sealed record Fixture(long SpecialtyId, long DoctorId, long OutsideDoctorId, DateOnly FromDate);

    private async Task<Fixture> CreateAvailabilityFixture()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var specialty = new Specialty { SpecialtyCode = "BOUNDS-" + suffix, Name = "Public bounds fixture", IsActive = true };
        db.Specialties.Add(specialty);
        var doctors = new List<Doctor>();
        foreach (var index in new[] { 1, 2 })
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = $"bounds-{index}-{suffix}@test.com",
                Email = $"bounds-{index}-{suffix}@test.com", PhoneNumber = $"bounds-{index}-{suffix}", FullName = $"Bounds doctor {index}", IsActive = true
            };
            db.Users.Add(user);
            var doctor = new Doctor { UserId = user.Id, IsActive = true };
            db.Doctors.Add(doctor);
            doctors.Add(doctor);
        }
        await db.SaveChangesAsync();
        db.DoctorSpecialties.AddRange(doctors.Select(doctor => new DoctorSpecialty
        {
            DoctorId = doctor.Id, SpecialtyId = specialty.Id, IsPrimary = true
        }));

        // Private fixture dates, independent of other test files and the shared seeded doctors.
        var fromDate = new DateOnly(2081, 3, 1);
        while (fromDate.DayOfWeek != DayOfWeek.Tuesday) fromDate = fromDate.AddDays(1);
        foreach (var (doctor, offsets) in new[] { (doctors[0], new[] { -1, 0, 31, 32 }), (doctors[1], new[] { 31 }) })
        {
            foreach (var offset in offsets)
            {
                var date = fromDate.AddDays(offset);
                db.DoctorWorkSchedules.Add(new DoctorWorkSchedule
                {
                    DoctorId = doctor.Id, WorkDate = date, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0), IsActive = true
                });
                db.AppointmentSlots.Add(new AppointmentSlot
                {
                    DoctorId = doctor.Id, SlotDate = date, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30), IsBooked = false
                });
            }
        }
        await db.SaveChangesAsync();
        return new Fixture(specialty.Id, doctors[0].Id, doctors[1].Id, fromDate);
    }

    [Fact]
    public async Task ExtremeAvailableSlotRangeIsSafeAndClampedToFirst32Dates()
    {
        var fixture = await CreateAvailabilityFixture();
        var singleDay = await Data(await Client.GetAsync($"/api/v1/doctors/{fixture.DoctorId}/available-slots?fromDate={fixture.FromDate:yyyy-MM-dd}&toDate={fixture.FromDate:yyyy-MM-dd}"));
        Assert.Single(singleDay.EnumerateArray());
        var data = await Data(await Client.GetAsync($"/api/v1/doctors/{fixture.DoctorId}/available-slots?fromDate=0001-01-01&toDate=9999-12-31"));
        Assert.All(data.EnumerateArray(), slot => Assert.InRange(
            DateOnly.Parse(slot.GetProperty("slotDate").GetString()!, CultureInfo.InvariantCulture), DateOnly.MinValue, DateOnly.MinValue.AddDays(31)));
        // All dates in the clamped year-one window are past; future fixture slots must be excluded.
        Assert.Empty(data.EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AvailableSlotsIncludeDay31ButExcludeDay32AfterNormalizingRange(bool reversed)
    {
        var fixture = await CreateAvailabilityFixture();
        var from = reversed ? DateOnly.MaxValue : fixture.FromDate;
        var to = reversed ? fixture.FromDate : DateOnly.MaxValue;
        var data = await Data(await Client.GetAsync($"/api/v1/doctors/{fixture.DoctorId}/available-slots?fromDate={from:yyyy-MM-dd}&toDate={to:yyyy-MM-dd}"));
        var dates = data.EnumerateArray().Select(slot => DateOnly.Parse(slot.GetProperty("slotDate").GetString()!, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(new[] { fixture.FromDate, fixture.FromDate.AddDays(31) }, dates);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100000)]
    [InlineData(1)]
    [InlineData(31)]
    public async Task RecommendedDoctorDaysAreClampedToOneThrough31(int days)
    {
        var fixture = await CreateAvailabilityFixture();
        var data = await Data(await Client.GetAsync($"/api/v1/specialties/{fixture.SpecialtyId}/recommended-doctors?fromDate={fixture.FromDate:yyyy-MM-dd}&days={days}"));
        var first = Assert.Single(data.EnumerateArray(), doctor => doctor.GetProperty("doctorId").GetInt64() == fixture.DoctorId);
        var earliest = DateTimeOffset.Parse(first.GetProperty("earliestAvailableSlot").GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal(fixture.FromDate, DateOnly.FromDateTime(earliest.DateTime));
        var outside = Assert.Single(data.EnumerateArray(), doctor => doctor.GetProperty("doctorId").GetInt64() == fixture.OutsideDoctorId);
        Assert.Equal(JsonValueKind.Null, outside.GetProperty("earliestAvailableSlot").ValueKind);
    }
}
