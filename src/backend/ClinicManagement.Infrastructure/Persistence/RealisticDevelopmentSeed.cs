using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.HealthPackages;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Pharmacy;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class RealisticDevelopmentSeed
{
    private sealed record PrescribedMedicine(string Code, int Quantity, string Dosage, string Frequency, int Days);
    private sealed record SpecialtyProfile(string ServiceCode, string Indication, PrescribedMedicine[] Medicines);
    private sealed record TodayOrder(string SpecialtyCode, string[] ServiceCodes, string Indication, int MinutesAgo);

    private static readonly Dictionary<string, SpecialtyProfile> Profiles = new()
    {
        ["SP01"] = new("LAB-CBC", "Kiểm tra công thức máu do mệt mỏi kéo dài", [new("MED01", 10, "1 viên/lần", "2 lần/ngày sau ăn", 5), new("MED26", 10, "1 viên/lần", "1 lần/ngày sau ăn sáng", 10)]),
        ["SP02"] = new("LAB-CBC", "Đánh giá tình trạng nhiễm trùng khi sốt", [new("MED01", 10, "1 viên/lần", "2 lần/ngày khi sốt", 5), new("MED23", 10, "1 viên/lần", "2 lần/ngày sau ăn", 5)]),
        ["SP03"] = new("US-ABD", "Siêu âm đánh giá vùng bụng dưới khi đau bụng kinh", [new("MED27", 30, "1 viên/lần", "1 lần/ngày sau ăn sáng", 30), new("MED09", 10, "1 viên/lần", "1 lần/ngày", 10)]),
        ["SP04"] = new("LAB-CBC", "Đánh giá phản ứng dị ứng toàn thân", [new("MED13", 10, "1 viên/lần", "1 lần/ngày buổi tối", 10), new("MED30", 1, "Bôi lớp mỏng", "2 lần/ngày", 14)]),
        ["SP05"] = new("IMG-CXR", "Ho kéo dài, loại trừ tổn thương phổi", [new("MED14", 3, "1 viên/lần", "1 lần/ngày", 3), new("MED22", 10, "1 gói/lần", "2 lần/ngày", 5)]),
        ["SP06"] = new("IMG-ECG", "Hồi hộp, kiểm tra nhịp tim", [new("MED08", 30, "1 viên/lần", "1 lần/ngày buổi sáng", 30), new("MED17", 30, "1 viên/lần", "1 lần/ngày buổi tối", 30)]),
        ["SP07"] = new("LAB-RFT", "Đánh giá chức năng thận trước khi dùng thuốc giảm đau", [new("MED03", 10, "1 viên/lần", "2 lần/ngày sau ăn", 5), new("MED27", 30, "1 viên/lần", "1 lần/ngày sau ăn sáng", 30)]),
        ["SP08"] = new("LAB-GLU", "Tê bì chân tay, kiểm tra đường huyết", [new("MED28", 14, "1 viên/lần", "2 lần/ngày", 7), new("MED26", 14, "1 viên/lần", "2 lần/ngày sau ăn", 7)]),
        ["SP09"] = new("LAB-GLU", "Theo dõi đường huyết lúc đói", [new("MED07", 30, "1 viên/lần", "2 lần/ngày sau ăn", 15), new("MED18", 15, "1 viên/lần", "1 lần/ngày trước ăn sáng", 15)]),
        ["SP10"] = new("LAB-GLU", "Mờ mắt, tầm soát đái tháo đường", [new("MED09", 10, "1 viên/lần", "1 lần/ngày", 10), new("MED26", 10, "1 viên/lần", "1 lần/ngày sau ăn sáng", 10)]),
        ["SP11"] = new("US-ABD", "Đau thượng vị, siêu âm ổ bụng tổng quát", [new("MED11", 10, "2 viên/lần", "2 lần/ngày", 3), new("MED19", 9, "1 viên/lần", "3 lần/ngày trước ăn", 3)]),
    };

    // Only one order exceeds the 30-minute waiting threshold when the seed runs.
    private static readonly TodayOrder[] TodayOrders =
    [
        new("SP08", ["IMG-ECG"], "Chóng mặt, hồi hộp từng cơn; loại trừ rối loạn nhịp tim", 42),
        new("SP09", ["LAB-GLU"], "Theo dõi đường huyết lúc đói ở người bệnh đái tháo đường", 10),
        new("SP03", ["LAB-CBC"], "Đánh giá thiếu máu trước khi kết luận phụ khoa", 18),
        new("SP11", ["LAB-LFT", "US-ABD"], "Đau thượng vị, kiểm tra chức năng gan và siêu âm ổ bụng", 24),
        new("SP05", ["LAB-CBC", "IMG-CXR"], "Ho kéo dài kèm sốt, loại trừ viêm phổi", 28),
    ];

    private static readonly Dictionary<string, (string Text, string Conclusion)> ImagingResults = new()
    {
        ["IMG-ECG"] = ("Nhịp xoang đều, tần số 78 lần/phút, trục trung gian", "Điện tâm đồ trong giới hạn bình thường"),
        ["IMG-CXR"] = ("Hai phế trường sáng, không thấy đám mờ khu trú; bóng tim không to", "Chưa phát hiện tổn thương tim phổi trên phim"),
        ["US-ABD"] = ("Gan, mật, tụy, lách và hai thận chưa thấy bất thường; không có dịch ổ bụng", "Siêu âm ổ bụng chưa phát hiện bất thường"),
        ["US-THY"] = ("Hai thùy tuyến giáp kích thước bình thường, nhu mô đồng nhất", "Tuyến giáp chưa phát hiện bất thường"),
        ["US-ECHO"] = ("Các buồng tim kích thước bình thường, chức năng tâm thu thất trái bảo tồn", "Siêu âm tim chưa phát hiện bất thường"),
    };

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

    internal static Patient PickPatient(IReadOnlyList<Patient> patients, ISet<(long DoctorId, DateOnly Date, long PatientId)> taken,
        long doctorId, DateOnly date, int start, string specialtyCode, bool onePerDay = false)
    {
        for (var offset = 0; offset < patients.Count; offset++)
        {
            var patient = patients[(Math.Abs(start) + offset) % patients.Count];
            if (!DemoPatientDevelopmentSeed.Allows(patient, specialtyCode, date)) continue;
            if (onePerDay && taken.Any(booking => booking.Date == date && booking.PatientId == patient.Id)) continue;
            if (taken.Add((doctorId, date, patient.Id))) return patient;
        }
        throw new InvalidOperationException($"Không còn bệnh nhân phù hợp cho bác sĩ #{doctorId} ngày {date:dd/MM/yyyy}.");
    }

    internal static async Task DispenseAsync(AppDbContext db, IDateTimeProvider clock, Prescription prescription, Guid pharmacist, DateTime dispensedAt)
    {
        var allocator = new MedicineBatchAllocator(db, clock);
        foreach (var item in prescription.Items)
        {
            var medicine = await db.Medicines.SingleAsync(m => m.Id == item.MedicineId);
            await allocator.Deduct(medicine, item.Quantity, MedicineStockTransactionType.Dispense,
                prescription.Id, $"Cấp phát thuốc cho đơn #{prescription.Id}", pharmacist);
        }
        foreach (var entry in db.MedicineStockTransactions.Local.Where(t => t.PrescriptionId == prescription.Id))
            entry.CreatedAt = dispensedAt;
        prescription.Status = PrescriptionStatus.Dispensed;
        prescription.DispensedAt = dispensedAt;
        prescription.DispensedByUserId = pharmacist;
    }

    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid receptionist, Guid technician, Guid supervisor, Guid pharmacist)
    {
        if (await db.SystemAuditLogs.AnyAsync(l => l.Action == "InitializeStaffDemo" && l.EntityId == "staff-v1"))
        {
            await SynchronizeExistingAsync(db);
            return;
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var facility = await db.Facilities.SingleAsync(f => f.Code == "CS01");
        var departments = await db.Departments.Include(d => d.Specialty).Where(d => d.FacilityId == facility.Id).ToListAsync();
        var outpatient = departments.Single(d => d.Code == "KKB");
        var doctors = await db.Doctors.Include(d => d.DoctorSpecialties).ThenInclude(ds => ds.Specialty).OrderBy(d => d.Id).ToListAsync();
        var patients = await db.Patients.OrderBy(p => p.Id).ToListAsync();
        var services = await db.DiagnosticServices.Include(s => s.Parameters).ToDictionaryAsync(s => s.Code);
        var medicines = await db.Medicines.ToDictionaryAsync(m => m.Code);
        var packages = await db.HealthPackages.OrderBy(p => p.Code).ToListAsync();
        var packageIndex = 0;
        var packagePurchases = await db.HealthPackageRegistrations.ToListAsync();
        var today = DemoSeedTimeline.ActivityDate(clock);
        var taken = (await db.Appointments.Where(a => a.AppointmentDate >= today.AddDays(-30) && a.AppointmentDate <= today)
                .Select(a => new { a.DoctorId, a.AppointmentDate, a.PatientId }).ToListAsync())
            .Select(a => (a.DoctorId, a.AppointmentDate, a.PatientId)).ToHashSet();
        Specialty SpecialtyOf(Doctor doctor) => (doctor.DoctorSpecialties.FirstOrDefault(ds => ds.IsPrimary) ?? doctor.DoctorSpecialties.First()).Specialty;
        Department DepartmentOf(Specialty specialty) => departments.FirstOrDefault(d => d.SpecialtyId == specialty.Id) ?? outpatient;

        for (var i = 0; i < 30; i++)
        {
            var date = today.AddDays(-i);
            if (DemoSeedCalendar.IsClosed(date) && i != 0) continue;
            date = DemoSeedCalendar.PreviousOpen(date);
            var time = DemoSeedTimeline.BeforeNow(clock, date, 9, (i % 4) * 10);
            var status = i is 1 or 7 or 13 ? InvoiceStatus.Unpaid : i is 3 or 8 ? InvoiceStatus.Cancelled : InvoiceStatus.Paid;
            var invoice = new Invoice
            {
                InvoiceCode = BusinessDisplay.RandomCode("INV", date, 8),
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
                var patient = DemoRevenueDevelopmentSeed.PickPatient(patients, package, date, packagePurchases, i);
                invoice.PatientId = patient.Id;
                invoice.SourceType = InvoiceSourceType.HealthPackageRegistration;
                invoice.HealthPackageRegistration = new HealthPackageRegistration
                {
                    RegistrationCode = BusinessDisplay.RandomCode("REG", date.AddDays(-2), 8), PatientId = patient.Id,
                    HealthPackageId = package.Id, PreferredDate = date, ContactPhone = patient.PhoneNumber ?? "0900000004",
                    Status = status == InvoiceStatus.Cancelled ? HealthPackageRegistrationStatus.Cancelled : HealthPackageRegistrationStatus.Confirmed, CreatedAt = time.AddDays(-2),
                };
                db.HealthPackageRegistrations.Add(invoice.HealthPackageRegistration);
                packagePurchases.Add(invoice.HealthPackageRegistration);
                await db.SaveChangesAsync();
                invoice.Items.Add(Item(package.Code, package.Name, package.Price, 1, "HealthPackage", invoice.HealthPackageRegistration.Id));
            }
            else
            {
                var doctor = doctors[i % doctors.Count];
                var specialty = SpecialtyOf(doctor);
                var profile = Profiles[specialty.SpecialtyCode];
                var patient = PickPatient(patients, taken, doctor.Id, date, i, specialty.SpecialtyCode);
                invoice.PatientId = patient.Id;
                var visit = new PatientVisit
                {
                    VisitCode = $"VIS-{date:yyyyMMdd}-{100 + i:D4}", PatientId = patient.Id, FacilityId = facility.Id,
                    DepartmentId = DepartmentOf(specialty).Id, AssignedDoctorId = doctor.Id, VisitDate = date,
                    QueueNumber = 100 + i, ChiefComplaint = Reason(specialty.SpecialtyCode, i), Status = VisitStatus.Completed,
                    CreatedByUserId = receptionist, CreatedAtUtc = time.AddHours(-1), CheckedInAtUtc = time.AddHours(-1),
                    ConsultationStartedAtUtc = time.AddMinutes(-45), CompletedAtUtc = time.AddMinutes(-15),
                };
                invoice.PatientVisit = visit;
                db.PatientVisits.Add(visit);
                await db.SaveChangesAsync();
                invoice.Items.Add(Item(specialty.SpecialtyCode, $"Phí khám {specialty.Name.ToLowerInvariant()}", specialty.ConsultationFee, 1, "Consultation", visit.Id));
                if (i % 4 >= 1)
                {
                    var service = services[profile.ServiceCode];
                    var order = new DiagnosticOrder
                    {
                        OrderCode = $"DX-{date:yyyyMMdd}-{1000 + i:D4}", PatientVisit = visit, PatientId = patient.Id,
                        OrderingDoctorId = doctor.Id, FacilityId = facility.Id, ClinicalIndication = profile.Indication,
                        Status = DiagnosticOrderStatus.Completed, OrderedAtUtc = time.AddMinutes(-40), StartedAtUtc = time.AddMinutes(-35),
                        CompletedAtUtc = time.AddMinutes(-20), StartedByUserId = technician, CompletedByUserId = technician,
                        Items = [new DiagnosticOrderItem { DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.Completed,
                            Result = Result(service, technician, time.AddMinutes(-20)) }],
                    };
                    db.DiagnosticOrders.Add(order);
                    await db.SaveChangesAsync();
                    invoice.Items.Add(Item(service.Code, service.Name, service.Price!.Value, 1, "DiagnosticItem", order.Items.Single().Id));
                }
                if (i % 4 == 2 || i <= 1)
                {
                    var prescription = new Prescription
                    {
                        PatientVisit = visit, PatientId = patient.Id, DoctorId = doctor.Id,
                        Status = PrescriptionStatus.Issued, CreatedAt = time.AddMinutes(-10),
                        Items = profile.Medicines.Select(m => new PrescriptionItem
                        {
                            MedicineId = medicines[m.Code].Id, Quantity = m.Quantity, Dosage = m.Dosage, Frequency = m.Frequency, DurationDays = m.Days
                        }).ToList(),
                    };
                    db.Prescriptions.Add(prescription);
                    await db.SaveChangesAsync();
                    foreach (var item in prescription.Items)
                    {
                        var medicine = medicines.Values.Single(m => m.Id == item.MedicineId);
                        invoice.Items.Add(Item(medicine.Code, medicine.Name, medicine.UnitPrice!.Value, item.Quantity, "PrescriptionItem:v2", prescription.Id * 4294967296L + medicine.Id));
                    }
                    if (i >= 2)
                        await DispenseAsync(db, clock, prescription, pharmacist, time.AddMinutes(45));
                }
            }
            invoice.Subtotal = invoice.TotalAmount = invoice.Items.Sum(item => item.LineTotal);
            if (status == InvoiceStatus.Cancelled)
                foreach (var item in invoice.Items) item.IsCancelled = true;
            if (status == InvoiceStatus.Paid)
                invoice.Payments.Add(new Payment
                {
                    PaymentCode = BusinessDisplay.RandomCode("PAY", date, 8), Amount = invoice.TotalAmount,
                    Method = i % 2 == 0 ? PaymentMethod.Cash : PaymentMethod.ManualBankTransfer,
                    ReceivedAtUtc = invoice.PaidAtUtc!.Value, ReceivedByUserId = receptionist, Status = PaymentStatus.Succeeded,
                });
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var now = DemoSeedTimeline.ActivityNow(clock);
        var specimen = 0;
        for (var i = 0; i < TodayOrders.Length; i++)
        {
            var definition = TodayOrders[i];
            var doctor = doctors.First(d => SpecialtyOf(d).SpecialtyCode == definition.SpecialtyCode);
            var specialty = SpecialtyOf(doctor);
            var patient = PickPatient(patients, taken, doctor.Id, today, i + 5, definition.SpecialtyCode);
            var orderedAt = now.AddMinutes(-definition.MinutesAgo);
            var checkedInAt = orderedAt.AddMinutes(-12);
            var visit = new PatientVisit
            {
                VisitCode = $"VIS-{today:yyyyMMdd}-{200 + i:D4}", PatientId = patient.Id, FacilityId = facility.Id,
                DepartmentId = DepartmentOf(specialty).Id, AssignedDoctorId = doctor.Id, VisitDate = today, QueueNumber = 200 + i,
                ChiefComplaint = Reason(definition.SpecialtyCode, i + 1), Status = i >= 3 ?VisitStatus.ResultsReady : VisitStatus.WaitingForDiagnostics,
                CreatedByUserId = receptionist, CreatedAtUtc = checkedInAt, CheckedInAtUtc = checkedInAt, ConsultationStartedAtUtc = checkedInAt.AddMinutes(4),
            };
            var started = i >= 2;
            var resulted = i >= 3;
            var order = new DiagnosticOrder
            {
                OrderCode = $"DX-{today:yyyyMMdd}-{2000 + i:D4}", PatientVisit = visit, PatientId = patient.Id,
                OrderingDoctorId = doctor.Id, FacilityId = facility.Id, ClinicalIndication = definition.Indication,
                OrderedAtUtc = orderedAt, Status = resulted ? DiagnosticOrderStatus.Completed : started ? DiagnosticOrderStatus.InProgress : DiagnosticOrderStatus.Ordered,
                StartedAtUtc = started ? orderedAt.AddMinutes(2) : null, StartedByUserId = started ? technician : null,
                CompletedAtUtc = resulted ? orderedAt.AddMinutes(i == 4 ? 22 : 20) : null, CompletedByUserId = resulted ? technician : null,
                VerificationStatus = i == 4 ? DiagnosticVerificationStatus.Verified : DiagnosticVerificationStatus.NotRequired,
                SubmittedForVerificationAtUtc = i == 4 ? orderedAt.AddMinutes(18) : null, SubmittedForVerificationByUserId = i == 4 ? technician : null,
                VerifiedAtUtc = i == 4 ? orderedAt.AddMinutes(22) : null, VerifiedByUserId = i == 4 ? supervisor : null,
            };
            foreach (var service in definition.ServiceCodes.Select(code => services[code]))
            {
                var hasSpecimen = service.SpecimenType != null;
                var collected = hasSpecimen && started;
                order.Items.Add(new DiagnosticOrderItem
                {
                    DiagnosticServiceId = service.Id,
                    Status = resulted ? DiagnosticItemStatus.Completed : started ? DiagnosticItemStatus.InProgress : DiagnosticItemStatus.Ordered,
                    SpecimenStatus = !hasSpecimen ? SpecimenStatus.NotRequired : collected ? SpecimenStatus.Received : SpecimenStatus.PendingCollection,
                    SpecimenBarcode = collected ? $"SP-{today:yyMMdd}-{++specimen:D4}" : null,
                    CollectedAtUtc = collected ? orderedAt.AddMinutes(4) : null, CollectedByUserId = collected ? technician : null,
                    ReceivedAtUtc = collected ? orderedAt.AddMinutes(6) : null, ReceivedByUserId = collected ? technician : null,
                    Result = resulted ? Result(service, technician, orderedAt.AddMinutes(15)) : null,
                });
            }
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
            .Where(o => o.PatientVisit != null && o.PatientVisit.QueueNumber >= 200 && o.PatientVisit.QueueNumber < 205)
            .ToListAsync();
        foreach (var order in orders)
        {
            if (order.PatientVisit?.QueueNumber == 203 &&
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

    internal static DiagnosticResult Result(DiagnosticService service, Guid technician, DateTime time)
    {
        var imaging = ImagingResults.GetValueOrDefault(service.Code);
        return new()
        {
            ResultText = service.Parameters.Count == 0 && imaging.Text != null ? imaging.Text : "Các chỉ số trong khoảng tham chiếu",
            Conclusion = service.Parameters.Count == 0 && imaging.Conclusion != null ? imaging.Conclusion : "Chưa ghi nhận bất thường",
            ResultedByUserId = technician, ResultedAtUtc = time,
            Values = service.Parameters.Where(p => p.IsActive).Select(p => new DiagnosticResultValue
            {
                DiagnosticServiceParameterId = p.Id, ParameterCode = p.Code, ParameterName = p.Name, Unit = p.Unit,
                RefLow = p.RefLow, RefHigh = p.RefHigh, CriticalLow = p.CriticalLow, CriticalHigh = p.CriticalHigh,
                NumericValue = ((p.RefLow ?? 0) + (p.RefHigh ?? p.RefLow ?? 0)) / 2, SortOrder = p.SortOrder,
            }).ToList(),
        };
    }
}
