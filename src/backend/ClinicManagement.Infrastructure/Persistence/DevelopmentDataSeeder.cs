using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ClinicManagement.Infrastructure.Persistence;

public class DevelopmentDataSeederOptions
{
    public bool SeedDoctorSchedulesAndSlots { get; set; } = false;
    public int ScheduleDays { get; set; } = 7;
    public bool SeedDemoAppointments { get; set; } = false;
}

public static class DevelopmentDataSeeder
{
    public static Task SeedMedicineCatalogAsync(AppDbContext db) => MedicineCatalogDevelopmentSeed.SeedAsync(db);

    public static async Task SeedDiagnosticParametersAsync(AppDbContext db)
    {
        // Khoảng tham chiếu minh họa, không dùng cho chẩn đoán thật
        var definitions = new (string Service, string Code, string Name, string Unit, decimal? Low,
            decimal? High, decimal? FemaleLow, decimal? FemaleHigh, decimal? CriticalLow, decimal? CriticalHigh,
            decimal? DeltaAbs, decimal? DeltaPct, int? DeltaDays)[]
        {
            ("LAB-CBC", "WBC", "Bạch cầu", "10^9/L", 4m, 10m, null, null, 2m, 30m, null, 50m, 14),
            ("LAB-CBC", "RBC", "Hồng cầu", "10^12/L", 4.5m, 5.9m, 4m, 5.2m, null, null, null, null, null),
            ("LAB-CBC", "HGB", "Huyết sắc tố", "g/L", 130m, 170m, 120m, 150m, 70m, 200m, 20m, null, 14),
            ("LAB-CBC", "HCT", "Hematocrit", "%", 40m, 50m, 36m, 46m, null, null, 6m, null, 14),
            ("LAB-CBC", "PLT", "Tiểu cầu", "10^9/L", 150m, 400m, null, null, 50m, 1000m, null, 50m, 14),
            ("LAB-CBC", "NEU%", "Bạch cầu trung tính", "%", 40m, 75m, null, null, null, null, null, null, null),
            ("LAB-CBC", "LYM%", "Bạch cầu lympho", "%", 20m, 40m, null, null, null, null, null, null, null),
            ("LAB-GLU", "GLU", "Glucose máu lúc đói", "mmol/L", 3.9m, 6.4m, null, null, 2.8m, 22.2m, null, 50m, 30),
            ("LAB-LIPID", "CHOL", "Cholesterol toàn phần", "mmol/L", null, 5.2m, null, null, null, null, null, null, null),
            ("LAB-LIPID", "TG", "Triglycerid", "mmol/L", null, 1.7m, null, null, null, null, null, null, null),
            ("LAB-LIPID", "HDL", "HDL-C", "mmol/L", 1m, null, null, null, null, null, null, null, null),
            ("LAB-LIPID", "LDL", "LDL-C", "mmol/L", null, 3.4m, null, null, null, null, null, null, null),
            ("LAB-LFT", "AST", "AST", "U/L", null, 40m, null, null, null, 1000m, null, 100m, 30),
            ("LAB-LFT", "ALT", "ALT", "U/L", null, 40m, null, null, null, 1000m, null, 100m, 30),
            ("LAB-RFT", "URE", "Ure", "mmol/L", 2.5m, 7.5m, null, null, null, null, null, 50m, 30),
            ("LAB-RFT", "CREA", "Creatinin", "µmol/L", 62m, 106m, 44m, 80m, null, 700m, null, 50m, 30)
        };
        var codes = definitions.Select(d => d.Service).Distinct().ToList();
        var services = await db.DiagnosticServices.Where(s => codes.Contains(s.Code)).Include(s => s.Parameters).ToListAsync();
        foreach (var service in services)
        {
            var sortOrder = 0;
            foreach (var definition in definitions.Where(d => d.Service == service.Code))
            {
                var parameter = service.Parameters.FirstOrDefault(p => p.Code == definition.Code);
                if (parameter == null)
                {
                    parameter = new DiagnosticServiceParameter { Code = definition.Code };
                    service.Parameters.Add(parameter);
                }
                parameter.Name = definition.Name;
                parameter.Unit = definition.Unit;
                parameter.RefLow = definition.Low;
                parameter.RefHigh = definition.High;
                parameter.RefLowFemale = definition.FemaleLow;
                parameter.RefHighFemale = definition.FemaleHigh;
                parameter.CriticalLow = definition.CriticalLow;
                parameter.CriticalHigh = definition.CriticalHigh;
                parameter.DeltaAbsoluteLimit = definition.DeltaAbs;
                parameter.DeltaPercentLimit = definition.DeltaPct;
                parameter.DeltaWindowDays = definition.DeltaDays;
                parameter.SortOrder = sortOrder++;
                parameter.IsActive = true;
            }
        }
        await db.SaveChangesAsync();
    }
    public static async Task SeedAsync(IServiceProvider serviceProvider, DevelopmentDataSeederOptions? options = null)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevelopmentDataSeeder");
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = serviceProvider.GetRequiredService<AppDbContext>();
        var dateTimeProvider = serviceProvider.GetService<ClinicManagement.Application.Common.Interfaces.IDateTimeProvider>()
            ?? new ClinicManagement.Infrastructure.Services.DateTimeProvider();

        var seedTime = RealisticDevelopmentSeed.At(dateTimeProvider, dateTimeProvider.VietnamToday, 9);
        var config = serviceProvider.GetService<IConfiguration>();
        var seedSchedules = options?.SeedDoctorSchedulesAndSlots
            ?? config?.GetValue<bool>("DemoSeed:GenerateSlots", false)
            ?? false;

        var scheduleDays = options?.ScheduleDays
            ?? config?.GetValue<int?>("DemoSeed:ScheduleDays")
            ?? 7;

        var seedAppointments = options?.SeedDemoAppointments
            ?? (seedSchedules && (config?.GetValue<bool>("DemoSeed:GenerateAppointments", false) ?? false));

        logger.LogInformation("Starting Development Data Seeding (Schedules/Slots: {SeedSchedules}, Demo Appointments: {SeedAppointments})...", seedSchedules, seedAppointments);

        var password = "Demo@12345";

        // 1. Seed Users
        var adminId1 = await SeedUserAsync(userManager, "admin@cliniccare.local", "Nguyễn Hoàng Phúc", "0999999999", "Admin", password, dateTimeProvider);
        var adminId2 = await SeedUserAsync(userManager, "admin.02@cliniccare.local", "Trần Quốc Huy", "0980000002", "Admin", password, dateTimeProvider);

        var pharmacist = await SeedUserAsync(userManager, "pharmacist@cliniccare.local", "Lê Minh Tâm", "0977777777", "Pharmacist", password, dateTimeProvider);
        var technician = await SeedUserAsync(userManager, "technician@cliniccare.local", "Nguyễn Thị Ánh", "0966666666", ClinicManagement.Application.Common.Constants.RoleNames.DiagnosticTechnician, password, dateTimeProvider);
        var labSupervisor = await SeedUserAsync(userManager, "labsupervisor@cliniccare.local", "Đỗ Thị Lan", "0966666601", ClinicManagement.Application.Common.Constants.RoleNames.LabSupervisor, password, dateTimeProvider);
        var technician02 = await SeedUserAsync(userManager, "technician.02@cliniccare.local", "Phạm Quốc Bảo", "0966666602", ClinicManagement.Application.Common.Constants.RoleNames.DiagnosticTechnician, password, dateTimeProvider);

        var rec1 = await SeedUserAsync(userManager, "reception@cliniccare.local", "Nguyễn Thu Trang", "0900000002", "Receptionist", password, dateTimeProvider);
        var rec2 = await SeedUserAsync(userManager, "letan.02@cliniccare.local", "Trần Mai Anh", "0981000002", "Receptionist", password, dateTimeProvider);
        var rec3 = await SeedUserAsync(userManager, "letan.03@cliniccare.local", "Lê Hoàng Yến", "0981000003", "Receptionist", password, dateTimeProvider);

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
            var u = await SeedUserAsync(userManager, def.Email, def.Name, def.Phone, "Doctor", password, dateTimeProvider);
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
            var u = await SeedUserAsync(userManager, pdef.Email, pdef.Name, pdef.Phone, "Patient", password, dateTimeProvider);
            patUsers.Add(u);
        }

        // 2. 11 Specialties with clean UTF-8
        var specialtyDefs = new[]
        {
            new Specialty { SpecialtyCode = "SP01", Name = "Nội tổng quát", Description = "Khám và điều trị các bệnh lý nội khoa chung", IsActive = true, AiEnabled = true, ConsultationFee = 150000m },
            new Specialty { SpecialtyCode = "SP02", Name = "Nhi khoa", Description = "Khám, chẩn đoán và điều trị bệnh cho trẻ em", IsActive = true, AiEnabled = true, ConsultationFee = 180000m },
            new Specialty { SpecialtyCode = "SP03", Name = "Sản phụ khoa", Description = "Khám thai định kỳ và tư vấn sức khỏe phụ khoa", IsActive = true, AiEnabled = true, ConsultationFee = 200000m },
            new Specialty { SpecialtyCode = "SP04", Name = "Da liễu", Description = "Chuyên trị các vấn đề về da, tóc và móng", IsActive = true, AiEnabled = true, ConsultationFee = 200000m },
            new Specialty { SpecialtyCode = "SP05", Name = "Tai mũi họng", Description = "Khám và điều trị bệnh lý tai mũi họng", IsActive = true, AiEnabled = true, ConsultationFee = 180000m },
            new Specialty { SpecialtyCode = "SP06", Name = "Tim mạch", Description = "Kiểm tra huyết áp, đo điện tâm đồ và bệnh lý tim mạch", IsActive = true, AiEnabled = true, ConsultationFee = 250000m },
            new Specialty { SpecialtyCode = "SP07", Name = "Cơ xương khớp", Description = "Điều trị viêm khớp, thoái hóa khớp và các chấn thương", IsActive = true, AiEnabled = true, ConsultationFee = 220000m },
            new Specialty { SpecialtyCode = "SP08", Name = "Thần kinh", Description = "Khám và điều trị các bệnh lý thần kinh và đau đầu", IsActive = true, AiEnabled = true, ConsultationFee = 250000m },
            new Specialty { SpecialtyCode = "SP09", Name = "Nội tiết", Description = "Khám và điều trị bệnh lý tiểu đường, tuyến giáp và rối loạn nội tiết", IsActive = true, AiEnabled = true, ConsultationFee = 220000m },
            new Specialty { SpecialtyCode = "SP10", Name = "Nhãn khoa", Description = "Khám và điều trị các bệnh lý về mắt và thị lực", IsActive = true, AiEnabled = true, ConsultationFee = 180000m },
            new Specialty { SpecialtyCode = "SP11", Name = "Tiêu hóa", Description = "Khám và điều trị các bệnh lý dạ dày, đại tràng và tiêu hóa", IsActive = true, AiEnabled = true, ConsultationFee = 200000m }
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
        var mrnGenerator = serviceProvider.GetService<ClinicManagement.Application.Mpi.Interfaces.IMrnGenerator>();
        for (int i = 0; i < patUsers.Count; i++)
        {
            var pdef = patientDefs[i];
            if (!await db.Patients.AnyAsync(p => p.UserId == patUsers[i].Id))
            {
                var mrn = mrnGenerator != null
                    ? await mrnGenerator.GenerateNextMrnAsync()
                    : $"BN-{seedTime.Year}-{(1000 + i + 1):D6}";

                db.Patients.Add(new Patient
                {
                    UserId = patUsers[i].Id,
                    FullName = pdef.Name,
                    PhoneNumber = pdef.Phone,
                    Email = pdef.Email,
                    MedicalRecordNumber = mrn,
                    DateOfBirth = pdef.Dob,
                    Gender = pdef.Gender
                });
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Patients seeded.");
        await DemoPatientDevelopmentSeed.SeedAsync(db, dateTimeProvider, mrnGenerator, userManager, password);
        var patients = await db.Patients.ToListAsync();

        // 5. Clinic Locations
        await ClinicBrandDevelopmentSeed.SeedLocationAsync(db);

        await DevelopmentAppointmentSeed.NormalizeLegacyCodesAsync(db, dateTimeProvider, adminId1.Id);
        var today = dateTimeProvider.VietnamToday;

        // 6 & 7. Doctor Work Schedules, Slots & Demo Appointments (Decoupled from baseline master seed)
        if (seedSchedules)
        {
            await SeedDoctorSchedulesAndSlotsInternalAsync(db, doctors, dateTimeProvider, logger, scheduleDays);

            if (seedAppointments)
            {
                await DevelopmentAppointmentSeed.SeedAsync(db, doctors, docUsers, adminId1, patients, specs, dateTimeProvider, logger);
            }
            else
            {
                logger.LogInformation("Demo appointments skipped (not requested).");
            }
        }
        else
        {
            logger.LogInformation("Automatic schedule & slot generation skipped (baseline master seed). Configure 'DemoSeed:GenerateSlots' or invoke SeedDoctorSchedulesAndSlotsAsync to generate slots.");
        }

        // 8. Health Packages
        await ClinicBrandDevelopmentSeed.SeedPackagesAsync(db);
        var packages = await db.HealthPackages.ToListAsync();

        // 9. Health Package Registrations
        if (!await db.HealthPackageRegistrations.AnyAsync(r => r.RegistrationCode.StartsWith("REG-")) && packages.Count > 0 && patients.Count > 0)
        {
            db.HealthPackageRegistrations.AddRange(
                new HealthPackageRegistration
                {
                    RegistrationCode = ClinicManagement.Infrastructure.Common.BusinessDisplay.RandomCode("REG", today.AddDays(-2), 7),
                    PatientId = patients[0].Id,
                    HealthPackageId = packages[0].Id,
                    PreferredDate = today.AddDays(3),
                    ContactPhone = "0900000004",
                    Note = "Ưu tiên khám buổi sáng sớm trước 9h",
                    Status = HealthPackageRegistrationStatus.Pending,
                    CreatedAt = seedTime.AddDays(-2)
                },
                new HealthPackageRegistration
                {
                    RegistrationCode = ClinicManagement.Infrastructure.Common.BusinessDisplay.RandomCode("REG", today.AddDays(-3), 7),
                    PatientId = patients[1 % patients.Count].Id,
                    HealthPackageId = packages[1 % packages.Count].Id,
                    PreferredDate = today.AddDays(5),
                    ContactPhone = "0983000001",
                    Note = "Có tiền sử tăng huyết áp gia đình",
                    AdminNotes = "Đã liên hệ xác nhận hẹn lịch vào 8h30 ngày hẹn",
                    Status = HealthPackageRegistrationStatus.Confirmed,
                    CreatedAt = seedTime.AddDays(-3),
                    UpdatedAt = seedTime.AddDays(-2)
                },
                new HealthPackageRegistration
                {
                    RegistrationCode = ClinicManagement.Infrastructure.Common.BusinessDisplay.RandomCode("REG", today.AddDays(-4), 7),
                    PatientId = patients.First(p => p.Gender == Gender.Female).Id,
                    HealthPackageId = packages[3 % packages.Count].Id,
                    PreferredDate = today.AddDays(2),
                    ContactPhone = "0983000002",
                    Note = "Khám tư vấn định kỳ",
                    CancellationReason = "Khách hàng bận lịch công tác không đến được",
                    Status = HealthPackageRegistrationStatus.Cancelled,
                    CreatedAt = seedTime.AddDays(-4),
                    UpdatedAt = seedTime.AddDays(-1)
                }
            );
            await db.SaveChangesAsync();
            logger.LogInformation("HealthPackageRegistrations seeded.");
        }

        // 10. Medicines & Pharmacy Seed
        var seedPharmacyPrescriptions = !await db.Medicines.AnyAsync();
        await SeedMedicineCatalogAsync(db);
        if (seedPharmacyPrescriptions)
        {
            var med1 = await db.Medicines.SingleAsync(m => m.Code == "MED01");
            var med2 = await db.Medicines.SingleAsync(m => m.Code == "MED02");
            var med4 = await db.Medicines.SingleAsync(m => m.Code == "MED04");
            var med9 = await db.Medicines.SingleAsync(m => m.Code == "MED09");
            var med12 = await db.Medicines.SingleAsync(m => m.Code == "MED12");

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
                    CreatedAt = seedTime.AddDays(-40)
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
                    CreatedAt = RealisticDevelopmentSeed.At(dateTimeProvider, completedAppts[0].AppointmentDate, completedAppts[0].EndTime.Hour, completedAppts[0].EndTime.Minute)
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
                    Status = PrescriptionStatus.Issued,
                    Notes = "Kiêng đồ cay nóng, bia rượu và chất kích thích.",
                    CreatedAt = RealisticDevelopmentSeed.At(dateTimeProvider, completedAppts[1].AppointmentDate, completedAppts[1].EndTime.Hour, completedAppts[1].EndTime.Minute)
                };
                db.Prescriptions.Add(pres2);
                await db.SaveChangesAsync();

                db.PrescriptionItems.AddRange(
                    new PrescriptionItem { PrescriptionId = pres2.Id, MedicineId = med4.Id, Quantity = 14, Dosage = "1 viên / lần", Frequency = "1 lần / ngày trước ăn sáng 30 phút", DurationDays = 14, Instructions = "Uống trước bữa ăn" },
                    new PrescriptionItem { PrescriptionId = pres2.Id, MedicineId = med12.Id, Quantity = 15, Dosage = "1 gói / lần", Frequency = "3 lần / ngày khi đau hoặc sau ăn 1h", DurationDays = 5, Instructions = "Lắc kỹ trước khi dùng" }
                );
                await db.SaveChangesAsync();
                await RealisticDevelopmentSeed.DispenseAsync(db, dateTimeProvider, pres2, pharmacistId, pres2.CreatedAt.AddMinutes(30));

                await db.SaveChangesAsync();
                logger.LogInformation("Sample Prescriptions seeded.");
            }
        }

        // Diagnostic Services Catalog (Idempotent)
        var diagnosticServiceDefs = new[]
        {
            new DiagnosticService { Code = "LAB-CBC", Name = "Tổng phân tích tế bào máu ngoại vi", Category = DiagnosticCategory.Laboratory, Price = 120000m, PreparationInstructions = "Không cần nhịn ăn đặc biệt.", IsActive = true },
            new DiagnosticService { Code = "LAB-GLU", Name = "Định lượng Glucose máu", Category = DiagnosticCategory.Laboratory, Price = 80000m, PreparationInstructions = "Nhịn đói ít nhất 8 tiếng trước khi lấy mẫu.", IsActive = true },
            new DiagnosticService { Code = "LAB-LIPID", Name = "Bộ mỡ máu toàn phần (Lipid panel)", Category = DiagnosticCategory.Laboratory, Price = 150000m, PreparationInstructions = "Nhịn ăn 10-12 tiếng trước khi lấy máu.", IsActive = true },
            new DiagnosticService { Code = "LAB-LFT", Name = "Đánh giá chức năng gan (AST, ALT)", Category = DiagnosticCategory.Laboratory, Price = 140000m, PreparationInstructions = "Tránh uống rượu bia 24 giờ trước khi xét nghiệm.", IsActive = true },
            new DiagnosticService { Code = "LAB-RFT", Name = "Đánh giá chức năng thận (Ure, Creatinin)", Category = DiagnosticCategory.Laboratory, Price = 130000m, PreparationInstructions = "Uống đủ nước, sinh hoạt bình thường.", IsActive = true },
            new DiagnosticService { Code = "US-ABD", Name = "Siêu âm ổ bụng tổng quát", Category = DiagnosticCategory.Ultrasound, Price = 250000m, PreparationInstructions = "Nhịn ăn ít nhất 6 tiếng, uống nhiều nước và nhịn tiểu.", IsActive = true },
            new DiagnosticService { Code = "US-THY", Name = "Siêu âm tuyến giáp", Category = DiagnosticCategory.Ultrasound, Price = 200000m, PreparationInstructions = "Không cần chuẩn bị trước.", IsActive = true },
            new DiagnosticService { Code = "US-ECHO", Name = "Siêu âm Doppler tim màu", Category = DiagnosticCategory.Ultrasound, Price = 450000m, PreparationInstructions = "Nghỉ ngơi 15 phút trước khi thực hiện.", IsActive = true },
            new DiagnosticService { Code = "IMG-CXR", Name = "Chụp X-quang ngực thẳng", Category = DiagnosticCategory.Imaging, Price = 180000m, PreparationInstructions = "Tháo bỏ trang sức kim loại vùng ngực và cổ.", IsActive = true },
            new DiagnosticService { Code = "IMG-ECG", Name = "Điện tâm đồ (ECG 12 chuyển đạo)", Category = DiagnosticCategory.Other, Price = 100000m, PreparationInstructions = "Nghỉ ngơi yên tĩnh 10 phút trước khi đo.", IsActive = true }
        };

        foreach (var ds in diagnosticServiceDefs)
        {
            // Only laboratory services declare a specimen workflow; existing items keep NotRequired.
            (ds.SpecimenType, ds.ContainerType) = ds.Code switch
            {
                "LAB-CBC" => ("Máu toàn phần", "Ống EDTA (nắp tím)"),
                "LAB-GLU" => ("Huyết tương", "Ống Natri Fluoride (nắp xám)"),
                "LAB-LIPID" or "LAB-LFT" or "LAB-RFT" => ("Huyết thanh", "Ống Serum có hạt (nắp đỏ)"),
                _ => ((string?)null, (string?)null)
            };
            var existing = await db.DiagnosticServices.FirstOrDefaultAsync(s => s.Code == ds.Code);
            if (existing == null)
            {
                db.DiagnosticServices.Add(ds);
            }
            else
            {
                existing.Name = ds.Name;
                existing.Category = ds.Category;
                existing.PreparationInstructions = ds.PreparationInstructions;
                existing.SpecimenType = ds.SpecimenType;
                existing.ContainerType = ds.ContainerType;
                existing.IsActive = true;
                if (!existing.Price.HasValue || existing.Price.Value <= 0)
                {
                    existing.Price = ds.Price;
                }
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Diagnostic Services catalog seeded.");
        await SeedDiagnosticParametersAsync(db);

        // 11. Facility BV-TW-01, Departments, Rooms & Staff Facility Assignments
        var fac = await ClinicBrandDevelopmentSeed.SeedFacilityAsync(db);

        var building = await db.Buildings.FirstOrDefaultAsync(b => b.FacilityId == fac.Id && b.Code == "TN-A");
        if (building == null)
        {
            building = new Building
            {
                FacilityId = fac.Id,
                Code = "TN-A",
                Name = "Tòa nhà A - Khám bệnh Đa khoa",
                NumberOfFloors = 5,
                IsActive = true
            };
            db.Buildings.Add(building);
            await db.SaveChangesAsync();
        }

        var deptDefs = new[]
        {
            new { Code = "KKB", Name = "Khoa Khám bệnh Ngoại trú", SpecialtyCode = "SP01", Type = DepartmentType.Clinical },
            new { Code = "KNHI", Name = "Khoa Nhi", SpecialtyCode = "SP02", Type = DepartmentType.Clinical },
            new { Code = "KPS", Name = "Khoa Sản phụ khoa", SpecialtyCode = "SP03", Type = DepartmentType.Clinical },
            new { Code = "KTMH", Name = "Khoa Tai Mũi Họng", SpecialtyCode = "SP05", Type = DepartmentType.Clinical },
            new { Code = "KTM", Name = "Khoa Tim mạch", SpecialtyCode = "SP06", Type = DepartmentType.Clinical }
        };

        var deptEntities = new List<Department>();
        foreach (var dd in deptDefs)
        {
            var d = await db.Departments.FirstOrDefaultAsync(x => x.FacilityId == fac.Id && x.Code == dd.Code);
            var spec = await db.Specialties.FirstOrDefaultAsync(s => s.SpecialtyCode == dd.SpecialtyCode);
            if (d == null)
            {
                d = new Department
                {
                    FacilityId = fac.Id,
                    BuildingId = building.Id,
                    Code = dd.Code,
                    Name = dd.Name,
                    DepartmentType = dd.Type,
                    SpecialtyId = spec?.Id,
                    IsActive = true
                };
                db.Departments.Add(d);
                await db.SaveChangesAsync();
            }
            else if (d.SpecialtyId == null && spec != null)
            {
                d.SpecialtyId = spec.Id;
                await db.SaveChangesAsync();
            }
            deptEntities.Add(d);
        }

        var roomDefs = new[]
        {
            new { DeptCode = "KKB", RoomNumber = "P101", Name = "Phòng khám Nội 101", Floor = 1 },
            new { DeptCode = "KKB", RoomNumber = "P102", Name = "Phòng khám Nội 102", Floor = 1 },
            new { DeptCode = "KNHI", RoomNumber = "P201", Name = "Phòng khám Nhi 201", Floor = 2 },
            new { DeptCode = "KPS", RoomNumber = "P202", Name = "Phòng khám Sản 202", Floor = 2 },
            new { DeptCode = "KTMH", RoomNumber = "P301", Name = "Phòng khám TMH 301", Floor = 3 },
            new { DeptCode = "KTM", RoomNumber = "P302", Name = "Phòng khám Tim mạch 302", Floor = 3 }
        };

        foreach (var rd in roomDefs)
        {
            var dept = deptEntities.FirstOrDefault(d => d.Code == rd.DeptCode);
            if (dept != null)
            {
                var r = await db.Rooms.FirstOrDefaultAsync(x => x.DepartmentId == dept.Id && x.RoomNumber == rd.RoomNumber);
                if (r == null)
                {
                    r = new Room
                    {
                        DepartmentId = dept.Id,
                        BuildingId = building.Id,
                        RoomNumber = rd.RoomNumber,
                        Name = rd.Name,
                        FloorNumber = rd.Floor,
                        RoomType = RoomType.Consultation,
                        IsActive = true
                    };
                    db.Rooms.Add(r);
                    await db.SaveChangesAsync();
                }
            }
        }

        var staffUsers = new List<(ApplicationUser User, string Role)>
        {
            (adminId1, "Admin"),
            (adminId2, "Admin"),
            (rec1, "Receptionist"),
            (rec2, "Receptionist"),
            (rec3, "Receptionist"),
            (pharmacist, "Pharmacist"),
            (technician, ClinicManagement.Application.Common.Constants.RoleNames.DiagnosticTechnician),
            (labSupervisor, ClinicManagement.Application.Common.Constants.RoleNames.LabSupervisor),
            (technician02, ClinicManagement.Application.Common.Constants.RoleNames.DiagnosticTechnician)
        };

        foreach (var du in docUsers)
        {
            staffUsers.Add((du, "Doctor"));
        }

        foreach (var (stUser, role) in staffUsers)
        {
            var existingAssignment = await db.StaffFacilityAssignments
                .FirstOrDefaultAsync(a => a.UserId == stUser.Id && a.FacilityId == fac.Id);
            if (existingAssignment == null)
            {
                var assignedDept = deptEntities.FirstOrDefault();
                db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
                {
                    UserId = stUser.Id,
                    FacilityId = fac.Id,
                    DepartmentId = assignedDept?.Id,
                    Role = role,
                    IsPrimary = true,
                    IsActive = true,
                    AssignedAtUtc = seedTime
                });
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Facility BV-TW-01, departments, rooms, and staff assignments seeded.");

        await MedicineBatchDevelopmentSeed.SeedAsync(db, dateTimeProvider, pharmacist.Id,
            config?.GetValue<bool>("DemoSeed:Enabled") == true);
        if (seedAppointments)
        {
            await RealisticDevelopmentSeed.SeedAsync(db, dateTimeProvider, rec1.Id, technician.Id, labSupervisor.Id, pharmacist.Id);
            await DemoClinicalDevelopmentSeed.SeedAsync(db, dateTimeProvider, rec1.Id, technician.Id, labSupervisor.Id, pharmacist.Id);
        }
        logger.LogInformation("Development Data Seeding completed.");
    }

    public static async Task SeedMasterDataAsync(IServiceProvider serviceProvider)
    {
        await SeedAsync(serviceProvider, new DevelopmentDataSeederOptions
        {
            SeedDoctorSchedulesAndSlots = false,
            SeedDemoAppointments = false
        });
    }

    public static async Task SeedDoctorSchedulesAndSlotsAsync(IServiceProvider serviceProvider, int days = 7, bool seedDemoAppointments = false)
    {
        await SeedAsync(serviceProvider, new DevelopmentDataSeederOptions
        {
            SeedDoctorSchedulesAndSlots = true,
            ScheduleDays = days,
            SeedDemoAppointments = seedDemoAppointments
        });
    }

    public static async Task SeedDoctorSchedulesAndSlotsInternalAsync(
        AppDbContext db,
        List<Doctor> doctors,
        ClinicManagement.Application.Common.Interfaces.IDateTimeProvider dateTimeProvider,
        ILogger logger,
        int scheduleDays = 7)
    {
        await DevelopmentScheduleSeed.SeedAsync(db, doctors, dateTimeProvider, logger, scheduleDays);
    }

    private static async Task<ApplicationUser> SeedUserAsync(UserManager<ApplicationUser> userManager, string email, string name, string phone, string role, string password, ClinicManagement.Application.Common.Interfaces.IDateTimeProvider clock)
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
                if (existingByPhone.CreatedAt.Year <= 1900) existingByPhone.CreatedAt = RealisticDevelopmentSeed.At(clock, clock.VietnamToday.AddDays(-90), 8);
                await userManager.UpdateAsync(existingByPhone);
                if (!await userManager.IsInRoleAsync(existingByPhone, role))
                {
                    await userManager.AddToRoleAsync(existingByPhone, role);
                }
                return existingByPhone;
            }

            user = new ApplicationUser { UserName = email, Email = email, FullName = name, PhoneNumber = phone, IsActive = true, CreatedAt = RealisticDevelopmentSeed.At(clock, clock.VietnamToday.AddDays(-90), 8) };
            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, role);
        }
        else
        {
            user.FullName = name;
            user.PhoneNumber = phone;
            user.IsActive = true;
            if (user.CreatedAt.Year <= 1900) user.CreatedAt = RealisticDevelopmentSeed.At(clock, clock.VietnamToday.AddDays(-90), 8);
            await userManager.UpdateAsync(user);
            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }
        return user;
    }
}
