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

        // 7. Health Care Packages (ClinicCare Branded)
        if (!await db.HealthPackages.AnyAsync())
        {
            db.HealthPackages.AddRange(
                new HealthPackage
                {
                    Code = "PKG01",
                    Name = "Gói Khám Sức Khỏe Tổng Quát Tiêu Chuẩn",
                    TargetAudience = "Mọi độ tuổi từ 18 trở lên",
                    Description = "Kiểm tra toàn diện các chỉ số huyết học, chức năng gan, thận, đường huyết, mỡ máu, X-quang phổi và siêu âm bụng tổng quát.",
                    Price = 1250000m,
                    IncludedServicesJson = "[\"Khám nội tổng quát\",\"Công thức máu 18 chỉ số\",\"Đo đường huyết Glucose\",\"Men gan AST/ALT\",\"Chức năng thận Ure/Creatinine\",\"X-quang tim phổi thẳng\",\"Siêu âm bụng tổng quát\"]",
                    IsActive = true
                },
                new HealthPackage
                {
                    Code = "PKG02",
                    Name = "Gói Tầm Soát Tim Mạch Toàn Diện",
                    TargetAudience = "Người trưởng thành, trung niên và có tiền sử tim mạch",
                    Description = "Tầm soát chuyên sâu bệnh lý mạch vành, huyết áp, rối loạn nhịp tim và xơ vữa động mạch.",
                    Price = 2800000m,
                    IncludedServicesJson = "[\"Khám chuyên khoa Tim mạch\",\"Điện tâm đồ ECG 12 chuyển đạo\",\"Siêu âm tim Doppler màu\",\"Bộ mỡ máu toàn phần (Cholesterol, Triglyceride, HDL, LDL)\",\"Đo chỉ số xơ vữa ABI\",\"Tư vấn chế độ dinh dưỡng tim mạch\"]",
                    IsActive = true
                },
                new HealthPackage
                {
                    Code = "PKG03",
                    Name = "Gói Chăm Sóc Sức Khỏe Nhi Khoa Toàn Diện",
                    TargetAudience = "Trẻ em từ 0 - 15 tuổi",
                    Description = "Đánh giá phát triển thể chất, dinh dưỡng, tầm soát thiếu máu, vi chất và kiểm tra tai mũi họng tổng quát.",
                    Price = 950000m,
                    IncludedServicesJson = "[\"Khám chuyên khoa Nhi\",\"Đánh giá chỉ số phát triển chiều cao - cân nặng\",\"Tổng phân tích tế bào máu\",\"Kiểm tra vi chất kẽm, canxi, sắt\",\"Nội soi tai mũi họng\",\"Tư vấn lịch tiêm chủng\"]",
                    IsActive = true
                },
                new HealthPackage
                {
                    Code = "PKG04",
                    Name = "Gói Tầm Soát Sức Khỏe Phụ Nữ Chuyên Sâu",
                    TargetAudience = "Nữ giới từ 18 tuổi trở lên",
                    Description = "Tầm soát bệnh lý phụ khoa, ung thư cổ tử cung, tầm soát tuyến vú và các rối loạn nội tiết.",
                    Price = 1950000m,
                    IncludedServicesJson = "[\"Khám Sản phụ khoa chuyên sâu\",\"Soi tươi dịch âm đạo\",\"Siêu âm đầu dò tử cung buồng trứng\",\"Siêu âm tuyến vú 2 bên\",\"Xét nghiệm Pap smear tầm soát sớm\",\"Định lượng hormon nội tiết\"]",
                    IsActive = true
                },
                new HealthPackage
                {
                    Code = "PKG05",
                    Name = "Gói Khám Cơ Xương Khớp & Loãng Xương",
                    TargetAudience = "Người cao tuổi, nhân viên văn phòng, người vận động thể thao",
                    Description = "Tầm soát thoái hóa khớp, thoát vị đĩa đệm, viêm khớp dạng thấp và đo mật độ xương toàn thân.",
                    Price = 1650000m,
                    IncludedServicesJson = "[\"Khám chuyên khoa Cơ xương khớp\",\"Đo mật độ xương DEXA\",\"X-quang khớp gối / cột sống thắt lưng\",\"Xét nghiệm Axit Uric (gút)\",\"Định lượng Canxi và Vitamin D3\"]",
                    IsActive = true
                },
                new HealthPackage
                {
                    Code = "PKG06",
                    Name = "Gói Tầm Soát Gan Mật & Rối Loạn Chuyển Hóa",
                    TargetAudience = "Người có nguy cơ gan nhiễm mỡ, viêm gan, đái tháo đường",
                    Description = "Đánh giá chức năng gan mật, tầm soát virus viêm gan B/C, men gan và các chỉ số rối loạn chuyển hóa.",
                    Price = 1750000m,
                    IncludedServicesJson = "[\"Khám chuyên khoa Nội\",\"Xét nghiệm HBsAg, Anti-HCV\",\"Men gan toàn diện AST, ALT, GGT\",\"Siêu âm Doppler gan mật tụy lách\",\"Chỉ số đường huyết HbA1c\"]",
                    IsActive = true
                }
            );
            await db.SaveChangesAsync();
            logger.LogInformation("HealthPackages seeded.");
        }

        // 8. Medicines & Pharmacy Seed
        if (!await db.Medicines.AnyAsync())
        {
            var med1 = new Medicine { Code = "MED01", Name = "Paracetamol 500mg", Unit = "Viên", StockQuantity = 500, ReorderLevel = 100, IsActive = true };
            var med2 = new Medicine { Code = "MED02", Name = "Amoxicillin 500mg", Unit = "Viên", StockQuantity = 300, ReorderLevel = 50, IsActive = true };
            var med3 = new Medicine { Code = "MED03", Name = "Ibuprofen 400mg", Unit = "Viên", StockQuantity = 250, ReorderLevel = 50, IsActive = true };
            var med4 = new Medicine { Code = "MED04", Name = "Omeprazole 20mg", Unit = "Viên", StockQuantity = 400, ReorderLevel = 80, IsActive = true };
            var med5 = new Medicine { Code = "MED05", Name = "Cefixime 200mg", Unit = "Viên", StockQuantity = 150, ReorderLevel = 40, IsActive = true };
            var med6 = new Medicine { Code = "MED06", Name = "Loratadine 10mg", Unit = "Viên", StockQuantity = 200, ReorderLevel = 50, IsActive = true };
            var med7 = new Medicine { Code = "MED07", Name = "Metformin 500mg", Unit = "Viên", StockQuantity = 350, ReorderLevel = 60, IsActive = true };
            var med8 = new Medicine { Code = "MED08", Name = "Amlodipine 5mg", Unit = "Viên", StockQuantity = 300, ReorderLevel = 50, IsActive = true };
            var med9 = new Medicine { Code = "MED09", Name = "Vitamin C 500mg", Unit = "Viên", StockQuantity = 600, ReorderLevel = 100, IsActive = true };
            var med10 = new Medicine { Code = "MED10", Name = "Salbutamol 2mg", Unit = "Viên", StockQuantity = 25, ReorderLevel = 50, IsActive = true }; // Low stock alert!
            var med11 = new Medicine { Code = "MED11", Name = "Berberin 100mg", Unit = "Viên", StockQuantity = 400, ReorderLevel = 80, IsActive = true };
            var med12 = new Medicine { Code = "MED12", Name = "Phosphalugel 20g", Unit = "Gói", StockQuantity = 180, ReorderLevel = 40, IsActive = true };

            db.Medicines.AddRange(med1, med2, med3, med4, med5, med6, med7, med8, med9, med10, med11, med12);
            await db.SaveChangesAsync();

            var pharmacistUser = await userManager.FindByEmailAsync("pharmacist@cliniccare.local");
            var pharmacistId = pharmacistUser?.Id ?? Guid.NewGuid();

            var allMeds = await db.Medicines.ToListAsync();
            foreach (var m in allMeds)
            {
                db.MedicineStockTransactions.Add(new MedicineStockTransaction
                {
                    MedicineId = m.Id,
                    Type = MedicineStockTransactionType.Initial,
                    QuantityChange = m.StockQuantity,
                    BalanceAfter = m.StockQuantity,
                    Reason = "Nhập kho ban đầu hệ thống",
                    ActorUserId = pharmacistId,
                    CreatedAt = DateTime.UtcNow.AddDays(-10)
                });
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Medicines and initial inventory transactions seeded.");

            // Prescriptions for completed appointments
            var completedAppts = await db.Appointments
                .Where(a => a.Status == AppointmentStatus.Completed)
                .Take(5)
                .ToListAsync();

            if (completedAppts.Count >= 2)
            {
                var pres1 = new Prescription
                {
                    AppointmentId = completedAppts[0].Id,
                    PatientId = completedAppts[0].PatientId,
                    DoctorId = completedAppts[0].DoctorId,
                    Status = PrescriptionStatus.Issued,
                    Notes = "Uống thuốc sau ăn, uống nhiều nước ấm. Nghỉ ngơi hợp lý.",
                    CreatedAt = DateTime.UtcNow.AddHours(-2)
                };
                db.Prescriptions.Add(pres1);
                await db.SaveChangesAsync();

                db.PrescriptionItems.AddRange(
                    new PrescriptionItem { PrescriptionId = pres1.Id, MedicineId = med1.Id, Quantity = 10, Dosage = "1 viên / lần", Frequency = "2 lần / ngày sau ăn", DurationDays = 5, Instructions = "Uống khi sốt hoặc đau đầu" },
                    new PrescriptionItem { PrescriptionId = pres1.Id, MedicineId = med2.Id, Quantity = 14, Dosage = "1 viên / lần", Frequency = "2 lần / ngày (sáng - tối)", DurationDays = 7, Instructions = "Uống đều đặn đủ liệu trình kháng sinh" },
                    new PrescriptionItem { PrescriptionId = pres1.Id, MedicineId = med9.Id, Quantity = 10, Dosage = "1 viên / lần", Frequency = "1 lần / ngày buổi sáng", DurationDays = 10, Instructions = "Tăng cường đề kháng" }
                );

                var pres2 = new Prescription
                {
                    AppointmentId = completedAppts[1].Id,
                    PatientId = completedAppts[1].PatientId,
                    DoctorId = completedAppts[1].DoctorId,
                    Status = PrescriptionStatus.Dispensed,
                    Notes = "Kiêng đồ cay nóng, bia rượu và chất kích thích.",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    DispensedAt = DateTime.UtcNow.AddDays(-1).AddHours(1),
                    DispensedByUserId = pharmacistId
                };
                db.Prescriptions.Add(pres2);
                await db.SaveChangesAsync();

                db.PrescriptionItems.AddRange(
                    new PrescriptionItem { PrescriptionId = pres2.Id, MedicineId = med4.Id, Quantity = 14, Dosage = "1 viên / lần", Frequency = "1 lần / ngày trước ăn sáng 30 phút", DurationDays = 14, Instructions = "Uống trước bữa ăn" },
                    new PrescriptionItem { PrescriptionId = pres2.Id, MedicineId = med12.Id, Quantity = 15, Dosage = "1 gói / lần", Frequency = "3 lần / ngày khi đau hoặc sau ăn 1h", DurationDays = 5, Instructions = "Lắc kỹ trước khi dùng" }
                );

                await db.SaveChangesAsync();
                logger.LogInformation("Sample Prescriptions seeded.");
            }
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
