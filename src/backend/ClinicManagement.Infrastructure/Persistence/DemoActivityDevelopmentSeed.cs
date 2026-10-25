using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DemoActivityDevelopmentSeed
{
    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid receptionist,
        Guid technician, Guid supervisor, Guid pharmacist)
    {
        await SeedPaymentsAsync(db, clock, receptionist);
        var users = await db.Patients.ToDictionaryAsync(p => p.Id, p => p.UserId);
        void Event(Guid? actor, string action, string entity, long id, string code, DateTime time,
            long patientId, NotificationType type, string title, string route, string? description = null)
        {
            db.SystemAuditLogs.Add(new SystemAuditLog { UserId = actor, Action = action, EntityName = entity,
                EntityId = id.ToString(), Description = description ?? $"{title}: {code}", CreatedAt = time });
            if (!users.TryGetValue(patientId, out var userId) || userId == null) return;
            var read = (id + (int)type + action.Length) % 2 == 0;
            db.Notifications.Add(new Notification { UserId = userId.Value, Type = type, Title = title,
                Message = $"{title}: {code}.", RelatedEntityType = entity, RelatedEntityId = id.ToString(), Route = route,
                DedupeKey = $"demo-v3-{entity}-{id}-{action}", CreatedAtUtc = time, IsRead = read,
                ReadAtUtc = read ? time.AddMinutes(10) : null });
        }

        var appointments = await db.Appointments.Include(a => a.Histories).Include(a => a.Doctor).ToListAsync();
        foreach (var appointment in appointments)
        {
            foreach (var history in appointment.Histories)
            {
                var created = history.Action == AppointmentHistoryAction.Created;
                var confirmed = history.Action == AppointmentHistoryAction.Confirmed;
                var completed = history.Action == AppointmentHistoryAction.Completed;
                if (!created && !confirmed && !completed) continue;
                if (confirmed) history.PerformedByUserId = receptionist;
                Event(history.PerformedByUserId, created ? "CreateAppointment" : confirmed ? "ConfirmAppointment" : "CompleteConsultation",
                    nameof(Appointment), appointment.Id, appointment.AppointmentCode, history.CreatedAt, appointment.PatientId,
                    NotificationType.Appointment, created ? "Đặt lịch thành công" : confirmed ? "Lịch hẹn đã xác nhận" : "Đã hoàn tất khám",
                    "/patient/appointments");
            }
        }
        foreach (var visit in await db.PatientVisits.Include(v => v.AssignedDoctor).ToListAsync())
        {
            Event(receptionist, "CheckInPatient", nameof(PatientVisit), visit.Id, visit.VisitCode, visit.CheckedInAtUtc,
                visit.PatientId, NotificationType.Appointment, "Đã tiếp nhận tại phòng khám", "/patient/appointments");
            if (visit.CompletedAtUtc.HasValue)
                Event(visit.AssignedDoctor!.UserId, "CompleteConsultation", nameof(PatientVisit), visit.Id, visit.VisitCode,
                    visit.CompletedAtUtc.Value, visit.PatientId, NotificationType.Appointment, "Đã hoàn tất khám", "/patient/profile");
        }
        foreach (var order in await db.DiagnosticOrders.Include(o => o.OrderingDoctor).ToListAsync())
        {
            Event(order.OrderingDoctor.UserId, "CreateDiagnosticOrder", nameof(DiagnosticOrder), order.Id, order.OrderCode,
                order.OrderedAtUtc, order.PatientId, NotificationType.Diagnostic, "Đã chỉ định cận lâm sàng", "/patient/diagnostic-results");
            if (order.SubmittedForVerificationAtUtc.HasValue)
                Event(technician, "SubmitDiagnosticResults", nameof(DiagnosticOrder), order.Id, order.OrderCode,
                    order.SubmittedForVerificationAtUtc.Value, order.PatientId, NotificationType.Diagnostic, "Kết quả đang chờ duyệt", "/patient/diagnostic-results");
            if (order.VerifiedAtUtc.HasValue)
                db.SystemAuditLogs.Add(new SystemAuditLog { UserId = supervisor, Action = "DiagnosticResultsVerified",
                    EntityName = nameof(DiagnosticOrder), EntityId = order.Id.ToString(), Description = $"Duyệt kết quả {order.OrderCode}", CreatedAt = order.VerifiedAtUtc.Value });
            if (order.ReviewedAtUtc.HasValue)
                Event(order.OrderingDoctor.UserId, "ReviewDiagnosticResults", nameof(DiagnosticOrder), order.Id, order.OrderCode,
                    order.ReviewedAtUtc.Value, order.PatientId, NotificationType.Diagnostic, "Đã có kết quả cận lâm sàng", "/patient/diagnostic-results");
        }
        foreach (var invoice in await db.Invoices.Include(i => i.Payments).ToListAsync())
        {
            Event(receptionist, "CreateInvoice", nameof(Invoice), invoice.Id, invoice.InvoiceCode, invoice.CreatedAtUtc,
                invoice.PatientId, NotificationType.Invoice, "Đã lập hóa đơn", "/patient/invoices",
                $"Lập hóa đơn {invoice.InvoiceCode}: {BusinessDisplay.Money(invoice.TotalAmount)}");
            foreach (var payment in invoice.Payments.Where(p => p.Status == PaymentStatus.Succeeded))
                Event(receptionist, "ReceivePayment", nameof(Payment), payment.Id, payment.PaymentCode, payment.ReceivedAtUtc,
                    invoice.PatientId, NotificationType.Payment, "Thanh toán thành công", "/patient/invoices",
                    $"Thu tiền hóa đơn {invoice.InvoiceCode}: {BusinessDisplay.Money(payment.Amount)}");
        }
        foreach (var prescription in await db.Prescriptions.Include(p => p.Doctor).ToListAsync())
        {
            Event(prescription.Doctor!.UserId, "IssuePrescription", nameof(Prescription), prescription.Id,
                BusinessDisplay.PrescriptionCode(prescription, clock), prescription.CreatedAt,
                prescription.PatientId, NotificationType.Prescription, "Đã kê đơn thuốc", "/patient/prescriptions");
            if (prescription.DispensedAt.HasValue)
                Event(pharmacist, "DispensePrescription", nameof(Prescription), prescription.Id, BusinessDisplay.PrescriptionCode(prescription, clock),
                    prescription.DispensedAt.Value, prescription.PatientId, NotificationType.Prescription, "Đã cấp thuốc", "/patient/prescriptions");
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedPaymentsAsync(AppDbContext db, IDateTimeProvider clock, Guid receptionist)
    {
        var packages = await db.HealthPackages.OrderBy(p => p.Code).ToListAsync();
        var patients = await db.Patients.OrderBy(p => p.Id).ToListAsync();
        var purchases = await db.HealthPackageRegistrations.ToListAsync();
        var random = new Random(261024);
        for (var day = 0; day < 30; day++)
        {
            var date = clock.VietnamToday.AddDays(-day);
            if (DemoSeedCalendar.IsClosed(date)) continue;
            var start = RealisticDevelopmentSeed.At(clock, date, 0);
            var end = start.AddDays(1);
            var paid = await db.Payments.CountAsync(p => p.Status == PaymentStatus.Succeeded && p.ReceivedAtUtc >= start && p.ReceivedAtUtc < end);
            var desired = random.Next(2, 7);
            for (var n = paid; n < desired; n++)
            {
                var choices = packages.Select(package => new { Package = package,
                    Patient = DemoRevenueDevelopmentSeed.FindPatient(patients, package, date, purchases, day + n) })
                    .Where(choice => choice.Patient != null).ToList();
                var choice = choices[random.Next(choices.Count)];
                var package = choice.Package;
                var patient = choice.Patient!;
                var time = RealisticDevelopmentSeed.At(clock, date, 8 + n / 2, n % 2 * 25);
                var invoice = new Invoice
                {
                    InvoiceCode = BusinessDisplay.RandomCode("INV", date, 7), PatientId = patient.Id,
                    SourceType = InvoiceSourceType.HealthPackageRegistration, Status = InvoiceStatus.Paid,
                    CreatedByUserId = receptionist, CreatedAtUtc = time, PaidAtUtc = time.AddMinutes(10), PaidByUserId = receptionist,
                    Subtotal = package.Price, TotalAmount = package.Price,
                    HealthPackageRegistration = new HealthPackageRegistration { RegistrationCode = BusinessDisplay.RandomCode("REG", date.AddDays(-2), 7),
                        PatientId = patient.Id, HealthPackageId = package.Id, PreferredDate = date, ContactPhone = patient.PhoneNumber!,
                        Status = HealthPackageRegistrationStatus.Confirmed, CreatedAt = time.AddDays(-2), UpdatedAt = time.AddDays(-1) },
                    Payments = [new Payment { PaymentCode = BusinessDisplay.RandomCode("PAY", date, 8), Amount = package.Price,
                        Method = n % 2 == 0 ? PaymentMethod.Cash : PaymentMethod.ManualBankTransfer, Status = PaymentStatus.Succeeded,
                        ReceivedAtUtc = time.AddMinutes(10), ReceivedByUserId = receptionist }],
                };
                db.HealthPackageRegistrations.Add(invoice.HealthPackageRegistration!);
                purchases.Add(invoice.HealthPackageRegistration!);
                await db.SaveChangesAsync();
                invoice.Items.Add(new InvoiceItem { ItemCode = package.Code, Description = package.Name, Quantity = 1,
                    UnitPrice = package.Price, LineTotal = package.Price, ReferenceType = "HealthPackage", ReferenceId = invoice.HealthPackageRegistration!.Id });
                db.Invoices.Add(invoice);
            }
        }
        await db.SaveChangesAsync();
    }
}
