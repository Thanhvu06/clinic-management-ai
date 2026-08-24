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
        var adminId1 = await SeedUserAsync(userManager, "admin@cliniccare.local", "Quản trị viên", "0999999999", "Admin", password);

        // Pharmacist
        var pharmacist = await SeedUserAsync(userManager, "pharmacist@cliniccare.local", "Dược sĩ Lâm Sàng", "0977777777", "Pharmacist", password);
        var adminId2 = await SeedUserAsync(userManager, "admin.02@cliniccare.local", "Quản trị viên 2", "0980000002", "Admin", password);
        
        var rec1 = await SeedUserAsync(userManager, "reception@cliniccare.local", "Lễ tân 1", "0900000002", "Receptionist", password);
        var rec2 = await SeedUserAsync(userManager, "letan.02@cliniccare.local", "Lễ tân 2", "0981000002", "Receptionist", password);
        var rec3 = await SeedUserAsync(userManager, "letan.03@cliniccare.local", "Lễ tân 3", "0981000003", "Receptionist", password);

        var docUsers = new List<ApplicationUser>();
        var docNames = new[] { "BS.CKI Nguyá»…n Minh Kháº£i", "BS. Tráº§n Thu HĂ ", "BS.CKII LĂª HoĂ ng Nam", "ThS.BS Pháº¡m VÄƒn HĂ¹ng", "BS. Äinh Thá»‹ Yáº¿n", "BS.CKI VÅ© Quang Vinh", "TS.BS BĂ¹i Háº£i Yáº¿n", "BS. Äá»— Tuáº¥n Anh", "BS.CKI LĂ½ Kim Dung", "BS. HoĂ ng VÄƒn Äáº¡t" };
        docUsers.Add(await SeedUserAsync(userManager, "doctor@cliniccare.local", docNames[0], "0900000003", "Doctor", password));
        for (int i = 2; i <= 10; i++) {
            docUsers.Add(await SeedUserAsync(userManager, $"bacsi.{i:D2}@cliniccare.local", docNames[i-1], $"09820000{i:D2}", "Doctor", password));
        }

        var patUsers = new List<ApplicationUser>();
        patUsers.Add(await SeedUserAsync(userManager, "patient@cliniccare.local", "Nguyá»…n ÄĂ¬nh ThĂ nh", "0900000004", "Patient", password));
        var patNames = new[] { "LĂª Thá»‹ Lan", "Tráº§n VÄƒn BĂ¬nh", "Pháº¡m Thu HÆ°Æ¡ng", "HoĂ ng Ngá»c SÆ¡n", "VÅ© Thá»‹ Mai", "Äáº·ng VÄƒn ToĂ n", "BĂ¹i Thá»‹ TĂ¡m", "Äá»— Minh Äá»©c", "Há»“ Quang Hiáº¿u", "NgĂ´ PhÆ°Æ¡ng Trinh", "DÆ°Æ¡ng Quá»‘c CÆ°á»ng", "LĂ½ Tiá»ƒu Long", "Tráº§n ÄÄƒng Khoa", "Nguyá»…n Thá»‹ Hoa", "Phan Anh Tuáº¥n" };
        for (int i = 1; i <= 15; i++) {
            patUsers.Add(await SeedUserAsync(userManager, $"patient.{i:D2}@cliniccare.local", patNames[i-1], $"09830000{i:D2}", "Patient", password));
        }

        // Specialties
        if (!await db.Specialties.AnyAsync())
        {
            db.Specialties.AddRange(
                new Specialty { SpecialtyCode = "SP01", Name = "Ná»™i tá»•ng quĂ¡t", Description = "KhĂ¡m vĂ  Ä‘iá»u trá»‹ cĂ¡c bá»‡nh lĂ½ ná»™i khoa chung", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP02", Name = "Nhi khoa", Description = "KhĂ¡m, cháº©n Ä‘oĂ¡n vĂ  Ä‘iá»u trá»‹ bá»‡nh cho tráº» em", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP03", Name = "Sáº£n phá»¥ khoa", Description = "KhĂ¡m thai Ä‘á»‹nh ká»³ vĂ  tÆ° váº¥n sá»©c khá»e phá»¥ khoa", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP04", Name = "Da liá»…u", Description = "ChuyĂªn trá»‹ cĂ¡c váº¥n Ä‘á» vá» da, tĂ³c vĂ  mĂ³ng", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP05", Name = "Tai mÅ©i há»ng", Description = "KhĂ¡m vĂ  Ä‘iá»u trá»‹ bá»‡nh lĂ½ tai mÅ©i há»ng", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP06", Name = "Tim máº¡ch", Description = "Kiá»ƒm tra huyáº¿t Ă¡p, Ä‘o Ä‘iá»‡n tĂ¢m Ä‘á»“ vĂ  bá»‡nh lĂ½ tim máº¡ch", IsActive = true, AiEnabled = true },
                new Specialty { SpecialtyCode = "SP07", Name = "CÆ¡ xÆ°Æ¡ng khá»›p", Description = "Äiá»u trá»‹ viĂªm khá»›p, thoĂ¡i hĂ³a khá»›p vĂ  cĂ¡c cháº¥n thÆ°Æ¡ng", IsActive = true, AiEnabled = true }
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

            var docsLocal = await db.Doctors.ToListAsync();
            // Bind to specialties
            for (int i = 0; i < docsLocal.Count; i++)
            {
                var primarySpec = specs[i % specs.Count];
                db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = docsLocal[i].Id, SpecialtyId = primarySpec.Id, IsPrimary = true });
                
                // Some have secondary
                if (i % 3 == 0) {
                    var sec = specs[(i + 1) % specs.Count];
                    if (sec.Id != primarySpec.Id) {
                        db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = docsLocal[i].Id, SpecialtyId = sec.Id, IsPrimary = false });
                    }
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
                    // Skip weekends for some variety
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
                    Reason = "KhĂ¡m tÆ° váº¥n", Status = status
                };
                db.Appointments.Add(a);
                if (status != AppointmentStatus.Cancelled) s.IsBooked = true; 
                await db.SaveChangesAsync();

                db.AppointmentHistories.Add(new AppointmentHistory {
                    AppointmentId = a.Id, Action = AppointmentHistoryAction.Created, NewStatus = AppointmentStatus.Pending, Note = "Bệnh nhân tự đặt lịch", PerformedByUserId = p.UserId, CreatedAt = DateTime.Now.AddDays(-10)
                });

                if (status != AppointmentStatus.Pending && status != AppointmentStatus.Cancelled) {
                    db.AppointmentHistories.Add(new AppointmentHistory {
                        AppointmentId = a.Id, Action = AppointmentHistoryAction.Confirmed, OldStatus = AppointmentStatus.Pending, NewStatus = AppointmentStatus.Confirmed, Note = "Lễ tân xác nhận", PerformedByUserId = adminId1.Id, CreatedAt = DateTime.Now.AddDays(-9)
                    });
                }
                
                if (status == AppointmentStatus.Completed) {
                    db.AppointmentHistories.Add(new AppointmentHistory {
                        AppointmentId = a.Id, Action = AppointmentHistoryAction.Completed, OldStatus = AppointmentStatus.Confirmed, NewStatus = AppointmentStatus.Completed, Note = "Bác sĩ hoàn thành", PerformedByUserId = d.UserId, CreatedAt = DateTime.Now.AddDays(-1)
                    });
                    db.VisitSummaries.Add(new VisitSummary {
                        AppointmentId = a.Id, DoctorId = d.Id, Summary = "Sá»©c khá»e bá»‡nh nhĂ¢n tÆ°Æ¡ng Ä‘á»‘i á»•n Ä‘á»‹nh. ÄĂ£ kĂª Ä‘Æ¡n thuá»‘c vĂ  tÆ° váº¥n cháº¿ Ä‘á»™ Äƒn uá»‘ng.", FollowUpInstruction = "Uá»‘ng nhiá»u nÆ°á»›c, táº­p thá»ƒ dá»¥c thÆ°á»ng xuyĂªn."
                    });
                }
                else if (status == AppointmentStatus.NoShow) {
                    db.AppointmentHistories.Add(new AppointmentHistory {
                        AppointmentId = a.Id, Action = AppointmentHistoryAction.NoShow, OldStatus = AppointmentStatus.Confirmed, NewStatus = AppointmentStatus.NoShow, Note = "Bệnh nhân vắng mặt", PerformedByUserId = d.UserId, CreatedAt = DateTime.Now.AddDays(-1)
                    });
                }
                else if (status == AppointmentStatus.Cancelled) {
                    db.AppointmentHistories.Add(new AppointmentHistory {
                        AppointmentId = a.Id, Action = AppointmentHistoryAction.Cancelled, OldStatus = AppointmentStatus.Pending, NewStatus = AppointmentStatus.Cancelled, Note = "Khách yêu cầu hủy vì bận công việc", PerformedByUserId = p.UserId, CreatedAt = DateTime.Now.AddDays(-2)
                    });
                }
                await db.SaveChangesAsync();
            }

            var futureSlots = slots.Where(x => x.SlotDate > today).ToList();
            
            // To make past appointments, we need past slots
            var pastDate = today.AddDays(-5);
            var pastSchedule = new DoctorWorkSchedule { DoctorId = mainDoc.Id, WorkDate = pastDate, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), IsActive = true };
            db.DoctorWorkSchedules.Add(pastSchedule);
            await db.SaveChangesAsync();
            
            var pastSlots = new List<AppointmentSlot>();
            for(int i=0; i<15; i++) {
                var s = new AppointmentSlot { DoctorId = mainDoc.Id, SlotDate = pastDate, StartTime = new TimeOnly(8, 0).AddMinutes(i*30), EndTime = new TimeOnly(8, 30).AddMinutes(i*30) };
                pastSlots.Add(s);
            }
            db.AppointmentSlots.AddRange(pastSlots);
            await db.SaveChangesAsync();

            // 12 Completed
            for(int i=0; i<12; i++) await CreateApt(patients[i], mainDoc, pastSlots[i], AppointmentStatus.Completed, true);
            // 3 NoShow
            for(int i=12; i<15; i++) await CreateApt(patients[i], mainDoc, pastSlots[i], AppointmentStatus.NoShow, true);
            
            // Generate some cancelled past
            var pastDate2 = today.AddDays(-4);
            var pastSchedule2 = new DoctorWorkSchedule { DoctorId = mainDoc.Id, WorkDate = pastDate2, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), IsActive = true };
            db.DoctorWorkSchedules.Add(pastSchedule2);
            await db.SaveChangesAsync();
            
            var pastSlots2 = new List<AppointmentSlot>();
            for(int i=0; i<5; i++) {
                pastSlots2.Add(new AppointmentSlot { DoctorId = mainDoc.Id, SlotDate = pastDate2, StartTime = new TimeOnly(8, 0).AddMinutes(i*30), EndTime = new TimeOnly(8, 30).AddMinutes(i*30) });
            }
            db.AppointmentSlots.AddRange(pastSlots2);
            await db.SaveChangesAsync();
            // 5 Cancelled
            for(int i=0; i<5; i++) await CreateApt(patients[i], mainDoc, pastSlots2[i], AppointmentStatus.Cancelled, true);

            // 6 Pending future
            for(int i=0; i<6; i++) await CreateApt(patients[i], doctors[i%doctors.Count], futureSlots[i], AppointmentStatus.Pending, false);
            // 10 Confirmed future
            for(int i=6; i<16; i++) await CreateApt(patients[i], doctors[(i+1)%doctors.Count], futureSlots[i], AppointmentStatus.Confirmed, false);
            
            logger.LogInformation("Appointments seeded.");
            
            // Change requests
            var pendingAppt = await db.Appointments.Include(a => a.Patient).FirstAsync(a => a.Status == AppointmentStatus.Pending);
            db.AppointmentChangeRequests.Add(new AppointmentChangeRequest {
                AppointmentId = pendingAppt.Id, RequestType = AppointmentChangeRequestType.Cancellation, Reason = "Bận việc đột xuất", Status = AppointmentChangeRequestStatus.Pending, RequestedByUserId = pendingAppt.Patient.UserId, CreatedAt = DateTime.Now
            });

            var confAppt = await db.Appointments.Include(a => a.Patient).Skip(1).FirstAsync(a => a.Status == AppointmentStatus.Confirmed);
            db.AppointmentChangeRequests.Add(new AppointmentChangeRequest {
                AppointmentId = confAppt.Id, RequestType = AppointmentChangeRequestType.Reschedule, RequestedSlotId = futureSlots[100].Id, Reason = "Xin dá»i ngĂ y khĂ¡m sang tuáº§n sau", Status = AppointmentChangeRequestStatus.Pending, RequestedByUserId = confAppt.Patient.UserId, CreatedAt = DateTime.Now
            });
            await db.SaveChangesAsync();

            // Revisit request
            var compAppt = await db.Appointments.FirstAsync(a => a.Status == AppointmentStatus.Completed);
            db.RevisitRequests.Add(new RevisitRequest {
                AppointmentId = compAppt.Id, PatientId = compAppt.PatientId, DoctorId = compAppt.DoctorId, SuggestedDate = today.AddDays(7), Note = "TĂ¡i khĂ¡m sau 1 tuáº§n kiá»ƒm tra lÆ°á»£ng Ä‘Æ°á»ng", Status = RevisitRequestStatus.PendingPatientResponse
            });
            await db.SaveChangesAsync();

            // Leave requests
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest {
                DoctorId = mainDoc.Id, StartDateTime = DateTime.Now.AddDays(2), EndDateTime = DateTime.Now.AddDays(3), Reason = "Nghỉ phép cá nhân", Status = DoctorLeaveRequestStatus.Pending
            });
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest {
                DoctorId = doctors[1].Id, StartDateTime = DateTime.Now.AddDays(5), EndDateTime = DateTime.Now.AddDays(6), Reason = "Tham gia hội thảo chuyên ngành tại Hà Nội", Status = DoctorLeaveRequestStatus.Approved, AdminNote = "ÄĂ£ duyá»‡t, yĂªu cáº§u chuyá»ƒn ca cĂ¡c bá»‡nh nhĂ¢n ngĂ y 5."
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
            var existingByPhone = userManager.Users.FirstOrDefault(u => u.PhoneNumber == phone);
            if (existingByPhone != null) return existingByPhone;

            user = new ApplicationUser { UserName = email, Email = email, FullName = name, PhoneNumber = phone, IsActive = true };
            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, role);
        }
        return user;
    }
}
