using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class DemoSpecialtyBookableSeedTests
{
    [Fact]
    public async Task Every_active_specialty_has_a_doctor_with_slots_and_an_eligible_facility_without_deleting_anything()
    {
        using var seeded = new FixedSeedWebApplicationFactory(new DateTime(2026, 11, 26, 3, 0, 0, DateTimeKind.Utc));
        using var scope = seeded.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var assignmentsBefore = await db.StaffFacilityAssignments.CountAsync();
        var appointmentsBefore = await db.Appointments.CountAsync();

        await DevelopmentSpecialtyCoverageSeed.EnsureAsync(db, clock);
        Assert.Equal(0, await DevelopmentSpecialtyCoverageSeed.EnsureAsync(db, clock));
        Assert.Equal(assignmentsBefore, await db.StaffFacilityAssignments.CountAsync());
        Assert.Equal(appointmentsBefore, await db.Appointments.CountAsync());

        var today = clock.VietnamToday;
        var availability = services.GetRequiredService<IAppointmentAvailabilityPolicy>();
        foreach (var specialty in await db.Specialties.Where(s => s.IsActive && s.DoctorSpecialties.Any()).ToListAsync())
        {
            var doctorIds = (await availability.GetAvailableSlotsAsync(new BatchSlotAvailabilityRequest
            {
                SpecialtyId = specialty.Id, FromDate = today.AddDays(1), ToDate = today.AddDays(DevelopmentSpecialtyCoverageSeed.CoverageDays - 1)
            })).Select(slot => slot.DoctorId).Distinct().ToList();
            var bookable = false;
            foreach (var doctorId in doctorIds)
                bookable |= (await AppointmentFacilityResolver.GetEligibleAsync(db, doctorId, specialty.Id)).Count > 0;
            Assert.True(bookable, $"{specialty.Name} has no doctor with both a free slot and an eligible facility");
        }
    }
}
