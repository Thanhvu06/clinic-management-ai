def generate_seeder():
    code = """using ClinicManagement.Domain.Entities;
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
        var adminId1 = await SeedUserAsync(userManager, "admin@cliniccare.local", "Admin Demo", "0900000001", "Admin", password);
        var adminId2 = await SeedUserAsync(userManager, "admin.phu@cliniccare.local", "Admin Phụ", "0900000002", "Admin", password);
        
        var rec1 = await SeedUserAsync(userManager, "reception@cliniccare.local", "Lễ tân Demo", "0910000001", "Receptionist", password);
        var rec2 = await SeedUserAsync(userManager, "letan.02@cliniccare.local", "Lễ tân 02", "0910000002", "Receptionist", password);
        var rec3 = await SeedUserAsync(userManager, "letan.03@cliniccare.local", "Lễ tân 03", "0910000003", "Receptionist", password);

        var docUsers = new List<ApplicationUser>();
        docUsers.Add(await SeedUserAsync(userManager, "doctor@cliniccare.local", "BS Nguyễn Văn Demo", "0920000001", "Doctor", password));
        for (int i = 2; i <= 10; i++) {
            docUsers.Add(await SeedUserAsync(userManager, $"bacsi.{i:D2}@cliniccare.local", $"BS Khám Bệnh {i}", $"09200000{i:D2}", "Doctor", password));
        }

        var patUsers = new List<ApplicationUser>();
        patUsers.Add(await SeedUserAsync(userManager, "patient@cliniccare.local", "Bệnh nhân Demo", "0930000001", "Patient", password));
        for (int i = 2; i <= 20; i++) {
            patUsers.Add(await SeedUserAsync(userManager, $"benhnhan.{i:D2}@cliniccare.local", $"Bệnh Nhân {i}", $"09300000{i:D2}", "Patient", password));
        }

        // Specialties
        if (!await db.Specialties.AnyAsync())
        {
            db.Specialties.AddRange(
                new Specialty { SpecialtyCode = "SP01", Name = "Nội tổng quát", Description = "Khám các bệnh nội khoa chung", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP02", Name = "Nhi khoa", Description = "Khám bệnh cho trẻ em", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP03", Name = "Sản phụ khoa", Description = "Khám thai, phụ khoa", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP04", Name = "Da liễu", Description = "Khám các bệnh về da", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP05", Name = "Tai mũi họng", Description = "Khám các bệnh tai mũi họng", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP06", Name = "Tim mạch", Description = "Khám bệnh tim mạch", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP07", Name = "Cơ xương khớp", Description = "Khám bệnh cơ xương khớp", IsActive = true, AiEnabled = true }
            );
            await db.SaveChangesAsync();
            logger.LogInformation("Specialties seeded.");
        }
        var specs = await db.Specialties.ToListAsync();

        // Doctors
        if (!await db.Doctors.AnyAsync())
        {
            var r = new Random(42);
            for (int i = 0; i < docUsers.Count; i++)
            {
                var d = new Doctor { UserId = docUsers[i].Id, IsActive = true, ExperienceYears = r.Next(3, 20), AcademicTitle = "BS" };
                db.Doctors.Add(d);
            }
            await db.SaveChangesAsync();

            var docs = await db.Doctors.ToListAsync();
            // Bind to specialties
            for (int i = 0; i < docs.Count; i++)
            {
                var primarySpec = specs[i % specs.Count];
                db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = docs[i].Id, SpecialtyId = primarySpec.Id, IsPrimary = true });
                
                // Some have secondary
                if (i % 3 == 0) {
                    var sec = specs[(i + 1) % specs.Count];
                    db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = docs[i].Id, SpecialtyId = sec.Id, IsPrimary = false });
                }
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Doctors seeded.");
        }
        var doctors = await db.Doctors.ToListAsync();

        // Patients
        if (!await db.Patients.AnyAsync())
        {
            foreach (var pat in patUsers)
            {
                db.Patients.Add(new Patient { UserId = pat.Id, DateOfBirth = new DateOnly(1990, 1, 1), Gender = Gender.Male });
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Patients seeded.");
        }
        var patients = await db.Patients.ToListAsync();

        // Work Schedules & Slots for next 14 days
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!await db.DoctorWorkSchedules.AnyAsync())
        {
            foreach (var doc in doctors)
            {
                for (int i = 1; i <= 14; i++)
                {
                    var date = today.AddDays(i);
                    // Skip weekends for some variety, maybe just Sunday
                    if (date.DayOfWeek == DayOfWeek.Sunday) continue;

                    var schedule = new DoctorWorkSchedule
                    {
                        DoctorId = doc.Id,
                        WorkDate = date,
                        StartTime = new TimeOnly(8, 0),
                        EndTime = new TimeOnly(17, 0),
                        IsActive = true
                    };
                    db.DoctorWorkSchedules.Add(schedule);
                    await db.SaveChangesAsync();

                    // Morning slots 08:00 to 11:30
                    var startTime = new TimeOnly(8, 0);
                    for (int j = 0; j < 7; j++)
                    {
                        db.AppointmentSlots.Add(new AppointmentSlot { DoctorId = doc.Id, SlotDate = date, StartTime = startTime, EndTime = startTime.AddMinutes(30), IsBooked = false });
                        startTime = startTime.AddMinutes(30);
                    }
                    
                    // Afternoon slots 13:30 to 17:00
                    startTime = new TimeOnly(13, 30);
                    for (int j = 0; j < 7; j++)
                    {
                        db.AppointmentSlots.Add(new AppointmentSlot { DoctorId = doc.Id, SlotDate = date, StartTime = startTime, EndTime = startTime.AddMinutes(30), IsBooked = false });
                        startTime = startTime.AddMinutes(30);
                    }
                }
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Work schedules and slots seeded.");
        }

        // Appointments
        if (!await db.Appointments.AnyAsync())
        {
            var slots = await db.AppointmentSlots.ToListAsync();
            var mainPat = patients[0];
            var mainDoc = doctors[0];
            var random = new Random(42);

            var aptCount = 0;
            // helper
            async Task CreateApt(Patient p, Doctor d, AppointmentSlot s, AppointmentStatus status, bool isPast) {
                var code = $"DEMO-{DateTime.Now.Ticks % 100000}-{aptCount++}";
                var a = new Appointment {
                    AppointmentCode = code,
                    PatientId = p.Id, DoctorId = d.Id, SpecialtyId = specs[0].Id,
                    AppointmentSlotId = s.Id, AppointmentDate = s.SlotDate,
                    StartTime = s.StartTime, EndTime = s.EndTime,
                    Reason = "Khám định kỳ", Status = status
                };
                db.Appointments.Add(a);
                s.IsBooked = true; // wait, if Cancelled, slot might be false, but let's say true for Confirmed/Pending
                if (status == AppointmentStatus.Cancelled) s.IsBooked = false;
                await db.SaveChangesAsync();

                db.AppointmentHistories.Add(new AppointmentHistory {
                    AppointmentId = a.Id, Action = AppointmentHistoryAction.Created, NewStatus = AppointmentStatus.Pending, Note = "Bệnh nhân tự đặt lịch", PerformedByUserId = p.UserId, CreatedAt = DateTime.Now.AddDays(-10)
                });

                if (status != AppointmentStatus.Pending) {
                    db.AppointmentHistories.Add(new AppointmentHistory {
                        AppointmentId = a.Id, Action = AppointmentHistoryAction.Confirmed, OldStatus = AppointmentStatus.Pending, NewStatus = AppointmentStatus.Confirmed, Note = "Lễ tân xác nhận", PerformedByUserId = adminId1.Id, CreatedAt = DateTime.Now.AddDays(-9)
                    });
                }
                
                if (status == AppointmentStatus.Completed) {
                    db.AppointmentHistories.Add(new AppointmentHistory {
                        AppointmentId = a.Id, Action = AppointmentHistoryAction.Completed, OldStatus = AppointmentStatus.Confirmed, NewStatus = AppointmentStatus.Completed, Note = "Bác sĩ hoàn thành", PerformedByUserId = d.UserId, CreatedAt = DateTime.Now.AddDays(-1)
                    });
                    db.VisitSummaries.Add(new VisitSummary {
                        AppointmentId = a.Id, DoctorId = d.Id, Summary = "Sức khỏe ổn định, cần theo dõi thêm huyết áp.", FollowUpInstruction = "Ăn nhạt, tập thể dục nhẹ."
                    });
                }
                else if (status == AppointmentStatus.NoShow) {
                    db.AppointmentHistories.Add(new AppointmentHistory {
                        AppointmentId = a.Id, Action = AppointmentHistoryAction.NoShow, OldStatus = AppointmentStatus.Confirmed, NewStatus = AppointmentStatus.NoShow, Note = "Bệnh nhân vắng mặt", PerformedByUserId = d.UserId, CreatedAt = DateTime.Now.AddDays(-1)
                    });
                }
                else if (status == AppointmentStatus.Cancelled) {
                    db.AppointmentHistories.Add(new AppointmentHistory {
                        AppointmentId = a.Id, Action = AppointmentHistoryAction.Cancelled, OldStatus = AppointmentStatus.Pending, NewStatus = AppointmentStatus.Cancelled, Note = "Khách yêu cầu hủy", PerformedByUserId = p.UserId, CreatedAt = DateTime.Now.AddDays(-2)
                    });
                }
                await db.SaveChangesAsync();
            }

            var futureSlots = slots.Where(x => x.SlotDate > today).ToList();
            var pastSlots = slots.Where(x => x.SlotDate <= today).ToList(); // actually slotDate > today is all of them because we generated from today+1. Let's pretend some are "past" for completed.
            
            // To make past appointments, we need past slots
            var pastDate = today.AddDays(-5);
            var pastSchedule = new DoctorWorkSchedule { DoctorId = mainDoc.Id, WorkDate = pastDate, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), IsActive = true };
            db.DoctorWorkSchedules.Add(pastSchedule);
            await db.SaveChangesAsync();
            
            var pastSlot1 = new AppointmentSlot { DoctorId = mainDoc.Id, SlotDate = pastDate, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30) };
            var pastSlot2 = new AppointmentSlot { DoctorId = mainDoc.Id, SlotDate = pastDate, StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(9, 0) };
            var pastSlot3 = new AppointmentSlot { DoctorId = mainDoc.Id, SlotDate = pastDate, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30) };
            var pastSlot4 = new AppointmentSlot { DoctorId = mainDoc.Id, SlotDate = pastDate, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 30) };
            db.AppointmentSlots.AddRange(pastSlot1, pastSlot2, pastSlot3, pastSlot4);
            await db.SaveChangesAsync();

            await CreateApt(mainPat, mainDoc, pastSlot1, AppointmentStatus.Completed, true);
            await CreateApt(patients[1], mainDoc, pastSlot2, AppointmentStatus.Completed, true);
            await CreateApt(mainPat, mainDoc, pastSlot3, AppointmentStatus.NoShow, true);
            await CreateApt(patients[2], mainDoc, pastSlot4, AppointmentStatus.Cancelled, true);

            // 6 Pending future
            for(int i=0; i<6; i++) await CreateApt(patients[i], doctors[i%doctors.Count], futureSlots[i], AppointmentStatus.Pending, false);
            // 10 Confirmed future
            for(int i=6; i<16; i++) await CreateApt(patients[i], doctors[(i+1)%doctors.Count], futureSlots[i], AppointmentStatus.Confirmed, false);
            
            logger.LogInformation("Appointments seeded.");
            
            // Change requests
            var pendingAppt = await db.Appointments.FirstAsync(a => a.Status == AppointmentStatus.Pending);
            db.AppointmentChangeRequests.Add(new AppointmentChangeRequest {
                AppointmentId = pendingAppt.Id, RequestType = AppointmentChangeRequestType.Cancellation, Reason = "Bận việc đột xuất", Status = AppointmentChangeRequestStatus.Pending, RequestedByUserId = pendingAppt.Patient.UserId, CreatedAt = DateTime.Now
            });

            var confAppt = await db.Appointments.Skip(1).FirstAsync(a => a.Status == AppointmentStatus.Confirmed);
            db.AppointmentChangeRequests.Add(new AppointmentChangeRequest {
                AppointmentId = confAppt.Id, RequestType = AppointmentChangeRequestType.Reschedule, RequestedSlotId = futureSlots[50].Id, Reason = "Đổi ngày khám", Status = AppointmentChangeRequestStatus.Pending, RequestedByUserId = confAppt.Patient.UserId, CreatedAt = DateTime.Now
            });
            await db.SaveChangesAsync();

            // Revisit request
            var compAppt = await db.Appointments.FirstAsync(a => a.Status == AppointmentStatus.Completed);
            db.RevisitRequests.Add(new RevisitRequest {
                AppointmentId = compAppt.Id, PatientId = compAppt.PatientId, DoctorId = compAppt.DoctorId, SuggestedDate = today.AddDays(7), Note = "Tái khám sau 1 tuần", Status = RevisitRequestStatus.PendingPatientResponse
            });
            await db.SaveChangesAsync();

            // Leave requests
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest {
                DoctorId = mainDoc.Id, StartDateTime = DateTime.Now.AddDays(2), EndDateTime = DateTime.Now.AddDays(3), Reason = "Nghỉ phép cá nhân", Status = DoctorLeaveRequestStatus.Pending
            });
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest {
                DoctorId = doctors[1].Id, StartDateTime = DateTime.Now.AddDays(5), EndDateTime = DateTime.Now.AddDays(6), Reason = "Đi hội thảo", Status = DoctorLeaveRequestStatus.Approved
            });
            await db.SaveChangesAsync();

            logger.LogInformation("Requests and Leaves seeded.");
        }

        logger.LogInformation("Development Data Seeding completed.");
    }

    private static async Task<ApplicationUser> SeedUserAsync(UserManager<ApplicationUser> userManager, string email, string name, string phone, string role, string password)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser { UserName = email, Email = email, FullName = name, PhoneNumber = phone, IsActive = true };
            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, role);
        }
        return user;
    }
}
"""
    with open("src/backend/ClinicManagement.Infrastructure/Persistence/DevelopmentDataSeeder.cs", "w", encoding="utf-8") as f:
        f.write(code)

generate_seeder()
