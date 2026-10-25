using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class ExpiryDemoSeed
{
    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid actor)
    {
        if (await db.SystemAuditLogs.AnyAsync(l => l.Action == "InitializeExpiryDemo")) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var patient = await db.Patients.Where(p => p.UserId != null).OrderByDescending(p => p.Id).FirstAsync();
        var doctor = await db.Doctors.Include(d => d.DoctorSpecialties).Where(d => d.IsActive).OrderBy(d => d.Id).FirstAsync();
        var specialty = doctor.DoctorSpecialties.First().SpecialtyId;
        var department = await db.Departments.Where(d => d.SpecialtyId == specialty && d.IsActive).OrderBy(d => d.Id).FirstAsync();
        var date = DemoSeedCalendar.PreviousOpen(clock.VietnamToday.AddDays(-10));
        var at = clock.ConvertVietnamToUtc(date.ToDateTime(new TimeOnly(9, 0)));

        DateOnly OpenDate(int offset)
        {
            var result = date;
            for (var i = 0; i < Math.Abs(offset); i++)
            {
                result = result.AddDays(Math.Sign(offset));
                if (DemoSeedCalendar.IsClosed(result)) result = result.AddDays(Math.Sign(offset));
            }
            return result;
        }

        async Task<Appointment> AppointmentAsync(AppointmentStatus status, int days)
        {
            var day = OpenDate(days);
            var occupied = await db.Appointments.Where(a => a.DoctorId == doctor.Id && a.AppointmentDate == day &&
                AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status)).Select(a => a.StartTime).ToListAsync();
            var start = Enumerable.Range(0, 8).Select(n => new TimeOnly(7, 0).AddMinutes(n * 30)).First(t => !occupied.Contains(t));
            var slot = await db.AppointmentSlots.FirstOrDefaultAsync(s => s.DoctorId == doctor.Id && s.SlotDate == day && s.StartTime == start);
            if (slot == null)
            {
                slot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = day, StartTime = start, EndTime = start.AddMinutes(30) };
                db.AppointmentSlots.Add(slot);
            }
            slot.IsBooked = AppointmentStatusExtensions.HoldingSlotStatuses.Contains(status);
            var a = new Appointment
            {
                AppointmentCode = BusinessDisplay.RandomCode("APT", day, 8), PatientId = patient.Id, DoctorId = doctor.Id,
                SpecialtyId = specialty, FacilityId = department.FacilityId, AppointmentSlot = slot, AppointmentDate = day,
                StartTime = start, EndTime = slot.EndTime, Status = status, Reason = "Minh họa xử lý quá hạn nghiệp vụ"
            };
            a.Histories.Add(new AppointmentHistory { Action = AppointmentHistoryAction.Created, NewStatus = status, CreatedAt = clock.ConvertVietnamToUtc(day.ToDateTime(new TimeOnly(6, 0))), PerformedByUserId = patient.UserId, Note = "Tạo lịch minh họa quá hạn" });
            db.Appointments.Add(a);
            await db.SaveChangesAsync();
            return a;
        }

        await AppointmentAsync(AppointmentStatus.Pending, 0);
        for (var i = 1; i <= 3; i++) await AppointmentAsync(AppointmentStatus.Confirmed, i);
        var change = await AppointmentAsync(AppointmentStatus.PendingCancellation, 4);
        db.AppointmentChangeRequests.Add(new AppointmentChangeRequest
        {
            AppointmentId = change.Id, RequestType = AppointmentChangeRequestType.Cancellation,
            OriginalAppointmentStatus = AppointmentStatus.Confirmed, RequestedByUserId = patient.UserId!.Value,
            CreatedAt = at.AddDays(-1), Reason = "Xin hủy lịch khám đã đăng ký"
        });
        var completed = await AppointmentAsync(AppointmentStatus.Completed, -2);
        var completedAt = clock.ConvertVietnamToUtc(completed.AppointmentDate.ToDateTime(completed.EndTime));
        completed.VisitSummary = new VisitSummary
        {
            DoctorId = doctor.Id, ChiefComplaint = "Đau đầu từng cơn", DiagnosisCode = "R51", Diagnosis = "Đau đầu",
            Summary = "Đã khám, hướng dẫn theo dõi và tái khám.", TreatmentPlan = "Theo dõi triệu chứng, nghỉ ngơi hợp lý.",
            CreatedAtUtc = completedAt, CompletedAtUtc = completedAt
        };
        completed.VitalSigns = new AppointmentVitalSigns
        {
            Temperature = 36.7m, BloodPressureSystolic = 120, BloodPressureDiastolic = 80, HeartRate = 76,
            RespiratoryRate = 18, Weight = 58, Height = 163, SpO2 = 98, RecordedAtUtc = completedAt,
            RecordedByUserId = doctor.UserId
        };
        db.RevisitRequests.Add(new RevisitRequest
        {
            AppointmentId = completed.Id, PatientId = patient.Id, DoctorId = doctor.Id,
            SuggestedDate = date, Note = "Đề xuất tái khám minh họa quá hạn"
        });
        var visits = new List<PatientVisit>();
        foreach (var status in new[] { VisitStatus.WaitingForDoctor, VisitStatus.InConsultation })
        {
            var visitDate = OpenDate(-4 + visits.Count);
            var checkedInAt = clock.ConvertVietnamToUtc(visitDate.ToDateTime(new TimeOnly(9, 0)));
            var visit = new PatientVisit
            {
                VisitCode = $"VIS-{visitDate:yyyyMMdd}-{900 + visits.Count:D4}", PatientId = patient.Id, FacilityId = department.FacilityId,
                DepartmentId = department.Id, AssignedDoctorId = doctor.Id, VisitDate = visitDate, QueueNumber = 900 + visits.Count,
                Status = status, CreatedByUserId = actor, CheckedInAtUtc = checkedInAt, CreatedAtUtc = checkedInAt,
                ConsultationStartedAtUtc = status == VisitStatus.InConsultation ? checkedInAt.AddMinutes(10) : null,
                ChiefComplaint = "Dữ liệu minh họa lượt khám qua ngày"
            };
            visits.Add(visit);
            db.PatientVisits.Add(visit);
        }
        var service = await db.DiagnosticServices.Where(s => s.IsActive).OrderBy(s => s.Id).FirstAsync();
        db.DiagnosticOrders.Add(new DiagnosticOrder
        {
            OrderCode = $"DX-{date:yyyyMMdd}-0900", PatientVisit = visits[1], PatientId = patient.Id,
            OrderingDoctorId = doctor.Id, FacilityId = department.FacilityId, OrderedAtUtc = at,
            ClinicalIndication = "Chỉ định minh họa quá hạn thực hiện",
            Items = [new DiagnosticOrderItem { DiagnosticServiceId = service.Id }]
        });
        var medicine = await db.Medicines.Where(m => m.IsActive).OrderBy(m => m.Id).FirstAsync();
        completed.Prescription = new Prescription
        {
            PatientId = patient.Id, DoctorId = doctor.Id, Status = PrescriptionStatus.Issued, CreatedAt = completedAt,
            Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 1, Dosage = "1 viên", Frequency = "1 lần/ngày", DurationDays = 1 }]
        };
        db.Prescriptions.Add(new Prescription
        {
            PatientVisit = visits[1], PatientId = patient.Id, DoctorId = doctor.Id, Status = PrescriptionStatus.Issued,
            CreatedAt = visits[1].CheckedInAtUtc.AddMinutes(20), Notes = "Đơn minh họa hết hạn lĩnh thuốc",
            Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 1, Dosage = "1 viên", Frequency = "1 lần/ngày", DurationDays = 1 }]
        });
        await db.SaveChangesAsync();
        var invoice = new Invoice
        {
            InvoiceCode = BusinessDisplay.RandomCode("INV", date, 7), PatientVisit = visits[1], PatientId = patient.Id,
            SourceType = InvoiceSourceType.Appointment, CreatedByUserId = actor, CreatedAtUtc = at,
            Subtotal = 150000, TotalAmount = 150000,
            Items = [new InvoiceItem { ItemCode = "EXAM", Description = "Phí khám", Quantity = 1, UnitPrice = 150000, LineTotal = 150000, ReferenceType = "Consultation", ReferenceId = visits[1].Id }]
        };
        db.Invoices.Add(invoice);
        var package = await db.HealthPackages.Where(p => p.IsActive).OrderBy(p => p.Id).FirstAsync();
        db.HealthPackageRegistrations.Add(new HealthPackageRegistration
        {
            RegistrationCode = BusinessDisplay.RandomCode("REG", date, 7), HealthPackageId = package.Id, PatientId = patient.Id,
            PreferredDate = date, ContactPhone = patient.PhoneNumber ?? "0900000000", CreatedAt = at
        });
        db.DoctorLeaveRequests.Add(new DoctorLeaveRequest
        {
            DoctorId = doctor.Id, StartDateTime = date.ToDateTime(new TimeOnly(8, 0)),
            EndDateTime = date.ToDateTime(new TimeOnly(17, 0)), Reason = "Đơn nghỉ minh họa quá hạn"
        });
        db.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = actor, Action = "InitializeExpiryDemo", EntityName = "DevelopmentData", EntityId = "expiry-v1",
            Description = "Khởi tạo ví dụ cho 12 quy tắc hết hạn", CreatedAt = clock.UtcNow
        });
        await db.SaveChangesAsync();
        db.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = actor, Action = "CreateInvoice", EntityName = nameof(Invoice), EntityId = invoice.Id.ToString(),
            Description = $"Lập hóa đơn {invoice.InvoiceCode}: {BusinessDisplay.Money(invoice.TotalAmount)}", CreatedAt = invoice.CreatedAtUtc
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
