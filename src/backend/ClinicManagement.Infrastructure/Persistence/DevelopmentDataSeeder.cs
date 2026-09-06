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
        var dateTimeProvider = serviceProvider.GetService<ClinicManagement.Application.Common.Interfaces.IDateTimeProvider>()
            ?? new ClinicManagement.Infrastructure.Services.DateTimeProvider();

        logger.LogInformation("Starting Development Data Seeding...");

        var password = "Demo@12345";

        // 1. Seed Users
        var adminId1 = await SeedUserAsync(userManager, "admin@cliniccare.local", "Quản trị viên", "0999999999", "Admin", password);
        var adminId2 = await SeedUserAsync(userManager, "admin.02@cliniccare.local", "Quản trị viên 2", "0980000002", "Admin", password);

        var pharmacist = await SeedUserAsync(userManager, "pharmacist@cliniccare.local", "Dược sĩ Lâm Sàng", "0977777777", "Pharmacist", password);

        var rec1 = await SeedUserAsync(userManager, "reception@cliniccare.local", "Lễ tân Nguyễn Thu Trang", "0900000002", "Receptionist", password);
        var rec2 = await SeedUserAsync(userManager, "letan.02@cliniccare.local", "Lễ tân Trần Mai Anh", "0981000002", "Receptionist", password);
        var rec3 = await SeedUserAsync(userManager, "letan.03@cliniccare.local", "Lễ tân Lê Hoàng Yến", "0981000003", "Receptionist", password);

        // 10 Doctors with clean FullName (without duplicated academic titles) and exact specialty mappings
        var doctorDefs = new[]
        {
            new { Email = "doctor@cliniccare.local", Name = "Nguyễn Minh Khải", Title = "BS.CKI", Exp = 12, Phone = "0900000003", PrimaryCode = "SP01", SecondaryCode = (string?)"SP06" },
            new { Email = "bacsi.02@cliniccare.local", Name = "Trần Thu Hà", Title = "BS", Exp = 7, Phone = "0982000002", PrimaryCode = "SP03", SecondaryCode = (string?)null },
            new { Email = "bacsi.03@cliniccare.local", Name = "Lê Hoàng Nam", Title = "BS.CKII", Exp = 18, Phone = "0982000003", PrimaryCode = "SP07", SecondaryCode = (string?)null },
            new { Email = "bacsi.04@cliniccare.local", Name = "Phạm Văn Hùng", Title = "ThS.BS", Exp = 14, Phone = "0982000004", PrimaryCode = "SP05", SecondaryCode = (string?)null },
            new { Email = "bacsi.05@cliniccare.local", Name = "Đinh Thị Yến", Title = "BS", Exp = 5, Phone = "0982000005", PrimaryCode = "SP04", SecondaryCode = (string?)null },
            new { Email = "bacsi.06@cliniccare.local", Name = "Vũ Quang Vinh", Title = "BS.CKI", Exp = 10, Phone = "0982000006", PrimaryCode = "SP08", SecondaryCode = (string?)null },
            new { Email = "bacsi.07@cliniccare.local", Name = "Bùi Hải Yến", Title = "TS.BS", Exp = 20, Phone = "0982000007", PrimaryCode = "SP09", SecondaryCode = (string?)null },
            new { Email = "bacsi.08@cliniccare.local", Name = "Đỗ Tuấn Anh", Title = "BS", Exp = 4, Phone = "0982000008", PrimaryCode = "SP10", SecondaryCode = (string?)null },
            new { Email = "bacsi.09@cliniccare.local", Name = "Lý Kim Dung", Title = "BS.CKI", Exp = 11, Phone = "0982000009", PrimaryCode = "SP02", SecondaryCode = (string?)null },
            new { Email = "bacsi.10@cliniccare.local", Name = "Hoàng Văn Đạt", Title = "BS", Exp = 8, Phone = "0982000010", PrimaryCode = "SP11", SecondaryCode = (string?)null }
        };

        var docUsers = new List<ApplicationUser>();
        foreach (var def in doctorDefs)
        {
            var u = await SeedUserAsync(userManager, def.Email, def.Name, def.Phone, "Doctor", password);
            docUsers.Add(u);
        }

        // 16 Patients with diverse demographics
        var patientDefs = new[]
        {
            new { Name = "Nguyễn Đình Thành", Phone = "0900000004", Email = "patient@cliniccare.local", Dob = new DateOnly(1990, 5, 15), Gender = Gender.Male },
            new { Name = "Lê Thị Lan", Phone = "0983000001", Email = "patient.01@cliniccare.local", Dob = new DateOnly(1985, 3, 22), Gender = Gender.Female },
            new { Name = "Trần Văn Bình", Phone = "0983000002", Email = "patient.02@cliniccare.local", Dob = new DateOnly(1978, 11, 8), Gender = Gender.Male },
            new { Name = "Phạm Thu Hương", Phone = "0983000003", Email = "patient.03@cliniccare.local", Dob = new DateOnly(1993, 7, 19), Gender = Gender.Female },
            new { Name = "Hoàng Ngọc Sơn", Phone = "0983000004", Email = "patient.04@cliniccare.local", Dob = new DateOnly(1968, 9, 30), Gender = Gender.Male },
            new { Name = "Vũ Thị Mai", Phone = "0983000005", Email = "patient.05@cliniccare.local", Dob = new DateOnly(2001, 1, 12), Gender = Gender.Female },
            new { Name = "Đặng Văn Toàn", Phone = "0983000006", Email = "patient.06@cliniccare.local", Dob = new DateOnly(1982, 4, 5), Gender = Gender.Male },
            new { Name = "Bùi Thị Tám", Phone = "0983000007", Email = "patient.07@cliniccare.local", Dob = new DateOnly(1955, 12, 25), Gender = Gender.Female },
            new { Name = "Đỗ Minh Đức", Phone = "0983000008", Email = "patient.08@cliniccare.local", Dob = new DateOnly(1996, 6, 17), Gender = Gender.Male },
            new { Name = "Hồ Quang Hiếu", Phone = "0983000009", Email = "patient.09@cliniccare.local", Dob = new DateOnly(1989, 8, 24), Gender = Gender.Male },
            new { Name = "Ngô Phương Trinh", Phone = "0983000010", Email = "patient.10@cliniccare.local", Dob = new DateOnly(1994, 2, 14), Gender = Gender.Female },
            new { Name = "Dương Quốc Cường", Phone = "0983000011", Email = "patient.11@cliniccare.local", Dob = new DateOnly(1975, 10, 3), Gender = Gender.Male },
            new { Name = "Lý Tiểu Long", Phone = "0983000012", Email = "patient.12@cliniccare.local", Dob = new DateOnly(2003, 9, 9), Gender = Gender.Male },
            new { Name = "Trần Đăng Khoa", Phone = "0983000013", Email = "patient.13@cliniccare.local", Dob = new DateOnly(1970, 6, 28), Gender = Gender.Male },
            new { Name = "Nguyễn Thị Hoa", Phone = "0983000014", Email = "patient.14@cliniccare.local", Dob = new DateOnly(1987, 12, 1), Gender = Gender.Female },
            new { Name = "Phan Anh Tuấn", Phone = "0983000015", Email = "patient.15@cliniccare.local", Dob = new DateOnly(1998, 4, 18), Gender = Gender.Male }
        };

        var patUsers = new List<ApplicationUser>();
        foreach (var pdef in patientDefs)
        {
            var u = await SeedUserAsync(userManager, pdef.Email, pdef.Name, pdef.Phone, "Patient", password);
            patUsers.Add(u);
        }

        // 2. 11 Specialties with clean UTF-8
        var specialtyDefs = new[]
        {
            new Specialty { SpecialtyCode = "SP01", Name = "Nội tổng quát", Description = "Khám và điều trị các bệnh lý nội khoa chung (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 150000m },
            new Specialty { SpecialtyCode = "SP02", Name = "Nhi khoa", Description = "Khám, chẩn đoán và điều trị bệnh cho trẻ em (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 180000m },
            new Specialty { SpecialtyCode = "SP03", Name = "Sản phụ khoa", Description = "Khám thai định kỳ và tư vấn sức khỏe phụ khoa (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 200000m },
            new Specialty { SpecialtyCode = "SP04", Name = "Da liễu", Description = "Chuyên trị các vấn đề về da, tóc và móng (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 200000m },
            new Specialty { SpecialtyCode = "SP05", Name = "Tai mũi họng", Description = "Khám và điều trị bệnh lý tai mũi họng (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 180000m },
            new Specialty { SpecialtyCode = "SP06", Name = "Tim mạch", Description = "Kiểm tra huyết áp, đo điện tâm đồ và bệnh lý tim mạch (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 250000m },
            new Specialty { SpecialtyCode = "SP07", Name = "Cơ xương khớp", Description = "Điều trị viêm khớp, thoái hóa khớp và các chấn thương (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 220000m },
            new Specialty { SpecialtyCode = "SP08", Name = "Thần kinh", Description = "Khám và điều trị các bệnh lý thần kinh và đau đầu (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 250000m },
            new Specialty { SpecialtyCode = "SP09", Name = "Nội tiết", Description = "Khám và điều trị bệnh lý tiểu đường, tuyến giáp và rối loạn nội tiết (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 220000m },
            new Specialty { SpecialtyCode = "SP10", Name = "Nhãn khoa", Description = "Khám và điều trị các bệnh lý về mắt và thị lực (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 180000m },
            new Specialty { SpecialtyCode = "SP11", Name = "Tiêu hóa", Description = "Khám và điều trị các bệnh lý dạ dày, đại tràng và tiêu hóa (Dữ liệu demo)", IsActive = true, AiEnabled = true, ConsultationFee = 200000m }
        };
        foreach (var sdef in specialtyDefs)
        {
            var existingSpec = await db.Specialties.FirstOrDefaultAsync(s => s.SpecialtyCode == sdef.SpecialtyCode);
            if (existingSpec == null)
            {
                db.Specialties.Add(sdef);
            }
            else
            {
                existingSpec.Name = sdef.Name;
                existingSpec.Description = sdef.Description;
                existingSpec.IsActive = true;
                existingSpec.AiEnabled = true;
                if (existingSpec.ConsultationFee <= 0)
                {
                    existingSpec.ConsultationFee = sdef.ConsultationFee;
                }
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("11 Specialties seeded and synchronized with demo consultation fees.");
        var specs = await db.Specialties.ToListAsync();
        var specByCode = specs.ToDictionary(s => s.SpecialtyCode, s => s);

        // 3. Doctors & Specialty Bindings (Exact mapping by Email and SpecialtyCode)
        var doctors = new List<Doctor>();
        for (int i = 0; i < doctorDefs.Length; i++)
        {
            var def = doctorDefs[i];
            var docUser = docUsers[i];
            var d = await db.Doctors.Include(x => x.DoctorSpecialties).FirstOrDefaultAsync(x => x.UserId == docUser.Id);
            if (d == null)
            {
                d = new Doctor
                {
                    UserId = docUser.Id,
                    IsActive = true,
                    ExperienceYears = def.Exp,
                    AcademicTitle = def.Title,
                    Description = $"Bác sĩ chuyên khoa tại ClinicCare AI với {def.Exp} năm kinh nghiệm chuyên môn."
                };
                db.Doctors.Add(d);
                await db.SaveChangesAsync();
            }
            else
            {
                d.IsActive = true;
                d.ExperienceYears = def.Exp;
                d.AcademicTitle = def.Title;
                if (string.IsNullOrWhiteSpace(d.Description))
                {
                    d.Description = $"Bác sĩ chuyên khoa tại ClinicCare AI với {def.Exp} năm kinh nghiệm chuyên môn.";
                }
            }

            // Sync exact specialties
            var primarySpec = specByCode[def.PrimaryCode];
            var secondarySpec = def.SecondaryCode != null && specByCode.ContainsKey(def.SecondaryCode) ? specByCode[def.SecondaryCode] : null;

            var currentBindings = await db.DoctorSpecialties.Where(ds => ds.DoctorId == d.Id).ToListAsync();

            // Primary
            var primaryBinding = currentBindings.FirstOrDefault(ds => ds.SpecialtyId == primarySpec.Id);
            if (primaryBinding == null)
            {
                db.DoctorSpecialties.Add(new DoctorSpecialty
                {
                    DoctorId = d.Id,
                    SpecialtyId = primarySpec.Id,
                    IsPrimary = true
                });
            }
            else
            {
                primaryBinding.IsPrimary = true;
            }

            // Secondary
            if (secondarySpec != null)
            {
                var secondaryBinding = currentBindings.FirstOrDefault(ds => ds.SpecialtyId == secondarySpec.Id);
                if (secondaryBinding == null)
                {
                    db.DoctorSpecialties.Add(new DoctorSpecialty
                    {
                        DoctorId = d.Id,
                        SpecialtyId = secondarySpec.Id,
                        IsPrimary = false
                    });
                }
                else
                {
                    secondaryBinding.IsPrimary = false;
                }
            }

            // Remove any other invalid bindings for this doctor
            var allowedSpecIds = new HashSet<long> { primarySpec.Id };
            if (secondarySpec != null) allowedSpecIds.Add(secondarySpec.Id);

            foreach (var b in currentBindings)
            {
                if (!allowedSpecIds.Contains(b.SpecialtyId))
                {
                    db.DoctorSpecialties.Remove(b);
                }
            }

            doctors.Add(d);
        }
        await db.SaveChangesAsync();
        logger.LogInformation("10 Doctors and exact specialty mappings synchronized.");

        // Fix any existing appointments where SpecialtyId is not one of doctor's specialties
        var allAppts = await db.Appointments.Include(a => a.Doctor).ThenInclude(d => d.DoctorSpecialties).ToListAsync();
        foreach (var appt in allAppts)
        {
            if (appt.Doctor?.DoctorSpecialties != null)
            {
                var validSpecialties = appt.Doctor.DoctorSpecialties.Select(ds => ds.SpecialtyId).ToList();
                if (!validSpecialties.Contains(appt.SpecialtyId))
                {
                    var docPrimary = appt.Doctor.DoctorSpecialties.FirstOrDefault(ds => ds.IsPrimary) 
                        ?? appt.Doctor.DoctorSpecialties.FirstOrDefault();
                    if (docPrimary != null)
                    {
                        appt.SpecialtyId = docPrimary.SpecialtyId;
                    }
                }
            }
        }
        await db.SaveChangesAsync();

        // 4. Patients
        for (int i = 0; i < patUsers.Count; i++)
        {
            var pdef = patientDefs[i];
            if (!await db.Patients.AnyAsync(p => p.UserId == patUsers[i].Id))
            {
                db.Patients.Add(new Patient
                {
                    UserId = patUsers[i].Id,
                    DateOfBirth = pdef.Dob,
                    Gender = pdef.Gender
                });
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Patients seeded.");
        var patients = await db.Patients.ToListAsync();

        // 5. Clinic Locations
        var locationDefs = new[]
        {
            new ClinicLocation
            {
                Code = "CS01",
                Name = "Cơ sở 1 - Quận 1 (Trụ sở chính)",
                Address = "123 Nguyễn Thị Minh Khai, Phường Bến Thành, Quận 1",
                City = "TP. Hồ Chí Minh",
                Phone = "028 3930 1234",
                OpeningHours = "07:30 - 17:30 (Thứ 2 - Thứ 7) | 07:30 - 11:30 (Chủ nhật)",
                Description = "Phòng khám đa khoa hiện đại đầy đủ các chuyên khoa, trang thiết bị chẩn đoán hình ảnh tiên tiến.",
                ServicesJson = "[\"Khám tổng quát\",\"Chẩn đoán hình ảnh\",\"Xét nghiệm\",\"Nội soi tiêu hóa\",\"Nhà thuốc GPP\"]",
                IsActive = true
            },
            new ClinicLocation
            {
                Code = "CS02",
                Name = "Cơ sở 2 - Quận 7",
                Address = "456 Nguyễn Lương Bằng, Phường Tân Phú, Quận 7",
                City = "TP. Hồ Chí Minh",
                Phone = "028 5412 5678",
                OpeningHours = "07:30 - 17:00 (Thứ 2 - Thứ 7) | Chủ nhật nghỉ",
                Description = "Cơ sở phía Nam thành phố, chuyên sâu Nhi khoa, Sản phụ khoa và Tim mạch can thiệp.",
                ServicesJson = "[\"Khám Nhi - Sản\",\"Xét nghiệm máu nhanh\",\"Siêu âm tim Doppler màu\",\"Nhà thuốc GPP\"]",
                IsActive = true
            },
            new ClinicLocation
            {
                Code = "CS03",
                Name = "Cơ sở 3 - Thành phố Thủ Đức",
                Address = "789 Võ Văn Ngân, Phường Linh Chiểu, TP. Thủ Đức",
                City = "TP. Hồ Chí Minh",
                Phone = "028 3896 9012",
                OpeningHours = "07:30 - 17:00 (Thứ 2 - Thứ 7) | Chủ nhật nghỉ",
                Description = "Cơ sở khu vực phía Đông, thuận tiện cho sinh viên, kỹ sư và cư dân khu công nghệ cao.",
                ServicesJson = "[\"Khám nội tổng quát\",\"Da liễu & Thẩm mỹ y khoa\",\"Cơ xương khớp\",\"X-quang kỹ thuật số\"]",
                IsActive = true
            }
        };
        foreach (var ldef in locationDefs)
        {
            if (!await db.ClinicLocations.AnyAsync(l => l.Code == ldef.Code))
            {
                db.ClinicLocations.Add(ldef);
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Clinic locations seeded.");

        // 6. Doctor Work Schedules & Slots (2-shift model: 08:00–11:30 and 13:30–17:00 for today + next 30 days, skipping Sundays)
        var today = dateTimeProvider.VietnamToday;
        var existingSchedules = await db.DoctorWorkSchedules.ToListAsync();
        var scheduleLookup = existingSchedules
            .GroupBy(s => (s.DoctorId, s.WorkDate, s.StartTime, s.EndTime))
            .ToDictionary(g => g.Key, g => g.First());

        var existingSlots = await db.AppointmentSlots.ToListAsync();
        var slotLookup = existingSlots
            .GroupBy(s => (s.DoctorId, s.SlotDate, s.StartTime))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var doc in doctors)
        {
            for (int i = 0; i < 30; i++)
            {
                var date = today.AddDays(i);
                // Skip Sundays
                if (date.DayOfWeek == DayOfWeek.Sunday) continue;

                // Morning Shift (08:00 - 11:30)
                var morningKey = (doc.Id, date, new TimeOnly(8, 0), new TimeOnly(11, 30));
                if (!scheduleLookup.TryGetValue(morningKey, out var morningSchedule))
                {
                    morningSchedule = new DoctorWorkSchedule
                    {
                        DoctorId = doc.Id,
                        WorkDate = date,
                        StartTime = new TimeOnly(8, 0),
                        EndTime = new TimeOnly(11, 30),
                        IsActive = true
                    };
                    db.DoctorWorkSchedules.Add(morningSchedule);
                    scheduleLookup[morningKey] = morningSchedule;
                }
                else if (!morningSchedule.IsActive)
                {
                    morningSchedule.IsActive = true;
                }

                // Afternoon Shift (13:30 - 17:00)
                var afternoonKey = (doc.Id, date, new TimeOnly(13, 30), new TimeOnly(17, 0));
                if (!scheduleLookup.TryGetValue(afternoonKey, out var afternoonSchedule))
                {
                    afternoonSchedule = new DoctorWorkSchedule
                    {
                        DoctorId = doc.Id,
                        WorkDate = date,
                        StartTime = new TimeOnly(13, 30),
                        EndTime = new TimeOnly(17, 0),
                        IsActive = true
                    };
                    db.DoctorWorkSchedules.Add(afternoonSchedule);
                    scheduleLookup[afternoonKey] = afternoonSchedule;
                }
                else if (!afternoonSchedule.IsActive)
                {
                    afternoonSchedule.IsActive = true;
                }

                // Morning slots (7 slots: 08:00 to 11:30, 30m each)
                var mStart = new TimeOnly(8, 0);
                for (int s = 0; s < 7; s++)
                {
                    var slotKey = (doc.Id, date, mStart);
                    if (!slotLookup.TryGetValue(slotKey, out var slot))
                    {
                        slot = new AppointmentSlot
                        {
                            DoctorId = doc.Id,
                            SlotDate = date,
                            StartTime = mStart,
                            EndTime = mStart.AddMinutes(30),
                            IsBooked = false
                        };
                        db.AppointmentSlots.Add(slot);
                        slotLookup[slotKey] = slot;
                    }
                    mStart = mStart.AddMinutes(30);
                }

                // Afternoon slots (7 slots: 13:30 to 17:00, 30m each)
                var aStart = new TimeOnly(13, 30);
                for (int s = 0; s < 7; s++)
                {
                    var slotKey = (doc.Id, date, aStart);
                    if (!slotLookup.TryGetValue(slotKey, out var slot))
                    {
                        slot = new AppointmentSlot
                        {
                            DoctorId = doc.Id,
                            SlotDate = date,
                            StartTime = aStart,
                            EndTime = aStart.AddMinutes(30),
                            IsBooked = false
                        };
                        db.AppointmentSlots.Add(slot);
                        slotLookup[slotKey] = slot;
                    }
                    aStart = aStart.AddMinutes(30);
                }
            }
        }
        await db.SaveChangesAsync();

        // Synchronize IsBooked on all slots based on active appointments holding slots
        var activeApptSlotIds = await db.Appointments
            .Where(a => AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
            .Select(a => a.AppointmentSlotId)
            .Distinct()
            .ToListAsync();
        var activeSlotIdSet = new HashSet<long>(activeApptSlotIds);

        var allDoctorSlots = await db.AppointmentSlots.ToListAsync();
        foreach (var slot in allDoctorSlots)
        {
            var shouldBeBooked = activeSlotIdSet.Contains(slot.Id);
            if (slot.IsBooked != shouldBeBooked)
            {
                slot.IsBooked = shouldBeBooked;
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Work schedules (30 days [VietnamToday..+29], 2 shifts) and slots (14 slots/day) seeded and synchronized.");
        logger.LogInformation("NOTE: All doctor profiles and medical records seeded are demo/mock data for development purposes only.");

        // 7. Appointments & Histories
        if (!await db.Appointments.AnyAsync(a => a.AppointmentCode.StartsWith("DEMO-")))
        {
            var slots = await db.AppointmentSlots.ToListAsync();
            var mainDoc = doctors.FirstOrDefault(d => d.UserId == docUsers[0].Id) ?? doctors[0];
            var mainDocSpecId = mainDoc.DoctorSpecialties.FirstOrDefault(ds => ds.IsPrimary)?.SpecialtyId ?? specs[0].Id;
            var aptCount = 0;

            async Task CreateApt(Patient p, Doctor d, AppointmentSlot s, AppointmentStatus status, string reason)
            {
                if (s.DoctorId != d.Id)
                {
                    throw new InvalidOperationException($"Appointment slot doctor {s.DoctorId} must match appointment doctor {d.Id}");
                }

                var docSpecId = d.DoctorSpecialties.FirstOrDefault(ds => ds.IsPrimary)?.SpecialtyId 
                    ?? d.DoctorSpecialties.FirstOrDefault()?.SpecialtyId 
                    ?? specs[0].Id;
                var code = $"DEMO-{DateTime.Now.Ticks % 100000:D5}-{aptCount++:D2}";
                var a = new Appointment
                {
                    AppointmentCode = code,
                    PatientId = p.Id,
                    DoctorId = d.Id,
                    SpecialtyId = docSpecId,
                    AppointmentSlotId = s.Id,
                    AppointmentDate = s.SlotDate,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    Reason = reason,
                    Status = status
                };
                db.Appointments.Add(a);
                if (status != AppointmentStatus.Cancelled)
                {
                    s.IsBooked = true;
                }
                await db.SaveChangesAsync();

                db.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = a.Id,
                    Action = AppointmentHistoryAction.Created,
                    NewStatus = AppointmentStatus.Pending,
                    Note = "Bệnh nhân tự đặt lịch hẹn qua ứng dụng",
                    PerformedByUserId = p.UserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-10)
                });

                if (status != AppointmentStatus.Pending && status != AppointmentStatus.Cancelled)
                {
                    db.AppointmentHistories.Add(new AppointmentHistory
                    {
                        AppointmentId = a.Id,
                        Action = AppointmentHistoryAction.Confirmed,
                        OldStatus = AppointmentStatus.Pending,
                        NewStatus = AppointmentStatus.Confirmed,
                        Note = "Lễ tân liên hệ và xác nhận lịch hẹn",
                        PerformedByUserId = adminId1.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-9)
                    });
                }

                if (status == AppointmentStatus.Completed)
                {
                    db.AppointmentHistories.Add(new AppointmentHistory
                    {
                        AppointmentId = a.Id,
                        Action = AppointmentHistoryAction.Completed,
                        OldStatus = AppointmentStatus.Confirmed,
                        NewStatus = AppointmentStatus.Completed,
                        Note = "Bác sĩ hoàn thành phiên khám",
                        PerformedByUserId = d.UserId,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    });
                    db.VisitSummaries.Add(new VisitSummary
                    {
                        AppointmentId = a.Id,
                        DoctorId = d.Id,
                        Summary = "Sức khỏe bệnh nhân tương đối ổn định. Đã kê đơn thuốc và tư vấn chế độ ăn uống, sinh hoạt lành mạnh.",
                        FollowUpInstruction = "Uống nhiều nước ấm, tập thể dục nhẹ nhàng 30 phút mỗi ngày. Tái khám sau 2 tuần nếu triệu chứng tái phát."
                    });
                }
                else if (status == AppointmentStatus.NoShow)
                {
                    db.AppointmentHistories.Add(new AppointmentHistory
                    {
                        AppointmentId = a.Id,
                        Action = AppointmentHistoryAction.NoShow,
                        OldStatus = AppointmentStatus.Confirmed,
                        NewStatus = AppointmentStatus.NoShow,
                        Note = "Bệnh nhân không có mặt tại phòng khám vào giờ hẹn",
                        PerformedByUserId = d.UserId,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    });
                }
                else if (status == AppointmentStatus.Cancelled)
                {
                    db.AppointmentHistories.Add(new AppointmentHistory
                    {
                        AppointmentId = a.Id,
                        Action = AppointmentHistoryAction.Cancelled,
                        OldStatus = AppointmentStatus.Pending,
                        NewStatus = AppointmentStatus.Cancelled,
                        Note = "Bệnh nhân thông báo bận việc đột xuất xin hủy lịch",
                        PerformedByUserId = p.UserId,
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    });
                }
                await db.SaveChangesAsync();
            }

            var futureSlots = slots.Where(x => x.SlotDate > today).ToList();

            // Historical past appointments (create past schedules & slots for realistic history)
            var pastDate = today.AddDays(-5);
            var pastSchedule = await db.DoctorWorkSchedules.FirstOrDefaultAsync(s => s.DoctorId == mainDoc.Id && s.WorkDate == pastDate);
            if (pastSchedule == null)
            {
                pastSchedule = new DoctorWorkSchedule
                {
                    DoctorId = mainDoc.Id,
                    WorkDate = pastDate,
                    StartTime = new TimeOnly(8, 0),
                    EndTime = new TimeOnly(17, 0),
                    IsActive = true
                };
                db.DoctorWorkSchedules.Add(pastSchedule);
                await db.SaveChangesAsync();
            }

            var pastSlots = await db.AppointmentSlots.Where(s => s.DoctorId == mainDoc.Id && s.SlotDate == pastDate).ToListAsync();
            if (pastSlots.Count < 15)
            {
                for (int i = pastSlots.Count; i < 15; i++)
                {
                    var sTime = new TimeOnly(8, 0).AddMinutes(i * 30);
                    if (!pastSlots.Any(x => x.StartTime == sTime))
                    {
                        var s = new AppointmentSlot
                        {
                            DoctorId = mainDoc.Id,
                            SlotDate = pastDate,
                            StartTime = sTime,
                            EndTime = sTime.AddMinutes(30),
                            IsBooked = true
                        };
                        pastSlots.Add(s);
                        db.AppointmentSlots.Add(s);
                    }
                }
                await db.SaveChangesAsync();
            }

            // 12 Completed
            for (int i = 0; i < 12; i++)
            {
                await CreateApt(patients[i % patients.Count], mainDoc, pastSlots[i % pastSlots.Count], AppointmentStatus.Completed, "Khám kiểm tra sức khỏe tổng quát định kỳ");
            }
            // 3 NoShow
            for (int i = 12; i < 15; i++)
            {
                await CreateApt(patients[i % patients.Count], mainDoc, pastSlots[i % pastSlots.Count], AppointmentStatus.NoShow, "Tư vấn sức khỏe định kỳ");
            }

            // Past cancelled
            var pastDate2 = today.AddDays(-4);
            var pastSchedule2 = await db.DoctorWorkSchedules.FirstOrDefaultAsync(s => s.DoctorId == mainDoc.Id && s.WorkDate == pastDate2);
            if (pastSchedule2 == null)
            {
                pastSchedule2 = new DoctorWorkSchedule
                {
                    DoctorId = mainDoc.Id,
                    WorkDate = pastDate2,
                    StartTime = new TimeOnly(8, 0),
                    EndTime = new TimeOnly(17, 0),
                    IsActive = true
                };
                db.DoctorWorkSchedules.Add(pastSchedule2);
                await db.SaveChangesAsync();
            }

            var pastSlots2 = await db.AppointmentSlots.Where(s => s.DoctorId == mainDoc.Id && s.SlotDate == pastDate2).ToListAsync();
            if (pastSlots2.Count < 5)
            {
                for (int i = pastSlots2.Count; i < 5; i++)
                {
                    var sTime = new TimeOnly(8, 0).AddMinutes(i * 30);
                    if (!pastSlots2.Any(x => x.StartTime == sTime))
                    {
                        var s = new AppointmentSlot
                        {
                            DoctorId = mainDoc.Id,
                            SlotDate = pastDate2,
                            StartTime = sTime,
                            EndTime = sTime.AddMinutes(30),
                            IsBooked = false
                        };
                        pastSlots2.Add(s);
                        db.AppointmentSlots.Add(s);
                    }
                }
                await db.SaveChangesAsync();
            }

            for (int i = 0; i < 5; i++)
            {
                await CreateApt(patients[i % patients.Count], mainDoc, pastSlots2[i % pastSlots2.Count], AppointmentStatus.Cancelled, "Tái khám theo hẹn của bác sĩ");
            }

            // Appointments for today for mainDoc (Dr. Khai) for immediate dashboard/workspace testing
            var todaySlots = await db.AppointmentSlots
                .Where(s => s.DoctorId == mainDoc.Id && s.SlotDate == today && !s.IsBooked)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            if (todaySlots.Count >= 3)
            {
                await CreateApt(patients[0], mainDoc, todaySlots[0], AppointmentStatus.Confirmed, "Kiểm tra định kỳ huyết áp và đường huyết sáng nay");
                await CreateApt(patients[1], mainDoc, todaySlots[1], AppointmentStatus.Confirmed, "Tái khám viêm họng hạt và sốt nhẹ");
                await CreateApt(patients[2], mainDoc, todaySlots[2], AppointmentStatus.Confirmed, "Tư vấn dinh dưỡng và chăm sóc sức khỏe");
            }

            // Future appointments with strict slot & specialty doctor integrity
            // 6 Pending
            for (int i = 0; i < 6; i++)
            {
                var doc = doctors[i % doctors.Count];
                var docSlot = futureSlots.FirstOrDefault(s => s.DoctorId == doc.Id && !s.IsBooked);
                if (docSlot != null)
                {
                    await CreateApt(patients[i % patients.Count], doc, docSlot, AppointmentStatus.Pending, "Đau đầu kéo dài kèm mệt mỏi nhẹ");
                }
            }
            // 10 Confirmed
            for (int i = 6; i < 16; i++)
            {
                var doc = doctors[(i + 1) % doctors.Count];
                var docSlot = futureSlots.FirstOrDefault(s => s.DoctorId == doc.Id && !s.IsBooked);
                if (docSlot != null)
                {
                    await CreateApt(patients[i % patients.Count], doc, docSlot, AppointmentStatus.Confirmed, "Kiểm tra huyết áp và tư vấn lối sống");
                }
            }

            logger.LogInformation("Appointments seeded.");

            // Change requests
            if (!await db.AppointmentChangeRequests.AnyAsync())
            {
                var pendingAppt = await db.Appointments.Include(a => a.Patient).FirstOrDefaultAsync(a => a.Status == AppointmentStatus.Pending);
                if (pendingAppt != null)
                {
                    db.AppointmentChangeRequests.Add(new AppointmentChangeRequest
                    {
                        AppointmentId = pendingAppt.Id,
                        RequestType = AppointmentChangeRequestType.Cancellation,
                        Reason = "Bận chuyến công tác đột xuất tại Hà Nội",
                        Status = AppointmentChangeRequestStatus.Pending,
                        RequestedByUserId = pendingAppt.Patient.UserId,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                var confAppt = await db.Appointments.Include(a => a.Patient).Skip(1).FirstOrDefaultAsync(a => a.Status == AppointmentStatus.Confirmed);
                if (confAppt != null)
                {
                    var rescheduleSlot = futureSlots.FirstOrDefault(s => s.DoctorId == confAppt.DoctorId && !s.IsBooked);
                    db.AppointmentChangeRequests.Add(new AppointmentChangeRequest
                    {
                        AppointmentId = confAppt.Id,
                        RequestType = AppointmentChangeRequestType.Reschedule,
                        RequestedSlotId = rescheduleSlot?.Id ?? confAppt.AppointmentSlotId,
                        Reason = "Xin dời ngày khám sang tuần sau vì việc gia đình",
                        Status = AppointmentChangeRequestStatus.Pending,
                        RequestedByUserId = confAppt.Patient.UserId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync();
            }

            // Revisit request
            if (!await db.RevisitRequests.AnyAsync())
            {
                var compAppt = await db.Appointments.FirstOrDefaultAsync(a => a.Status == AppointmentStatus.Completed);
                if (compAppt != null)
                {
                    db.RevisitRequests.Add(new RevisitRequest
                    {
                        AppointmentId = compAppt.Id,
                        PatientId = compAppt.PatientId,
                        DoctorId = compAppt.DoctorId,
                        SuggestedDate = today.AddDays(7),
                        Note = "Tái khám sau 1 tuần để kiểm tra lại đường huyết và men gan",
                        Status = RevisitRequestStatus.PendingPatientResponse
                    });
                    await db.SaveChangesAsync();
                }
            }

            // Leave requests
            if (!await db.DoctorLeaveRequests.AnyAsync())
            {
                db.DoctorLeaveRequests.Add(new DoctorLeaveRequest
                {
                    DoctorId = mainDoc.Id,
                    StartDateTime = DateTime.UtcNow.AddDays(2),
                    EndDateTime = DateTime.UtcNow.AddDays(3),
                    Reason = "Nghỉ phép cá nhân giải quyết việc gia đình",
                    Status = DoctorLeaveRequestStatus.Pending
                });
                db.DoctorLeaveRequests.Add(new DoctorLeaveRequest
                {
                    DoctorId = doctors[1].Id,
                    StartDateTime = DateTime.UtcNow.AddDays(5),
                    EndDateTime = DateTime.UtcNow.AddDays(6),
                    Reason = "Tham gia hội thảo chuyên ngành tại Hà Nội",
                    Status = DoctorLeaveRequestStatus.Approved,
                    AdminNote = "Đã duyệt, yêu cầu chuyển giao ca trực và hỗ trợ bệnh nhân ngày 5."
                });
                await db.SaveChangesAsync();
            }
            logger.LogInformation("Requests and Leaves seeded.");
        }

        // 8. Health Packages
        var packageDefs = new[]
        {
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
        };
        foreach (var pdef in packageDefs)
        {
            if (!await db.HealthPackages.AnyAsync(p => p.Code == pdef.Code))
            {
                db.HealthPackages.Add(pdef);
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("HealthPackages seeded.");
        var packages = await db.HealthPackages.ToListAsync();

        // 9. Health Package Registrations
        if (!await db.HealthPackageRegistrations.AnyAsync(r => r.RegistrationCode.StartsWith("PKG-REG-")) && packages.Count > 0 && patients.Count > 0)
        {
            db.HealthPackageRegistrations.AddRange(
                new HealthPackageRegistration
                {
                    RegistrationCode = "PKG-REG-001",
                    PatientId = patients[0].Id,
                    HealthPackageId = packages[0].Id,
                    PreferredDate = today.AddDays(3),
                    ContactPhone = "0900000004",
                    Note = "Ưu tiên khám buổi sáng sớm trước 9h",
                    Status = HealthPackageRegistrationStatus.Pending,
                    CreatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new HealthPackageRegistration
                {
                    RegistrationCode = "PKG-REG-002",
                    PatientId = patients[1 % patients.Count].Id,
                    HealthPackageId = packages[1 % packages.Count].Id,
                    PreferredDate = today.AddDays(5),
                    ContactPhone = "0983000001",
                    Note = "Có tiền sử tăng huyết áp gia đình",
                    AdminNotes = "Đã liên hệ xác nhận hẹn lịch vào 8h30 ngày hẹn",
                    Status = HealthPackageRegistrationStatus.Confirmed,
                    CreatedAt = DateTime.UtcNow.AddDays(-3),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new HealthPackageRegistration
                {
                    RegistrationCode = "PKG-REG-003",
                    PatientId = patients[2 % patients.Count].Id,
                    HealthPackageId = packages[3 % packages.Count].Id,
                    PreferredDate = today.AddDays(2),
                    ContactPhone = "0983000002",
                    Note = "Khám tư vấn định kỳ",
                    CancellationReason = "Khách hàng bận lịch công tác không đến được",
                    Status = HealthPackageRegistrationStatus.Cancelled,
                    CreatedAt = DateTime.UtcNow.AddDays(-4),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                }
            );
            await db.SaveChangesAsync();
            logger.LogInformation("HealthPackageRegistrations seeded.");
        }

        // 10. Medicines & Pharmacy Seed
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
            if (existingByPhone != null)
            {
                existingByPhone.UserName = email;
                existingByPhone.Email = email;
                existingByPhone.FullName = name;
                existingByPhone.IsActive = true;
                await userManager.UpdateAsync(existingByPhone);
                if (!await userManager.IsInRoleAsync(existingByPhone, role))
                {
                    await userManager.AddToRoleAsync(existingByPhone, role);
                }
                return existingByPhone;
            }

            user = new ApplicationUser { UserName = email, Email = email, FullName = name, PhoneNumber = phone, IsActive = true };
            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, role);
        }
        else
        {
            user.FullName = name;
            user.PhoneNumber = phone;
            user.IsActive = true;
            await userManager.UpdateAsync(user);
            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }
        return user;
    }
}
