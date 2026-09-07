using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public abstract class IntegrationTestBase : IClassFixture<CustomWebApplicationFactory>
{
    protected readonly CustomWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    public static Guid AdminId { get; private set; }
    public static Guid DoctorId { get; private set; }
    public static Guid Patient1Id { get; private set; }
    public static Guid Patient2Id { get; private set; }
    public static Guid ReceptionistId { get; private set; }
    public static Guid PharmacistId { get; private set; }
    public static Guid TechnicianId { get; private set; }
    
    public static long DoctorEntityId { get; private set; }
    public static long Doctor2EntityId { get; private set; }
    public static long Patient1EntityId { get; private set; }
    public static long Patient2EntityId { get; private set; }
    public static long SpecialtyEntityId { get; private set; }
    public static long SlotEntityId { get; private set; }
    public static long MedicineEntityId { get; private set; }
    public static long PackageEntityId { get; private set; }

    protected IntegrationTestBase(CustomWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        
        // Ensure Database is created and Seed data
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        SeedData(db, scope.ServiceProvider).GetAwaiter().GetResult();
    }

    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    private async Task SeedData(AppDbContext db, IServiceProvider sp)
    {
        await _seedLock.WaitAsync();
        try
        {
            if (await db.Users.AnyAsync())
            {
                var docExisting = await db.Doctors.FirstAsync();
                var doc2Existing = await db.Doctors.OrderBy(d => d.Id).Skip(1).FirstOrDefaultAsync();
                var pat1Existing = await db.Patients.FirstAsync();
                var pat2Existing = await db.Patients.OrderBy(p => p.Id).Skip(1).FirstOrDefaultAsync();
                var specExisting = await db.Specialties.FirstAsync();
                var slotExisting = await db.AppointmentSlots.FirstAsync();
                var medExisting = await db.Medicines.FirstAsync();
                var pkgExisting = await db.HealthPackages.FirstAsync();

                DoctorEntityId = docExisting.Id;
                Doctor2EntityId = doc2Existing?.Id ?? 0;
                Patient1EntityId = pat1Existing.Id;
                Patient2EntityId = pat2Existing?.Id ?? 0;
                SpecialtyEntityId = specExisting.Id;
                SlotEntityId = slotExisting.Id;
                MedicineEntityId = medExisting.Id;
                PackageEntityId = pkgExisting.Id;

                AdminId = (await db.Users.FirstAsync(u => u.UserName == "admin@test.com")).Id;
                DoctorId = (await db.Users.FirstAsync(u => u.UserName == "doc@test.com")).Id;
                ReceptionistId = (await db.Users.FirstAsync(u => u.UserName == "rec@test.com")).Id;
                PharmacistId = (await db.Users.FirstAsync(u => u.UserName == "pharm@test.com")).Id;
                TechnicianId = (await db.Users.FirstOrDefaultAsync(u => u.UserName == "tech@test.com"))?.Id ?? Guid.Empty;
                Patient1Id = (await db.Users.FirstAsync(u => u.UserName == "pat1@test.com")).Id;
                Patient2Id = (await db.Users.FirstAsync(u => u.UserName == "pat2@test.com")).Id;
                return;
            }

            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            // Roles
            string[] roles = { "Admin", "Doctor", "Patient", "Receptionist", "Pharmacist", "DiagnosticTechnician" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                    if (!result.Succeeded)
                    {
                        throw new Exception($"Failed to create role '{role}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
                    }
                }
            }

            AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            DoctorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var doc2UserId = Guid.Parse("22222222-2222-2222-2222-222222222223");
            ReceptionistId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            PharmacistId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            TechnicianId = Guid.Parse("66666666-6666-6666-6666-666666666666");
            Patient1Id = Guid.Parse("44444444-4444-4444-4444-444444444441");
            Patient2Id = Guid.Parse("44444444-4444-4444-4444-444444444442");

            // Users
            var admin = new ApplicationUser { Id = AdminId, UserName = "admin@test.com", Email = "admin@test.com", FullName = "Admin", PhoneNumber = "0123456781", IsActive = true };
            var res1 = await userManager.CreateAsync(admin, "Pass@123");
            if (!res1.Succeeded) throw new Exception("Failed to create admin: " + res1.Errors.First().Description);
            await userManager.AddToRoleAsync(admin, "Admin");

            var doc = new ApplicationUser { Id = DoctorId, UserName = "doc@test.com", Email = "doc@test.com", FullName = "Doctor 1", PhoneNumber = "0123456782", IsActive = true };
            var res2 = await userManager.CreateAsync(doc, "Pass@123");
            if (!res2.Succeeded) throw new Exception("Failed doc: " + res2.Errors.First().Description);
            await userManager.AddToRoleAsync(doc, "Doctor");

            var rec = new ApplicationUser { Id = ReceptionistId, UserName = "rec@test.com", Email = "rec@test.com", FullName = "Receptionist", PhoneNumber = "0123456783", IsActive = true };
            var res3 = await userManager.CreateAsync(rec, "Pass@123");
            if (!res3.Succeeded) throw new Exception("Failed rec: " + res3.Errors.First().Description);
            await userManager.AddToRoleAsync(rec, "Receptionist");

            var pharm = new ApplicationUser { Id = PharmacistId, UserName = "pharm@test.com", Email = "pharm@test.com", FullName = "Pharmacist", PhoneNumber = "0123456786", IsActive = true };
            var resPharm = await userManager.CreateAsync(pharm, "Pass@123");
            if (!resPharm.Succeeded) throw new Exception("Failed pharm: " + resPharm.Errors.First().Description);
            await userManager.AddToRoleAsync(pharm, "Pharmacist");

            var tech = new ApplicationUser { Id = TechnicianId, UserName = "tech@test.com", Email = "tech@test.com", FullName = "Technician", PhoneNumber = "0123456787", IsActive = true };
            var resTech = await userManager.CreateAsync(tech, "Pass@123");
            if (!resTech.Succeeded) throw new Exception("Failed tech: " + resTech.Errors.First().Description);
            await userManager.AddToRoleAsync(tech, "DiagnosticTechnician");

            var pat1 = new ApplicationUser { Id = Patient1Id, UserName = "pat1@test.com", Email = "pat1@test.com", FullName = "Patient 1", PhoneNumber = "0123456784", IsActive = true };
            var res4 = await userManager.CreateAsync(pat1, "Pass@123");
            if (!res4.Succeeded) throw new Exception("Failed pat1: " + res4.Errors.First().Description);
            await userManager.AddToRoleAsync(pat1, "Patient");

            var pat2 = new ApplicationUser { Id = Patient2Id, UserName = "pat2@test.com", Email = "pat2@test.com", FullName = "Patient 2", PhoneNumber = "0123456785", IsActive = true };
            var res5 = await userManager.CreateAsync(pat2, "Pass@123");
            if (!res5.Succeeded) throw new Exception("Failed pat2: " + res5.Errors.First().Description);
            await userManager.AddToRoleAsync(pat2, "Patient");

            // Entities
            var doctor = new Doctor { UserId = DoctorId, IsActive = true, AcademicTitle = "BS", ExperienceYears = 5 };
            db.Doctors.Add(doctor);

            var doc2 = new ApplicationUser { Id = doc2UserId, UserName = "doc2@test.com", Email = "doc2@test.com", FullName = "Doctor 2", PhoneNumber = "0123456789", IsActive = true };
            await userManager.CreateAsync(doc2, "Pass@123");
            await userManager.AddToRoleAsync(doc2, "Doctor");

            var doctor2 = new Doctor { UserId = doc2.Id, IsActive = true, AcademicTitle = "BS", ExperienceYears = 5 };
            db.Doctors.Add(doctor2);

            var patient1 = new Patient { UserId = Patient1Id, DateOfBirth = new DateOnly(1990, 1, 1), Gender = Gender.Male };
            var patient2 = new Patient { UserId = Patient2Id, DateOfBirth = new DateOnly(1995, 1, 1), Gender = Gender.Female };
            db.Patients.Add(patient1);
            db.Patients.Add(patient2);

            var spec = new Specialty { SpecialtyCode = "SP-01", Name = "Tim Mạch", IsActive = true, AiEnabled = true };
            db.Specialties.Add(spec);
            await db.SaveChangesAsync();

            db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = spec.Id, IsPrimary = true });
            db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = doctor2.Id, SpecialtyId = spec.Id, IsPrimary = true });

            var date = GetFutureWorkingDate(1);
            var schedule = new DoctorWorkSchedule { DoctorId = doctor.Id, WorkDate = date, StartTime = new TimeOnly(8,0,0), EndTime = new TimeOnly(12,0,0), IsActive = true };
            db.DoctorWorkSchedules.Add(schedule);
            await db.SaveChangesAsync();

            var slot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date, StartTime = new TimeOnly(8,0,0), EndTime = new TimeOnly(8,30,0), IsBooked = false };
            db.AppointmentSlots.Add(slot);

            var medicine = new Medicine
            {
                Code = "PARA500",
                Name = "Paracetamol 500mg",
                Unit = "Viên",
                StockQuantity = 50,
                ReorderLevel = 10,
                IsActive = true
            };
            db.Medicines.Add(medicine);

            var package = new HealthPackage
            {
                Code = "PKG-TEST",
                Name = "Gói Khám Tổng Quát Tiêu Chuẩn Test",
                TargetAudience = "Người trưởng thành từ 18 tuổi",
                Description = "Kiểm tra sức khỏe tổng quát định kỳ",
                Price = 1500000,
                IsActive = true,
                IncludedServicesJson = "[\"Khám nội tổng quát\",\"Xét nghiệm máu\"]"
            };
            db.HealthPackages.Add(package);

            var diagService1 = new DiagnosticService { Code = "LAB-TEST-01", Name = "Xét nghiệm máu test", Category = DiagnosticCategory.Laboratory, IsActive = true };
            var diagService2 = new DiagnosticService { Code = "US-TEST-01", Name = "Siêu âm bụng test", Category = DiagnosticCategory.Ultrasound, IsActive = true };
            db.DiagnosticServices.AddRange(diagService1, diagService2);

            await db.SaveChangesAsync();

            DoctorEntityId = doctor.Id;
            Doctor2EntityId = doctor2.Id;
            Patient1EntityId = patient1.Id;
            Patient2EntityId = patient2.Id;
            SpecialtyEntityId = spec.Id;
            SlotEntityId = slot.Id;
            MedicineEntityId = medicine.Id;
            PackageEntityId = package.Id;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    protected static DateOnly GetFutureWorkingDate(int daysFromNow)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).AddDays(daysFromNow));
        while (date.DayOfWeek == DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    protected async Task AuthenticateAsync(string email)
    {
        var loginResponse = await Client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Pass@123" });
        var resStr = await loginResponse.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(resStr);
        if (!doc.RootElement.TryGetProperty("data", out var dataProp))
        {
            throw new Exception("Login failed: " + resStr);
        }
        string token = dataProp.GetProperty("accessToken").GetString()!;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    protected async Task<AppointmentSlot> CreateAvailableSlotAsync(long doctorId, DateOnly date, TimeOnly startTime, TimeOnly endTime)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var schedule = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            db.DoctorWorkSchedules, s => s.DoctorId == doctorId && s.WorkDate == date && s.StartTime <= startTime && s.EndTime >= endTime);
        if (schedule == null)
        {
            schedule = new DoctorWorkSchedule
            {
                DoctorId = doctorId,
                WorkDate = date,
                StartTime = startTime < new TimeOnly(7, 0, 0) ? startTime : new TimeOnly(7, 0, 0),
                EndTime = endTime > new TimeOnly(20, 0, 0) ? endTime : new TimeOnly(20, 0, 0),
                IsActive = true
            };
            db.DoctorWorkSchedules.Add(schedule);
            await db.SaveChangesAsync();
        }

        var existingSlot = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            db.AppointmentSlots, s => s.DoctorId == doctorId && s.SlotDate == date && s.StartTime == startTime);
        if (existingSlot != null)
        {
            existingSlot.IsBooked = false;
            existingSlot.EndTime = endTime;
            await db.SaveChangesAsync();
            return existingSlot;
        }

        var slot = new AppointmentSlot
        {
            DoctorId = doctorId,
            SlotDate = date,
            StartTime = startTime,
            EndTime = endTime,
            IsBooked = false
        };
        db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();
        return slot;
    }
}
