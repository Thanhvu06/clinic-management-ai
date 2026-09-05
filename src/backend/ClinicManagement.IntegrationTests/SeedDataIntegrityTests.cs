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

        // 1. Verify Specialties
        var specialties = await db.Specialties.ToListAsync();
        Assert.NotEmpty(specialties);
        Assert.True(specialties.Count >= 7);
        foreach (var s in specialties)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.SpecialtyCode));
            // Ensure no mojibake markers like 'á»…' or 'Ă'
            Assert.DoesNotContain("á»", s.Name);
            Assert.DoesNotContain("Ă", s.Name);
            Assert.DoesNotContain("Â", s.Name);
        }

        // 2. Verify Doctors & Academic Titles
        var doctors = await db.Doctors.Include(d => d.DoctorSpecialties).ToListAsync();
        var users = await db.Users.ToListAsync();
        Assert.NotEmpty(doctors);
        Assert.True(doctors.Count >= 10);

        foreach (var doc in doctors)
        {
            var user = users.First(u => u.Id == doc.UserId);
            Assert.True(doc.IsActive);
            Assert.True(user.IsActive);
            Assert.True(doc.ExperienceYears > 0);
            Assert.False(string.IsNullOrWhiteSpace(doc.AcademicTitle));

            // FullName should NOT duplicate AcademicTitle (e.g., FullName is "Nguyễn Minh Khải", not "BS.CKI Nguyễn Minh Khải")
            Assert.False(user.FullName.StartsWith(doc.AcademicTitle, StringComparison.OrdinalIgnoreCase),
                $"Doctor {user.FullName} should not duplicate AcademicTitle {doc.AcademicTitle}");

            // Each doctor must have at least one primary specialty
            Assert.Contains(doc.DoctorSpecialties, ds => ds.IsPrimary);
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

        // 4. Verify Work Schedules & 2-Shift Slots
        var schedules = await db.DoctorWorkSchedules.ToListAsync();
        Assert.NotEmpty(schedules);

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
    }
}
