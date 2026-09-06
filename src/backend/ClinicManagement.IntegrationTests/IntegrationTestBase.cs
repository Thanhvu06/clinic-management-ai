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
    private static bool _seeded = false;

    private async Task SeedData(AppDbContext db, IServiceProvider sp)
    {
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded || db.Users.Any())
            {
                _seeded = true;
                return;
            }

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Roles
        await roleManager.CreateAsync(new IdentityRole<Guid>("Admin"));
        await roleManager.CreateAsync(new IdentityRole<Guid>("Doctor"));
        await roleManager.CreateAsync(new IdentityRole<Guid>("Patient"));
        await roleManager.CreateAsync(new IdentityRole<Guid>("Receptionist"));
        await roleManager.CreateAsync(new IdentityRole<Guid>("Pharmacist"));

        // Users
        var admin = new ApplicationUser { UserName = "admin@test.com", Email = "admin@test.com", FullName = "Admin", PhoneNumber = "0123456781", IsActive = true };
        var res1 = await userManager.CreateAsync(admin, "Pass@123");
        if (!res1.Succeeded) throw new Exception("Failed to create admin: " + res1.Errors.First().Description);
        await userManager.AddToRoleAsync(admin, "Admin");
        AdminId = admin.Id;

        var doc = new ApplicationUser { UserName = "doc@test.com", Email = "doc@test.com", FullName = "Doctor 1", PhoneNumber = "0123456782", IsActive = true };
        var res2 = await userManager.CreateAsync(doc, "Pass@123");
        if (!res2.Succeeded) throw new Exception("Failed doc: " + res2.Errors.First().Description);
        await userManager.AddToRoleAsync(doc, "Doctor");
        DoctorId = doc.Id;

        var rec = new ApplicationUser { UserName = "rec@test.com", Email = "rec@test.com", FullName = "Receptionist", PhoneNumber = "0123456783", IsActive = true };
        var res3 = await userManager.CreateAsync(rec, "Pass@123");
        if (!res3.Succeeded) throw new Exception("Failed rec: " + res3.Errors.First().Description);
        await userManager.AddToRoleAsync(rec, "Receptionist");
        ReceptionistId = rec.Id;

        var pharm = new ApplicationUser { UserName = "pharm@test.com", Email = "pharm@test.com", FullName = "Pharmacist", PhoneNumber = "0123456786", IsActive = true };
        var resPharm = await userManager.CreateAsync(pharm, "Pass@123");
        if (!resPharm.Succeeded) throw new Exception("Failed pharm: " + resPharm.Errors.First().Description);
        await userManager.AddToRoleAsync(pharm, "Pharmacist");
        PharmacistId = pharm.Id;

        var pat1 = new ApplicationUser { UserName = "pat1@test.com", Email = "pat1@test.com", FullName = "Patient 1", PhoneNumber = "0123456784", IsActive = true };
        var res4 = await userManager.CreateAsync(pat1, "Pass@123");
        if (!res4.Succeeded) throw new Exception("Failed pat1: " + res4.Errors.First().Description);
        await userManager.AddToRoleAsync(pat1, "Patient");
        Patient1Id = pat1.Id;

        var pat2 = new ApplicationUser { UserName = "pat2@test.com", Email = "pat2@test.com", FullName = "Patient 2", PhoneNumber = "0123456785", IsActive = true };
        var res5 = await userManager.CreateAsync(pat2, "Pass@123");
        if (!res5.Succeeded) throw new Exception("Failed pat2: " + res5.Errors.First().Description);
        await userManager.AddToRoleAsync(pat2, "Patient");
        Patient2Id = pat2.Id;

        // Entities
        var doctor = new Doctor { UserId = DoctorId, IsActive = true, AcademicTitle = "BS", ExperienceYears = 5 };
        db.Doctors.Add(doctor);

        var doc2 = new ApplicationUser { UserName = "doc2@test.com", Email = "doc2@test.com", FullName = "Doctor 2", PhoneNumber = "0123456789", IsActive = true };
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
        await db.SaveChangesAsync();

        DoctorEntityId = doctor.Id;
        Doctor2EntityId = doctor2.Id;
        Patient1EntityId = patient1.Id;
        Patient2EntityId = patient2.Id;
        SpecialtyEntityId = spec.Id;
        SlotEntityId = slot.Id;
        MedicineEntityId = medicine.Id;
        PackageEntityId = package.Id;
        
        _seeded = true;
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
