using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DemoClinicalDevelopmentSeed
{
    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid receptionist,
        Guid technician, Guid supervisor, Guid pharmacist)
    {
        if (await db.SystemAuditLogs.AnyAsync(l => l.Action == "InitializeClinicalDemo")) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var patients = await db.Patients.OrderBy(p => p.Id).ToListAsync();
        foreach (var patient in patients)
        {
            var birthYear = patient.DateOfBirth?.Year ?? 1990;
            var century = birthYear >= 2000 ? 2 : 0;
            var genderDigit = century + (patient.Gender == Gender.Female ? 1 : 0);
            patient.NationalId ??= $"074{genderDigit}{birthYear % 100:D2}{patient.Id:D6}";
        }
        var appointments = await db.Appointments.Include(a => a.VisitSummary).Include(a => a.VitalSigns)
            .Include(a => a.Prescription).ThenInclude(p => p!.Items).Include(a => a.Specialty).Include(a => a.Doctor)
            .Where(a => a.Status == AppointmentStatus.Completed).ToListAsync();
        var medicines = await db.Medicines.ToDictionaryAsync(m => m.Code);
        foreach (var appointment in appointments)
        {
            var completedAt = RealisticDevelopmentSeed.At(clock, appointment.AppointmentDate, appointment.EndTime.Hour, appointment.EndTime.Minute);
            appointment.VisitSummary ??= new VisitSummary { AppointmentId = appointment.Id, DoctorId = appointment.DoctorId };
            FillSummary(appointment.VisitSummary, appointment.Specialty.SpecialtyCode, appointment.Reason, completedAt);
            appointment.VitalSigns ??= Vitals(appointment.Doctor.UserId, completedAt.AddMinutes(-25));
            if (appointment.Prescription == null)
            {
                var medicine = medicines[MedicineFor(appointment.Specialty.SpecialtyCode)];
                appointment.Prescription = new Prescription
                {
                    AppointmentId = appointment.Id, PatientId = appointment.PatientId, DoctorId = appointment.DoctorId,
                    CreatedAt = completedAt, Status = PrescriptionStatus.Issued,
                    Notes = "Dùng theo đơn, tái khám nếu triệu chứng không cải thiện.",
                    Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 10, Dosage = "1 viên/lần", Frequency = "2 lần/ngày sau ăn", DurationDays = 5 }],
                };
            }
            appointment.Prescription.CreatedAt = completedAt;
            if (appointment.Prescription.DispensedAt != null)
            {
                appointment.Prescription.DispensedAt = completedAt.AddMinutes(30);
                foreach (var movement in await db.MedicineStockTransactions.Where(t => t.PrescriptionId == appointment.Prescription.Id).ToListAsync())
                    movement.CreatedAt = appointment.Prescription.DispensedAt.Value;
            }
        }
        await db.SaveChangesAsync();
        foreach (var prescription in appointments.Select(a => a.Prescription!).Where(p => p.Status != PrescriptionStatus.Dispensed))
            await RealisticDevelopmentSeed.DispenseAsync(db, clock, prescription, pharmacist, prescription.CreatedAt.AddMinutes(30));

        var visits = await db.PatientVisits.Include(v => v.VisitSummary).Include(v => v.VitalSigns)
            .Include(v => v.AssignedDoctor).ThenInclude(d => d!.DoctorSpecialties).ThenInclude(ds => ds.Specialty)
            .Where(v => v.CompletedAtUtc != null).ToListAsync();
        foreach (var visit in visits)
        {
            if (visit.AssignedDoctor == null) continue;
            var specialty = visit.AssignedDoctor.DoctorSpecialties.First(ds => ds.IsPrimary).Specialty.SpecialtyCode;
            visit.VisitSummary ??= new VisitSummary { PatientVisitId = visit.Id, DoctorId = visit.AssignedDoctor.Id };
            FillSummary(visit.VisitSummary, specialty, visit.ChiefComplaint, visit.CompletedAtUtc!.Value);
            visit.VitalSigns ??= Vitals(visit.AssignedDoctor.UserId, visit.CheckedInAtUtc.AddMinutes(3));
        }

        var historicalOrders = await db.DiagnosticOrders.Include(o => o.Items).ThenInclude(i => i.DiagnosticService)
            .Include(o => o.Items).ThenInclude(i => i.Result).Where(o => o.PatientVisit != null && o.PatientVisit.QueueNumber < 200 && o.Status == DiagnosticOrderStatus.Completed).ToListAsync();
        foreach (var order in historicalOrders)
        {
            order.StartedAtUtc = order.OrderedAtUtc.AddMinutes(2);
            foreach (var item in order.Items)
            {
                if (item.DiagnosticService.SpecimenType != null)
                {
                    item.SpecimenStatus = SpecimenStatus.Received;
                    item.SpecimenBarcode = $"SP-{clock.ConvertUtcToVietnam(order.OrderedAtUtc):yyMMdd}-H{item.Id:D4}";
                    item.CollectedAtUtc = order.OrderedAtUtc.AddMinutes(4); item.CollectedByUserId = technician;
                    item.ReceivedAtUtc = order.OrderedAtUtc.AddMinutes(6); item.ReceivedByUserId = technician;
                }
                else item.SpecimenStatus = SpecimenStatus.NotRequired;
            }
            order.VerificationStatus = DiagnosticVerificationStatus.Verified;
            order.SubmittedForVerificationAtUtc = order.Items.Max(i => i.Result!.ResultedAtUtc).AddMinutes(1);
            order.SubmittedForVerificationByUserId = technician;
            order.VerifiedAtUtc = order.SubmittedForVerificationAtUtc.Value.AddMinutes(1);
            order.CompletedAtUtc = order.VerifiedAtUtc;
            order.VerifiedByUserId = supervisor;
            order.ReviewedAtUtc = order.CompletedAtUtc!.Value.AddMinutes(1);
            order.ReviewedByDoctorId = order.OrderingDoctorId;
        }
        await db.SaveChangesAsync();
        await DemoActivityDevelopmentSeed.SeedAsync(db, clock, receptionist, technician, supervisor, pharmacist);
        db.SystemAuditLogs.Add(new SystemAuditLog { UserId = receptionist, Action = "InitializeClinicalDemo",
            EntityName = "DevelopmentData", EntityId = "clinical-v3", Description = "Khởi tạo dữ liệu lâm sàng", CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static AppointmentVitalSigns Vitals(Guid actor, DateTime time) => new()
    {
        Temperature = 36.7m, BloodPressureSystolic = 120, BloodPressureDiastolic = 78, HeartRate = 76,
        RespiratoryRate = 18, Weight = 58, Height = 163, Bmi = AppointmentVitalSigns.CalculateBmi(58, 163), SpO2 = 98,
        RecordedAtUtc = time, RecordedByUserId = actor,
    };

    private static string MedicineFor(string specialty) => specialty switch
    {
        "SP03" => "MED27", "SP04" => "MED13", "SP09" => "MED07", "SP11" => "MED04", _ => "MED01",
    };

    private static void FillSummary(VisitSummary summary, string specialty, string? complaint, DateTime time)
    {
        var (diagnosis, icd, findings, plan) = ClinicalProfile(specialty, complaint ?? "");
        summary.ChiefComplaint = complaint;
        summary.ClinicalFindings = findings;
        summary.Diagnosis = diagnosis; summary.DiagnosisCode = icd;
        summary.TreatmentPlan = plan;
        summary.Summary = $"{diagnosis}. Tình trạng ổn định, điều trị ngoại trú.";
        summary.FollowUpInstruction = "Tái khám sau 7 ngày hoặc sớm hơn khi triệu chứng tăng.";
        summary.CreatedAtUtc = time.AddMinutes(-25); summary.CompletedAtUtc = time;
    }

    private static (string Diagnosis, string Icd, string Findings, string Plan) ClinicalProfile(string specialty, string complaint)
    {
        if (specialty == "SP01")
        {
            if (complaint.Contains("huyết áp")) return ("Tăng huyết áp đang kiểm soát", "I10", "Tim đều, phổi thông khí tốt, không phù", "Theo dõi huyết áp, giảm muối, duy trì thuốc theo đơn");
            if (complaint.Contains("Ho") || complaint.Contains("cảm cúm")) return ("Viêm đường hô hấp trên", "J06.9", "Họng đỏ nhẹ, phổi không ran", "Nghỉ ngơi, uống đủ nước, điều trị triệu chứng");
            if (complaint.Contains("đường huyết")) return ("Theo dõi rối loạn đường huyết", "R73.9", "Tỉnh, không dấu mất nước, bụng mềm", "Điều chỉnh dinh dưỡng, kiểm tra đường huyết theo hẹn");
            if (complaint.Contains("định kỳ")) return ("Khám sức khỏe định kỳ", "Z00.0", "Tim phổi chưa ghi nhận bất thường", "Tư vấn vận động, dinh dưỡng và khám định kỳ");
            if (complaint.Contains("Chóng mặt")) return ("Chóng mặt tư thế", "R42", "Tỉnh táo, không dấu thần kinh khu trú", "Thay đổi tư thế chậm, uống đủ nước, theo dõi triệu chứng");
            if (complaint.Contains("mất ngủ")) return ("Rối loạn giấc ngủ", "G47.0", "Tỉnh, tim đều, không dấu thần kinh khu trú", "Vệ sinh giấc ngủ, hạn chế caffeine buổi chiều");
            return ("Mệt mỏi sau gắng sức", "R53", "Tỉnh, niêm hồng, tim phổi ổn định", "Nghỉ ngơi, bổ sung dinh dưỡng và theo dõi");
        }
        return specialty switch
        {
            "SP02" => ("Viêm mũi họng cấp", "J00", "Niêm mạc mũi phù nhẹ, phổi không ran", "Chăm sóc mũi họng, theo dõi nhiệt độ"),
            "SP03" when complaint.Contains("thai") => ("Theo dõi thai kỳ bình thường", "Z34.9", "Tổng trạng ổn định, bụng không đau", "Khám thai định kỳ và bổ sung vi chất theo đơn"),
            "SP03" => ("Rối loạn kinh nguyệt", "N92.6", "Bụng mềm, không phản ứng thành bụng", "Theo dõi chu kỳ, tái khám chuyên khoa"),
            "SP04" => ("Viêm da dị ứng", "L23.9", "Mảng đỏ và sẩn ngứa khu trú, không bội nhiễm", "Tránh dị nguyên, dưỡng ẩm, dùng thuốc theo đơn"),
            "SP05" => ("Viêm mũi họng", "J06.9", "Họng đỏ, niêm mạc mũi phù nhẹ", "Vệ sinh mũi họng, điều trị triệu chứng"),
            "SP06" => ("Tăng huyết áp", "I10", "Tim đều, không phù ngoại biên", "Theo dõi huyết áp và tuân thủ thuốc"),
            "SP07" => ("Đau khớp cơ học", "M25.5", "Đau khi vận động, không sưng nóng khớp", "Giảm tải khớp, tập phục hồi và thuốc theo đơn"),
            "SP08" => ("Đau đầu căng thẳng", "G44.2", "Không dấu thần kinh khu trú", "Điều chỉnh giấc ngủ và điều trị triệu chứng"),
            "SP09" => ("Đái tháo đường típ 2", "E11.9", "Không dấu mất nước, bàn chân không loét", "Theo dõi đường huyết, điều chỉnh dinh dưỡng"),
            "SP10" => ("Khô mắt", "H04.1", "Kết mạc khô nhẹ, không đau nhức mắt", "Nghỉ mắt khi dùng màn hình, tái khám thị lực"),
            _ => ("Rối loạn tiêu hóa", "K30", "Bụng mềm, đau nhẹ thượng vị, không phản ứng thành bụng", "Chia nhỏ bữa ăn, tránh chất kích thích, dùng thuốc theo đơn"),
        };
    }
}
