using System;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Authentication;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class SeedDataIntegrityTests : IntegrationTestBase
{
    public SeedDataIntegrityTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task SeedAsync_PopulatesConsistentAndCleanData()
    {
        using var scope = Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        // Ensure roles exist first
        await RoleSeeder.SeedRolesAsync(sp);

        // Run the development data seeder
        await DevelopmentDataSeeder.SeedAsync(sp);

        // 1. Verify 11 Standard Specialties (SP01 to SP11)
        var specialties = await db.Specialties.ToListAsync();
        Assert.NotEmpty(specialties);
        var seededSpecialties = specialties.Where(s => s.SpecialtyCode.StartsWith("SP") && s.SpecialtyCode.Length == 4).ToList();
        Assert.Equal(11, seededSpecialties.Count);
        for (int sIdx = 1; sIdx <= 11; sIdx++)
        {
            var code = $"SP{sIdx:D2}";
            Assert.Contains(specialties, s => s.SpecialtyCode == code);
        }
        foreach (var s in seededSpecialties)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.SpecialtyCode));
            Assert.True(s.IsActive);
            // Ensure no mojibake markers like 'á»…' or 'Ă'
            Assert.DoesNotContain("á»", s.Name);
            Assert.DoesNotContain("Ă", s.Name);
            Assert.DoesNotContain("Â", s.Name);
        }

        // 2. Verify Doctors, Academic Titles & Exact Specialty Bindings
        var doctors = await db.Doctors.Include(d => d.DoctorSpecialties).ToListAsync();
        var users = await db.Users.ToListAsync();
        Assert.NotEmpty(doctors);

        var expectedDoctorMappings = new Dictionary<string, (string PrimaryCode, string? SecondaryCode)>
        {
            ["doctor@cliniccare.local"] = ("SP01", "SP06"),
            ["bacsi.02@cliniccare.local"] = ("SP03", null),
            ["bacsi.03@cliniccare.local"] = ("SP07", null),
            ["bacsi.04@cliniccare.local"] = ("SP05", null),
            ["bacsi.05@cliniccare.local"] = ("SP04", null),
            ["bacsi.06@cliniccare.local"] = ("SP08", null),
            ["bacsi.07@cliniccare.local"] = ("SP09", null),
            ["bacsi.08@cliniccare.local"] = ("SP10", null),
            ["bacsi.09@cliniccare.local"] = ("SP02", null),
            ["bacsi.10@cliniccare.local"] = ("SP11", null)
        };

        var demoDoctors = doctors.Where(d => expectedDoctorMappings.ContainsKey(users.First(u => u.Id == d.UserId).Email ?? "")).ToList();
        Assert.Equal(10, demoDoctors.Count);

        var specLookup = specialties.ToDictionary(s => s.Id, s => s.SpecialtyCode);

        foreach (var doc in demoDoctors)
        {
            var user = users.First(u => u.Id == doc.UserId);
            Assert.True(doc.IsActive);
            Assert.True(user.IsActive);
            Assert.True(doc.ExperienceYears > 0);
            Assert.False(string.IsNullOrWhiteSpace(doc.AcademicTitle));

            // FullName should NOT duplicate AcademicTitle
            Assert.False(user.FullName.StartsWith(doc.AcademicTitle, StringComparison.OrdinalIgnoreCase),
                $"Doctor {user.FullName} should not duplicate AcademicTitle {doc.AcademicTitle}");

            // Each doctor must have exactly one primary specialty
            var primarySpecialties = doc.DoctorSpecialties.Where(ds => ds.IsPrimary).ToList();
            Assert.Single(primarySpecialties);

            if (expectedDoctorMappings.TryGetValue(user.Email ?? "", out var expected))
            {
                var primaryCode = specLookup[primarySpecialties[0].SpecialtyId];
                Assert.Equal(expected.PrimaryCode, primaryCode);

                if (expected.SecondaryCode != null)
                {
                    var secondary = doc.DoctorSpecialties.FirstOrDefault(ds => !ds.IsPrimary);
                    Assert.NotNull(secondary);
                    var secondaryCode = specLookup[secondary.SpecialtyId];
                    Assert.Equal(expected.SecondaryCode, secondaryCode);
                }
            }
        }

        // 3. Verify Clinic Locations
        var locations = await db.ClinicLocations.Where(l => l.IsActive).ToListAsync();
        Assert.True(locations.Count >= 3);
        foreach (var loc in locations)
        {
            Assert.False(string.IsNullOrWhiteSpace(loc.Code));
            Assert.False(string.IsNullOrWhiteSpace(loc.Name));
            Assert.False(string.IsNullOrWhiteSpace(loc.Address));
            Assert.False(string.IsNullOrWhiteSpace(loc.City));
            Assert.False(string.IsNullOrWhiteSpace(loc.Phone));
            Assert.False(string.IsNullOrWhiteSpace(loc.OpeningHours));
        }

        // 4. Verify Work Schedules & 2-Shift Slots (No Sundays, 14 slots/day)
        var schedules = await db.DoctorWorkSchedules.ToListAsync();
        // None on Sunday among standard clinic work schedules (08:00-11:30 and 13:30-17:00)
        var standardSchedules = schedules.Where(s => 
            (s.StartTime == new TimeOnly(8, 0) && s.EndTime == new TimeOnly(11, 30)) ||
            (s.StartTime == new TimeOnly(13, 30) && s.EndTime == new TimeOnly(17, 0))).ToList();
        Assert.NotEmpty(standardSchedules);
        Assert.DoesNotContain(standardSchedules, s => s.WorkDate.DayOfWeek == DayOfWeek.Sunday);

        var schedulesByDocAndDay = schedules.GroupBy(s => new { s.DoctorId, s.WorkDate });
        var twoShiftDays = schedulesByDocAndDay.Where(g => g.Count() >= 2).ToList();
        Assert.NotEmpty(twoShiftDays);

        var aTwoShiftDayGroup = twoShiftDays.First();
        var aTwoShiftDay = aTwoShiftDayGroup.ToList();
        Assert.Contains(aTwoShiftDay, s => s.StartTime == new TimeOnly(8, 0) && s.EndTime == new TimeOnly(11, 30));
        Assert.Contains(aTwoShiftDay, s => s.StartTime == new TimeOnly(13, 30) && s.EndTime == new TimeOnly(17, 0));

        // Verify slots
        var slots = await db.AppointmentSlots.Where(s => s.DoctorId == aTwoShiftDayGroup.Key.DoctorId && s.SlotDate == aTwoShiftDayGroup.Key.WorkDate).ToListAsync();
        Assert.Equal(14, slots.Count); // 7 morning + 7 afternoon
        Assert.DoesNotContain(slots, s => s.SlotDate.DayOfWeek == DayOfWeek.Sunday);

        // 5. Verify Health Packages & Registrations
        var packages = await db.HealthPackages.ToListAsync();
        Assert.True(packages.Count >= 6);
        foreach (var pkg in packages)
        {
            Assert.False(string.IsNullOrWhiteSpace(pkg.Code));
            Assert.False(string.IsNullOrWhiteSpace(pkg.Name));
            Assert.True(pkg.Price > 0);
            Assert.False(string.IsNullOrWhiteSpace(pkg.IncludedServicesJson));
            Assert.StartsWith("[", pkg.IncludedServicesJson);
        }

        var registrations = await db.HealthPackageRegistrations.ToListAsync();
        Assert.NotEmpty(registrations);
        Assert.Contains(registrations, r => r.Status == HealthPackageRegistrationStatus.Pending);
        Assert.Contains(registrations, r => r.Status == HealthPackageRegistrationStatus.Confirmed);
        Assert.Contains(registrations, r => r.Status == HealthPackageRegistrationStatus.Cancelled);

        // 6. Verify Patient Demographics
        var patients = await db.Patients.ToListAsync();
        Assert.True(patients.Count >= 15);
        var maleCount = patients.Count(p => p.Gender == Gender.Male);
        var femaleCount = patients.Count(p => p.Gender == Gender.Female);
        Assert.True(maleCount > 0, "Should have male patients");
        Assert.True(femaleCount > 0, "Should have female patients");

        var distinctBirthYears = patients.Select(p => p.DateOfBirth?.Year).Where(y => y.HasValue).Distinct().Count();
        Assert.True(distinctBirthYears >= 5, "Should have diverse patient birth years");

        // 7. Verify Doctor Appointment Slot and Specialty Integrity
        var appointments = await db.Appointments
            .Include(a => a.AppointmentSlot)
            .Include(a => a.Doctor)
                .ThenInclude(d => d.DoctorSpecialties)
            .ToListAsync();

        Assert.NotEmpty(appointments);
        foreach (var apt in appointments)
        {
            Assert.NotNull(apt.AppointmentSlot);
            Assert.Equal(apt.DoctorId, apt.AppointmentSlot.DoctorId);
            Assert.Contains(apt.Doctor.DoctorSpecialties, ds => ds.SpecialtyId == apt.SpecialtyId);
        }

        // 8. Verify Idempotency of SeedAsync (re-running seed should not duplicate or error)
        var specCountBefore = await db.Specialties.CountAsync();
        var docCountBefore = await db.Doctors.CountAsync();
        var slotCountBefore = await db.AppointmentSlots.CountAsync();

        await DevelopmentDataSeeder.SeedAsync(sp);

        var specCountAfter = await db.Specialties.CountAsync();
        var docCountAfter = await db.Doctors.CountAsync();
        var slotCountAfter = await db.AppointmentSlots.CountAsync();

        Assert.Equal(specCountBefore, specCountAfter);
        Assert.Equal(docCountBefore, docCountAfter);
        Assert.Equal(slotCountBefore, slotCountAfter);
    }
}
