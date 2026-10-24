using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class RealisticDevelopmentSeed
{
    internal static DateTime At(IDateTimeProvider clock, DateOnly date, int hour, int minute = 0) =>
        clock.ConvertVietnamToUtc(date.ToDateTime(new TimeOnly(hour, minute)));

    internal static string Reason(string code, int variant)
    {
        if (code == "SP01")
        {
            string[] reasons = ["Mệt mỏi, ăn kém và cần kiểm tra sức khỏe", "Tái khám huyết áp và tư vấn dinh dưỡng", "Ho khan và sốt nhẹ 2 ngày", "Khám sức khỏe định kỳ trước khi đi làm", "Kiểm tra đường huyết lúc đói theo hẹn", "Chóng mặt khi thay đổi tư thế 3 ngày", "Đau nhức cơ sau đợt cảm cúm", "Tư vấn mất ngủ và mệt mỏi gần đây"];
            return reasons[Math.Abs(variant % reasons.Length)];
        }
        return (code, variant % 2) switch
        {
        ("SP02", 0) => "Trẻ ho, sổ mũi 2 ngày", ("SP02", _) => "Tư vấn dinh dưỡng và theo dõi tăng trưởng của trẻ",
        ("SP03", 0) => "Khám thai định kỳ tuần 20", ("SP03", _) => "Tư vấn rối loạn kinh nguyệt 2 tháng",
        ("SP04", 0) => "Nổi mẩn ngứa ở tay 1 tuần", ("SP04", _) => "Mụn viêm vùng mặt kéo dài 1 tháng",
        ("SP05", 0) => "Đau họng, nghẹt mũi 3 ngày", ("SP05", _) => "Ù tai phải và nghe kém 1 tuần",
        ("SP06", 0) => "Hồi hộp, đo huyết áp tại nhà cao", ("SP06", _) => "Tái khám tăng huyết áp theo hẹn",
        ("SP07", 0) => "Đau khớp gối khi đi cầu thang", ("SP07", _) => "Đau thắt lưng sau vận động 5 ngày",
        ("SP08", 0) => "Đau đầu từng cơn kèm khó ngủ", ("SP08", _) => "Tê bàn tay và chóng mặt 3 ngày",
        ("SP09", 0) => "Tái khám đái tháo đường, kiểm tra đường huyết", ("SP09", _) => "Mệt mỏi và sụt cân, kiểm tra tuyến giáp",
        ("SP10", 0) => "Mờ mắt khi đọc sách và nhìn xa", ("SP10", _) => "Khô mắt và cộm mắt khi làm việc máy tính",
        ("SP11", 0) => "Đau thượng vị sau ăn 1 tuần", ("SP11", _) => "Đầy bụng, ợ chua kéo dài 5 ngày",
        (_, 0) => "Mệt mỏi, ăn kém và cần kiểm tra sức khỏe", (_, _) => "Tái khám huyết áp và tư vấn dinh dưỡng",
        };
    }

    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid receptionist, Guid technician, Guid supervisor)
    {
        if (await db.SystemAuditLogs.AnyAsync(l => l.Action == "InitializeStaffDemo" && l.EntityId == "staff-v1"))
        {
            await SynchronizeExistingAsync(db);
            return;
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var facility = await db.Facilities.SingleAsync(f => f.Code == "BV-TW-01");
        var department = await db.Departments.Include(d => d.Specialty).SingleAsync(d => d.FacilityId == facility.Id && d.Code == "KKB");
        var doctors = await db.Doctors.OrderBy(d => d.Id).ToListAsync();
        var patients = await db.Patients.OrderBy(p => p.Id).ToListAsync();
        var service = await db.DiagnosticServices.Include(s => s.Parameters).SingleAsync(s => s.Code == "LAB-CBC");
        var medicine = await db.Medicines.SingleAsync(m => m.Code == "MED01");
        var packages = await db.HealthPackages.OrderBy(p => p.Code).ToListAsync();
        var packageIndex = 0;
        var today = clock.VietnamToday;

        for (var i = 0; i < 30; i++)
        {
            if (i % 6 == 5) continue;
            var date = today.AddDays(-i);
            var patient = patients[i % patients.Count];
            var time = At(clock, date, 9, (i % 4) * 10);
            var status = i is 1 or 7 or 13 ? InvoiceStatus.Unpaid : i is 2 or 8 ? InvoiceStatus.Cancelled : InvoiceStatus.Paid;
            var invoice = new Invoice
            {
                InvoiceCode = $"INV-{date:yyMMdd}-S{i + 1:D4}", PatientId = patient.Id,
                SourceType = InvoiceSourceType.Appointment, CreatedByUserId = receptionist,
                CreatedAtUtc = time, Status = status,
                PaidAtUtc = status == InvoiceStatus.Paid ? time.AddMinutes(30) : null,
                PaidByUserId = status == InvoiceStatus.Paid ? receptionist : null,
                CancelledAtUtc = status == InvoiceStatus.Cancelled ? time.AddMinutes(15) : null,
                CancellationReason = status == InvoiceStatus.Cancelled ? "Người bệnh hủy dịch vụ trước khi thực hiện" : null,
            };
            if (i % 4 == 3)
            {
                var package = packages[packageIndex++ % packages.Count];
                invoice.SourceType = InvoiceSourceType.HealthPackageRegistration;
                invoice.HealthPackageRegistration = new HealthPackageRegistration
                {
                    RegistrationCode = $"PKG-{date:yyMMdd}-S{i + 1:D4}", PatientId = patient.Id,
                    HealthPackageId = package.Id, PreferredDate = date, ContactPhone = patient.PhoneNumber ?? "0900000004",
                    Status = HealthPackageRegistrationStatus.Confirmed, CreatedAt = time.AddDays(-2),
                };
                invoice.Items.Add(Item(package.Code, package.Name, package.Price, 1, "HealthPackage", package.Id));
            }
            else
            {
                var visit = new PatientVisit
                {
                    VisitCode = $"VIS-{date:yyMMdd}-S{i + 1:D4}", PatientId = patient.Id, FacilityId = facility.Id,
                    DepartmentId = department.Id, AssignedDoctorId = doctors[0].Id, VisitDate = date,
                    QueueNumber = 100 + i, ChiefComplaint = Reason("SP01", i), Status = VisitStatus.Completed,
                    CreatedByUserId = receptionist, CreatedAtUtc = time.AddHours(-1), CheckedInAtUtc = time.AddHours(-1),
                    ConsultationStartedAtUtc = time.AddMinutes(-45), CompletedAtUtc = time.AddMinutes(-15),
                };
                invoice.PatientVisit = visit;
                db.PatientVisits.Add(visit);
                await db.SaveChangesAsync();
                invoice.Items.Add(Item(department.Specialty!.SpecialtyCode, "Phí khám nội tổng quát", department.Specialty.ConsultationFee, 1, "Consultation", visit.Id));
                if (i % 4 >= 1)
                {
                    var order = new DiagnosticOrder
                    {
                        OrderCode = $"CLS-{date:yyMMdd}-S{i + 1:D4}", PatientVisit = visit, PatientId = patient.Id,
                        OrderingDoctorId = doctors[0].Id, FacilityId = facility.Id, ClinicalIndication = "Kiểm tra công thức máu theo chỉ định",
                        Status = DiagnosticOrderStatus.Completed, OrderedAtUtc = time.AddMinutes(-40), StartedAtUtc = time.AddMinutes(-35),
                        CompletedAtUtc = time.AddMinutes(-20), StartedByUserId = technician, CompletedByUserId = technician,
                        Items = [new DiagnosticOrderItem { DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.Completed,
                            Result = Result(service, technician, time.AddMinutes(-20)) }],
                    };
                    db.DiagnosticOrders.Add(order);
                    await db.SaveChangesAsync();
                    invoice.Items.Add(Item(service.Code, service.Name, service.Price!.Value, 1, "DiagnosticItem", order.Items.Single().Id));
                }
                if (i % 4 == 2)
                {
                    var prescription = new Prescription
                    {
                        PatientVisit = visit, PatientId = patient.Id, DoctorId = doctors[0].Id,
                        Status = PrescriptionStatus.Issued, CreatedAt = time.AddMinutes(-10),
                        Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 10, Dosage = "1 viên khi đau", Frequency = "Theo đơn", DurationDays = 5 }],
                    };
                    db.Prescriptions.Add(prescription);
                    await db.SaveChangesAsync();
                    invoice.Items.Add(Item(medicine.Code, medicine.Name, medicine.UnitPrice!.Value, 10, "PrescriptionItem:v2", prescription.Id * 4294967296L + medicine.Id));
                }
            }
            invoice.Subtotal = invoice.TotalAmount = invoice.Items.Sum(item => item.LineTotal);
            if (status == InvoiceStatus.Cancelled)
                foreach (var item in invoice.Items) item.IsCancelled = true;
            if (status == InvoiceStatus.Paid)
                invoice.Payments.Add(new Payment
                {
                    PaymentCode = $"PAY-{date:yyMMdd}-S{i + 1:D4}", Amount = invoice.TotalAmount,
                    Method = i % 2 == 0 ? PaymentMethod.Cash : PaymentMethod.ManualBankTransfer,
                    ReceivedAtUtc = invoice.PaidAtUtc!.Value, ReceivedByUserId = receptionist, Status = PaymentStatus.Succeeded,
                });
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 5; i++)
        {
            var patient = patients[(i + 5) % patients.Count];
            var time = At(clock, today, 8, i * 10);
            var visit = new PatientVisit
            {
                VisitCode = $"VIS-{today:yyMMdd}-D{i + 1:D4}", PatientId = patient.Id, FacilityId = facility.Id,
                DepartmentId = department.Id, AssignedDoctorId = doctors[1].Id, VisitDate = today, QueueNumber = 200 + i,
                ChiefComplaint = Reason("SP01", i), Status = i == 4 ? VisitStatus.ResultsReady : VisitStatus.WaitingForDiagnostics, CreatedByUserId = receptionist,
                CreatedAtUtc = time, CheckedInAtUtc = time, ConsultationStartedAtUtc = time.AddMinutes(5),
            };
            var completed = i >= 3;
            var order = new DiagnosticOrder
            {
                OrderCode = $"CLS-{today:yyMMdd}-D{i + 1:D4}", PatientVisit = visit, PatientId = patient.Id,
                OrderingDoctorId = doctors[1].Id, FacilityId = facility.Id, ClinicalIndication = "Kiểm tra công thức máu trước khi kết luận",
                OrderedAtUtc = time.AddMinutes(10), Status = i == 4 ? DiagnosticOrderStatus.Completed : i >= 2 ? DiagnosticOrderStatus.InProgress : DiagnosticOrderStatus.Ordered,
                StartedAtUtc = i >= 2 ? time.AddMinutes(20) : null, StartedByUserId = i >= 2 ? technician : null,
                CompletedAtUtc = i == 4 ? time.AddMinutes(40) : null, CompletedByUserId = i == 4 ? technician : null,
                VerificationStatus = i == 3 ? DiagnosticVerificationStatus.Pending : i == 4 ? DiagnosticVerificationStatus.Verified : DiagnosticVerificationStatus.NotRequired,
                SubmittedForVerificationAtUtc = completed ? time.AddMinutes(45) : null, SubmittedForVerificationByUserId = completed ? technician : null,
                VerifiedAtUtc = i == 4 ? time.AddMinutes(50) : null, VerifiedByUserId = i == 4 ? supervisor : null,
            };
            order.Items.Add(new DiagnosticOrderItem
            {
                DiagnosticServiceId = service.Id, Status = completed ? DiagnosticItemStatus.Completed : i == 2 ? DiagnosticItemStatus.InProgress : DiagnosticItemStatus.Ordered,
                SpecimenStatus = i >= 2 ? SpecimenStatus.Received : SpecimenStatus.PendingCollection,
                SpecimenBarcode = i >= 2 ? $"SP-{today:yyMMdd}-{i + 1:D4}" : null,
                CollectedAtUtc = i >= 2 ? time.AddMinutes(15) : null, CollectedByUserId = i >= 2 ? technician : null,
                ReceivedAtUtc = i >= 2 ? time.AddMinutes(18) : null, ReceivedByUserId = i >= 2 ? technician : null,
                Result = completed ? Result(service, technician, time.AddMinutes(40)) : null,
            });
            db.DiagnosticOrders.Add(order);
        }
        db.SystemAuditLogs.Add(new SystemAuditLog { UserId = receptionist, Action = "InitializeStaffDemo", EntityName = "DevelopmentData", EntityId = "staff-v1", Description = "Khởi tạo dữ liệu bàn làm việc", CreatedAt = At(clock, today, 9) });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static InvoiceItem Item(string code, string description, decimal price, int quantity, string referenceType, long referenceId) =>
        new() { ItemCode = code, Description = description, Quantity = quantity, UnitPrice = price, LineTotal = price * quantity, ReferenceType = referenceType, ReferenceId = referenceId };

    private static async Task SynchronizeExistingAsync(AppDbContext db)
    {
        var appointments = await db.Appointments.Include(a => a.Specialty)
            .Where(a => a.Histories.Any(h => h.Note == "Bệnh nhân tự đặt lịch hẹn qua ứng dụng"))
            .OrderBy(a => a.DoctorId).ThenBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).ToListAsync();
        var knownReasons = Enumerable.Range(0, 8).Select(i => Reason("SP01", i)).ToHashSet();
        foreach (var group in appointments.GroupBy(a => a.DoctorId))
        {
            var variant = 0;
            foreach (var appointment in group)
            {
                if (appointment.Specialty.SpecialtyCode == "SP01" && knownReasons.Contains(appointment.Reason ?? ""))
                    appointment.Reason = Reason("SP01", variant);
                variant++;
            }
        }
        var orders = await db.DiagnosticOrders.Include(o => o.PatientVisit)
            .Where(o => o.OrderCode.StartsWith("CLS-") && o.ClinicalIndication == "Kiểm tra công thức máu trước khi kết luận")
            .ToListAsync();
        foreach (var order in orders)
        {
            if (order.OrderCode.EndsWith("-D0004", StringComparison.Ordinal) &&
                order.VerificationStatus == DiagnosticVerificationStatus.Pending && order.Status == DiagnosticOrderStatus.Completed)
            {
                order.Status = DiagnosticOrderStatus.InProgress;
                order.CompletedAtUtc = null;
                order.CompletedByUserId = null;
            }
            if (order.PatientVisit?.Status == VisitStatus.InConsultation)
                order.PatientVisit.Status = order.VerificationStatus == DiagnosticVerificationStatus.Verified
                    ? VisitStatus.ResultsReady : VisitStatus.WaitingForDiagnostics;
        }
        await db.SaveChangesAsync();
    }

    private static DiagnosticResult Result(DiagnosticService service, Guid technician, DateTime time) => new()
    {
        ResultText = "Các chỉ số trong khoảng tham chiếu", Conclusion = "Chưa ghi nhận bất thường", ResultedByUserId = technician, ResultedAtUtc = time,
        Values = service.Parameters.Where(p => p.IsActive).Select(p => new DiagnosticResultValue
        {
            DiagnosticServiceParameterId = p.Id, ParameterCode = p.Code, ParameterName = p.Name, Unit = p.Unit,
            RefLow = p.RefLow, RefHigh = p.RefHigh, CriticalLow = p.CriticalLow, CriticalHigh = p.CriticalHigh,
            NumericValue = ((p.RefLow ?? 0) + (p.RefHigh ?? p.RefLow ?? 0)) / 2, SortOrder = p.SortOrder,
        }).ToList(),
    };
}
