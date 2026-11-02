using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class TwentyFourSevenPatientHistorySeed
{
    internal static async Task SeedAsync(AppDbContext db, IDateTimeProvider clock, Guid receptionist, Guid technician, Guid supervisor, Guid pharmacist)
    {
        if (!db.OperatingHours.IsTwentyFourSeven || await db.SystemAuditLogs.AnyAsync(a => a.Action == "Initialize24SevenPatientHistory")) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var patient = await db.Patients.SingleAsync(p => p.Email == "patient@cliniccare.local");
        var doctor = await db.Doctors.Include(d => d.DoctorSpecialties).ThenInclude(d => d.Specialty)
            .SingleAsync(d => db.Users.Any(u => u.Id == d.UserId && u.Email == "doctor@cliniccare.local"));
        var specialty = doctor.DoctorSpecialties.Single(s => s.IsPrimary).Specialty;
        var department = await db.Departments.SingleAsync(d => d.IsActive && d.SpecialtyId == specialty.Id);
        var service = await db.DiagnosticServices.Include(s => s.Parameters).SingleAsync(s => s.Code == "LAB-CBC");
        var medicine = await db.Medicines.SingleAsync(m => m.Code == "MED01");
        for (var index = 0; index < 11; index++)
        {
            var date = clock.VietnamToday.AddDays(-12 - index);
            var time = RealisticDevelopmentSeed.At(clock, date, 9);
            var visit = new PatientVisit
            {
                VisitCode = $"VIS-{date:yyyyMMdd}-3000", PatientId = patient.Id, FacilityId = department.FacilityId,
                DepartmentId = department.Id, AssignedDoctorId = doctor.Id, VisitDate = date, QueueNumber = 3000,
                Status = VisitStatus.Completed, ChiefComplaint = RealisticDevelopmentSeed.Reason(specialty.SpecialtyCode, index),
                CreatedByUserId = receptionist, CreatedAtUtc = time, CheckedInAtUtc = time,
                ConsultationStartedAtUtc = time.AddMinutes(5), CompletedAtUtc = time.AddMinutes(45)
            };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            var invoice = new Invoice
            {
                InvoiceCode = BusinessDisplay.RandomCode("INV", date, 8), PatientId = patient.Id, PatientVisitId = visit.Id,
                SourceType = InvoiceSourceType.Appointment, Status = InvoiceStatus.Paid, CreatedByUserId = receptionist,
                CreatedAtUtc = time.AddMinutes(50), PaidAtUtc = time.AddHours(1), PaidByUserId = receptionist,
                Items = [Item(specialty.SpecialtyCode, $"Phí khám {specialty.Name.ToLowerInvariant()}", specialty.ConsultationFee, "Consultation", visit.Id)]
            };
            if (index < 5)
            {
                var item = new DiagnosticOrderItem
                {
                    DiagnosticServiceId = service.Id, Status = DiagnosticItemStatus.Completed,
                    SpecimenStatus = SpecimenStatus.Received, SpecimenBarcode = $"SP-{date:yyMMdd}-P0001",
                    CollectedAtUtc = time.AddMinutes(10), CollectedByUserId = technician,
                    ReceivedAtUtc = time.AddMinutes(12), ReceivedByUserId = technician,
                    Result = RealisticDevelopmentSeed.Result(service, technician, time.AddMinutes(25))
                };
                db.DiagnosticOrders.Add(new DiagnosticOrder
                {
                    OrderCode = $"DX-{date:yyyyMMdd}-3000", PatientVisitId = visit.Id, PatientId = patient.Id,
                    OrderingDoctorId = doctor.Id, FacilityId = department.FacilityId, PerformingDepartmentId = department.Id,
                    ClinicalIndication = "Kiểm tra công thức máu do mệt mỏi kéo dài", Status = DiagnosticOrderStatus.Completed,
                    OrderedAtUtc = time.AddMinutes(5), StartedAtUtc = time.AddMinutes(15), StartedByUserId = technician,
                    CompletedAtUtc = time.AddMinutes(30), CompletedByUserId = technician,
                    VerificationStatus = DiagnosticVerificationStatus.Verified, SubmittedForVerificationAtUtc = time.AddMinutes(26),
                    SubmittedForVerificationByUserId = technician, VerifiedAtUtc = time.AddMinutes(30), VerifiedByUserId = supervisor,
                    ReviewedAtUtc = time.AddMinutes(35), ReviewedByDoctorId = doctor.Id, Items = [item]
                });
                await db.SaveChangesAsync();
                invoice.Items.Add(Item(service.Code, service.Name, service.Price!.Value, "DiagnosticItem", item.Id));
            }
            if (index < 4)
            {
                var prescription = new Prescription
                {
                    PatientVisitId = visit.Id, PatientId = patient.Id, DoctorId = doctor.Id, Status = PrescriptionStatus.Issued,
                    CreatedAt = time.AddMinutes(40), Notes = "Dùng theo đơn, tái khám nếu triệu chứng không cải thiện.",
                    Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = 10, Dosage = "1 viên/lần", Frequency = "2 lần/ngày sau ăn", DurationDays = 5 }]
                };
                db.Prescriptions.Add(prescription);
                await db.SaveChangesAsync();
                invoice.Items.Add(Item(medicine.Code, medicine.Name, medicine.UnitPrice!.Value, "PrescriptionItem:v2", prescription.Id * 4294967296L + medicine.Id, 10));
                await RealisticDevelopmentSeed.DispenseAsync(db, clock, prescription, pharmacist, time.AddHours(1));
            }
            invoice.Subtotal = invoice.TotalAmount = invoice.Items.Sum(i => i.LineTotal);
            invoice.Payments.Add(new Payment
            {
                PaymentCode = BusinessDisplay.RandomCode("PAY", date, 8), Amount = invoice.TotalAmount, Method = PaymentMethod.Cash,
                Status = PaymentStatus.Succeeded, ReceivedAtUtc = invoice.PaidAtUtc.Value, ReceivedByUserId = receptionist
            });
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }
        db.SystemAuditLogs.Add(new SystemAuditLog { UserId = receptionist, Action = "Initialize24SevenPatientHistory", EntityName = "DevelopmentData",
            EntityId = patient.Id.ToString(), Description = "Khôi phục lịch sử khám của tài khoản bệnh nhân mẫu", CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static InvoiceItem Item(string code, string description, decimal price, string referenceType, long referenceId, int quantity = 1) =>
        new() { ItemCode = code, Description = description, Quantity = quantity, UnitPrice = price, LineTotal = price * quantity, ReferenceType = referenceType, ReferenceId = referenceId };
}
