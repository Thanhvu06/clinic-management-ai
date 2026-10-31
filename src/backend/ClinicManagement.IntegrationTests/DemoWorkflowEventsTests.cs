using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Billing;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Notifications;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Pharmacy;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class DemoWorkflowEventsTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task CompletionPaymentAndDispense_RecordAuditsAndNotifyOnlyFacilityStaff(bool scheduled, bool paidInFull)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var doctor = await db.Doctors.FindAsync(DoctorEntityId);
        var department = await db.Departments.FirstAsync();
        var otherFacility = new Facility { Code = $"OTHER-{Guid.NewGuid():N}"[..15], Name = "Cơ sở khác" };
        db.Facilities.Add(otherFacility);
        var receptionist = AddUser(db, "reception");
        var pharmacist = AddUser(db, "pharmacy");
        var outsider = AddUser(db, "outside");
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.AddRange(
            new StaffFacilityAssignment { UserId = receptionist.Id, FacilityId = department.FacilityId, Role = "Receptionist" },
            new StaffFacilityAssignment { UserId = pharmacist.Id, FacilityId = department.FacilityId, Role = "Pharmacist" },
            new StaffFacilityAssignment { UserId = outsider.Id, FacilityId = otherFacility.Id, Role = "Receptionist" },
            new StaffFacilityAssignment { UserId = outsider.Id, FacilityId = otherFacility.Id, Role = "Pharmacist" });
        var visit = new PatientVisit
        {
            VisitCode = $"VIS-{Guid.NewGuid():N}"[..20], PatientId = Patient1EntityId,
            FacilityId = department.FacilityId, DepartmentId = department.Id, AssignedDoctorId = doctor!.Id,
            VisitDate = clock.VietnamToday, QueueNumber = (scheduled ? 921 : 922) + (paidInFull ? 0 : 2), Status = VisitStatus.InConsultation,
            CreatedByUserId = ReceptionistId
        };
        if (scheduled)
            visit.Appointment = new Appointment
            {
                AppointmentCode = $"APT-{Guid.NewGuid():N}"[..20], PatientId = visit.PatientId, DoctorId = doctor.Id,
                SpecialtyId = SpecialtyEntityId, FacilityId = visit.FacilityId, AppointmentSlotId = SlotEntityId,
                AppointmentDate = clock.VietnamToday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30),
                Status = AppointmentStatus.InConsultation
            };
        var medicine = new Medicine { Code = $"EV-{Guid.NewGuid():N}"[..15], Name = "Thuốc kiểm thử", Unit = "Viên", UnitPrice = 1000, StockQuantity = 10, IsActive = true };
        db.Medicines.Add(medicine);
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        var prescription = new Prescription
        {
            PatientVisitId = visit.Id, AppointmentId = visit.AppointmentId, PatientId = visit.PatientId,
            DoctorId = doctor.Id, Status = PrescriptionStatus.Draft, CreatedAt = clock.UtcNow,
            Items = [new PrescriptionItem { MedicineId = medicine.Id, Quantity = paidInFull ? 1 : 2, Dosage = "1 viên", Frequency = "1 lần/ngày", DurationDays = 1 }]
        };
        db.Prescriptions.Add(prescription);
        db.MedicineBatches.Add(new MedicineBatch { MedicineId = medicine.Id, BatchNumber = $"EVENT-{Guid.NewGuid():N}"[..15], QuantityReceived = 10, QuantityRemaining = 10, ExpiryDate = clock.VietnamToday.AddMonths(6), ReceivedAt = clock.UtcNow });
        await db.SaveChangesAsync();
        var current = new Mock<ICurrentUserService>(); current.SetupGet(c => c.UserId).Returns(doctor.UserId);
        var doctorContext = new Mock<IDoctorContextService>(); doctorContext.Setup(c => c.GetCurrentActiveDoctorAsync()).ReturnsAsync(doctor);
        var notifications = new NotificationService(db, current.Object, NullLogger<NotificationService>.Instance);
        var service = new DoctorAppointmentService(db, doctorContext.Object, clock, current.Object, Mock.Of<IPatientVisitService>(), notificationService: notifications);
        if (scheduled) await service.CompleteAppointmentAsync(visit.AppointmentId!.Value, new CompleteConsultationRequest { Diagnosis = "Đau đầu" });
        else await service.CompleteVisitConsultationAsync(visit.Id, new CompleteConsultationRequest { Diagnosis = "Đau đầu" });
        Assert.True(await db.SystemAuditLogs.AnyAsync(l => l.Action == "COMPLETE_CONSULTATION" && l.UserId == doctor.UserId && l.EntityId == visit.Id.ToString()));
        Assert.True(await db.SystemAuditLogs.AnyAsync(l => l.Action == "ISSUE_PRESCRIPTION" && l.UserId == doctor.UserId && l.EntityId == prescription.Id.ToString()));
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == receptionist.Id && n.Message == $"Lượt khám {visit.VisitCode} đã hoàn tất, chờ lập hóa đơn"));
        Assert.False(await db.Notifications.AnyAsync(n => n.UserId == outsider.Id));
        await VisitCompletionCoordinator.RecordClinicalCompletionAsync(visit.Id, db, notifications);
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == receptionist.Id && n.RelatedEntityId == visit.Id.ToString()));

        var invoice = new Invoice
        {
            InvoiceCode = $"INV-{Guid.NewGuid():N}"[..20], PatientId = visit.PatientId, PatientVisitId = visit.Id,
            AppointmentId = visit.AppointmentId, CreatedByUserId = ReceptionistId, TotalAmount = 1000, Subtotal = 1000,
            Items = [new InvoiceItem { ItemCode = medicine.Code, Description = medicine.Name, Quantity = 1, UnitPrice = 1000, LineTotal = 1000,
                ReferenceType = "PrescriptionItem:v2", ReferenceId = prescription.Id * 4294967296L + medicine.Id }]
        };
        db.Invoices.Add(invoice); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var billing = new BillingService(db, notifications, clock, NullLogger<BillingService>.Instance, Mock.Of<IFacilityAuthorizationService>(), current.Object);
        await billing.ProcessPaymentAsync(invoice.Id, new ProcessPaymentRequest { Amount = 1000, Method = PaymentMethod.Cash }, ReceptionistId);
        if (!paidInFull)
        {
            Assert.False(await db.Notifications.AnyAsync(n => n.UserId == pharmacist.Id && n.RelatedEntityId == prescription.Id.ToString()));
            return;
        }
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == pharmacist.Id && n.RelatedEntityId == prescription.Id.ToString() && n.Message.Contains("đã thanh toán, chờ cấp phát")));
        Assert.False(await db.Notifications.AnyAsync(n => n.UserId == outsider.Id));
        current.SetupGet(c => c.UserId).Returns(PharmacistId);
        var pharmacy = new PharmacyService(db, current.Object, new PrescriptionPaymentEligibilityService(db), Mock.Of<IFacilityAuthorizationService>(), clock);
        await pharmacy.DispensePrescriptionAsync(prescription.Id);
        Assert.True(await db.SystemAuditLogs.AnyAsync(l => l.Action == "DISPENSE_PRESCRIPTION" && l.UserId == PharmacistId && l.EntityId == prescription.Id.ToString()));
    }

    [Fact]
    public async Task WalkInIntake_RecordsAuditOnceForAnIdempotentRequest()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await db.Departments.FirstAsync();
        var current = new Mock<ICurrentUserService>(); current.SetupGet(c => c.UserId).Returns(ReceptionistId);
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var service = new PatientVisitService(db, current.Object, clock, scope.ServiceProvider.GetRequiredService<IMrnGenerator>(), Mock.Of<IFacilityAuthorizationService>());
        var request = new ReceptionIntakeRequest { ExistingPatientId = Patient2EntityId, FacilityId = department.FacilityId, DepartmentId = department.Id, ChiefComplaint = "Đau đầu", IdempotencyKey = Guid.NewGuid().ToString() };
        var ticket = await service.ReceptionIntakeAsync(request);
        var repeat = await service.ReceptionIntakeAsync(request);
        Assert.Equal(ticket.VisitId, repeat.VisitId);
        var audits = await db.SystemAuditLogs.Where(l => l.Action == "WALK_IN_INTAKE" && l.EntityId == ticket.VisitId.ToString()).ToListAsync();
        Assert.Single(audits);
        Assert.Equal(ReceptionistId, audits[0].UserId);
        Assert.Equal(clock.UtcNow.Date, audits[0].CreatedAt.Date);
    }

    private static ApplicationUser AddUser(AppDbContext db, string name)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"{name}-{Guid.NewGuid():N}", Email = $"{name}-{Guid.NewGuid():N}@test.local", FullName = name, PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", IsActive = true };
        db.Users.Add(user); return user;
    }
}
