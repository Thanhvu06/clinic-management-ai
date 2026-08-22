using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ClinicManagement.Infrastructure.Persistence;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevelopmentDataSeeder");
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = serviceProvider.GetRequiredService<AppDbContext>();

        logger.LogInformation("Starting Development Data Seeding...");

        var password = "Demo@12345";

        // Seed Users
        var admin = await SeedUserAsync(userManager, "admin@cliniccare.local", "Admin Demo", "0900000001", "Admin", password);
        var receptionist = await SeedUserAsync(userManager, "reception@cliniccare.local", "Receptionist Demo", "0900000002", "Receptionist", password);
        var doctor1User = await SeedUserAsync(userManager, "doctor@cliniccare.local", "Doctor Demo 1", "0900000003", "Doctor", password);
        var doctor2User = await SeedUserAsync(userManager, "doctor2@cliniccare.local", "Doctor Demo 2", "0900000004", "Doctor", password);
        var patientUser = await SeedUserAsync(userManager, "patient@cliniccare.local", "Patient Demo", "0900000005", "Patient", password);

        // Seed Specialties
        if (!await db.Specialties.AnyAsync())
        {
            db.Specialties.AddRange(
                new Specialty { SpecialtyCode = "SP01", Name = "Nội khoa", Description = "Khám các bệnh nội khoa chung", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP02", Name = "Nhi khoa", Description = "Khám bệnh cho trẻ em", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP03", Name = "Da liễu", Description = "Khám các bệnh về da", IsActive = true, AiEnabled = true }
            );
            await db.SaveChangesAsync();
            logger.LogInformation("Specialties seeded.");
        }

        var specialties = await db.Specialties.ToListAsync();

        // Seed Doctors
        if (!await db.Doctors.AnyAsync())
        {
            var doctor1 = new Doctor { UserId = doctor1User.Id, IsActive = true };
            var doctor2 = new Doctor { UserId = doctor2User.Id, IsActive = true };
            db.Doctors.AddRange(doctor1, doctor2);
            await db.SaveChangesAsync();

            db.DoctorSpecialties.AddRange(
                new DoctorSpecialty { DoctorId = doctor1.Id, SpecialtyId = specialties[0].Id, IsPrimary = true },
                new DoctorSpecialty { DoctorId = doctor2.Id, SpecialtyId = specialties[1].Id, IsPrimary = true }
            );
            await db.SaveChangesAsync();
            logger.LogInformation("Doctors seeded.");
        }

        var doctors = await db.Doctors.ToListAsync();
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.UserId == patientUser.Id);
        if (patient == null)
        {
            patient = new Patient { UserId = patientUser.Id };
            db.Patients.Add(patient);
            await db.SaveChangesAsync();
            logger.LogInformation("Patient seeded.");
        }

        // Seed Work Schedules & Slots (next 7 days)
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!await db.DoctorWorkSchedules.AnyAsync())
        {
            foreach (var doc in doctors)
            {
                for (int i = 1; i <= 7; i++)
                {
                    var date = today.AddDays(i);
                    var schedule = new DoctorWorkSchedule
                    {
                        DoctorId = doc.Id,
                        WorkDate = date,
                        StartTime = new TimeOnly(8, 0),
                        EndTime = new TimeOnly(16, 30),
                        IsActive = true
                    };
                    db.DoctorWorkSchedules.Add(schedule);
                    await db.SaveChangesAsync();

                    // Morning slots 08:00 to 11:30
                    var startTime = new TimeOnly(8, 0);
                    for (int j = 0; j < 7; j++)
                    {
                        db.AppointmentSlots.Add(new AppointmentSlot
                        {
                            DoctorId = doc.Id,
                            SlotDate = date,
                            StartTime = startTime,
                            EndTime = startTime.AddMinutes(30),
                            IsBooked = false
                        });
                        startTime = startTime.AddMinutes(30);
                    }
                    
                    // Afternoon slots 13:00 to 16:30
                    startTime = new TimeOnly(13, 0);
                    for (int j = 0; j < 7; j++)
                    {
                        db.AppointmentSlots.Add(new AppointmentSlot
                        {
                            DoctorId = doc.Id,
                            SlotDate = date,
                            StartTime = startTime,
                            EndTime = startTime.AddMinutes(30),
                            IsBooked = false
                        });
                        startTime = startTime.AddMinutes(30);
                    }
                }
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Work schedules and slots seeded.");
        }

        logger.LogInformation("Development Data Seeding completed.");
    }

    private static async Task<ApplicationUser> SeedUserAsync(UserManager<ApplicationUser> userManager, string email, string name, string phone, string role, string password)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = name,
                PhoneNumber = phone,
                IsActive = true
            };
            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, role);
        }
        return user;
    }
}
