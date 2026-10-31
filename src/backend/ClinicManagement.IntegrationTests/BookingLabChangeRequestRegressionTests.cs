using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Billing;
using ClinicManagement.Infrastructure.Notifications;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class BookingLabChangeRequestRegressionTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(Doctor Doctor, Department Department)> DoctorAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = suffix, Email = suffix + "@test.com", PhoneNumber = "09" + Random.Shared.Next(10000000, 99999999), FullName = "Bác sĩ chuyên khoa phụ", IsActive = true };
        db.Users.Add(user);
        var facility = new Facility { Code = suffix, Name = "Cơ sở chuyên khoa", IsActive = true };
        var department = new Department { Facility = facility, Code = suffix, Name = "Khoa chính", SpecialtyId = SpecialtyEntityId, IsActive = true };
        var doctor = new Doctor { UserId = user.Id, IsActive = true, DoctorSpecialties = [
            new() { SpecialtyId = SpecialtyEntityId, IsPrimary = true }, new() { SpecialtyId = CardiologySpecialtyId, IsPrimary = false }] };
        db.Doctors.Add(doctor);
        db.Departments.AddRange(department, new Department { Facility = facility, Code = suffix + "-S", Name = "Tim mạch", SpecialtyId = CardiologySpecialtyId, IsActive = true });
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = user.Id, FacilityId = facility.Id, DepartmentId = department.Id, Role = "Doctor", IsActive = true });
        await db.SaveChangesAsync();
        return (doctor, department);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecondarySpecialty_BooksAtTheSingleAssignedFacility(bool explicitFacility)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (doctor, department) = await DoctorAsync(db);
        var slot = await CreateAvailableSlotAsync(doctor.Id, GetFutureWorkingDate(explicitFacility ? 6 : 5), new(14, 0), new(14, 30));
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/appointments", new {
            doctorId = doctor.Id, specialtyId = CardiologySpecialtyId, appointmentSlotId = slot.Id,
            facilityId = explicitFacility ? (long?)department.FacilityId : null, reason = "Khám tim mạch và kiểm tra huyết áp" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await db.Appointments.SingleAsync(a => a.AppointmentSlotId == slot.Id);
        Assert.Equal(department.FacilityId, saved.FacilityId);
        Assert.Equal(CardiologySpecialtyId, saved.SpecialtyId);
    }

    [Fact]
    public async Task NoEligibleFacility_ExplainsUnavailableInsteadOfMultipleFacilities()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (doctor, department) = await DoctorAsync(db);
        (await db.Facilities.FindAsync(department.FacilityId))!.IsActive = false;
        await db.SaveChangesAsync();
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/appointments", new {
            doctorId = doctor.Id, specialtyId = CardiologySpecialtyId, appointmentSlotId = SlotEntityId,
            reason = "Khám tim mạch và kiểm tra huyết áp" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("FACILITY_NOT_AVAILABLE", body.GetProperty("errorCode").GetString());
        Assert.Contains("Không có cơ sở khả dụng", body.GetProperty("message").GetString());
        Assert.DoesNotContain("nhiều cơ sở", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(AppointmentChangeRequestType.Reschedule)]
    [InlineData(AppointmentChangeRequestType.Cancellation)]
    public async Task PendingRequest_IsVisibleAndWithdrawableByAppointmentOwner_EvenWhenSeededByStaff(AppointmentChangeRequestType type)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointment = new Appointment { AppointmentCode = "APT-" + Guid.NewGuid().ToString("N")[..12],
            PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = SlotEntityId, AppointmentDate = GetFutureWorkingDate(10), StartTime = new(9, 0), EndTime = new(9, 30), Status = AppointmentStatus.Confirmed };
        var request = new AppointmentChangeRequest { Appointment = appointment, RequestType = type,
            Status = AppointmentChangeRequestStatus.Pending, RequestedByUserId = AdminId, Reason = "Yêu cầu được tạo từ seed", OriginalAppointmentStatus = AppointmentStatus.Confirmed };
        db.AppointmentChangeRequests.Add(request);
        await db.SaveChangesAsync();
        await AuthenticateAsync("pat2@test.com");
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsJsonAsync($"/api/v1/appointment-change-requests/{request.Id}/withdraw", new { })).StatusCode);
        await AuthenticateAsync("pat1@test.com");
        var body = await Client.GetFromJsonAsync<JsonElement>("/api/v1/appointment-change-requests?status=Pending&pageSize=100");
        Assert.Contains(body.GetProperty("data").GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetInt64() == request.Id);
        Assert.True((await Client.PostAsJsonAsync($"/api/v1/appointment-change-requests/{request.Id}/withdraw", new { })).IsSuccessStatusCode);
        await db.Entry(request).ReloadAsync();
        Assert.Equal(AppointmentChangeRequestStatus.Withdrawn, request.Status);
    }

    [Fact]
    public async Task LeaveConflict_IncludesCountCodeDateTimeAndPatient()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (doctor, department) = await DoctorAsync(db);
        var date = GetFutureWorkingDate(8);
        var slot = await CreateAvailableSlotAsync(doctor.Id, date, new(9, 0), new(9, 30));
        var appointment = new Appointment { AppointmentCode = "APT-LEAVE-" + Guid.NewGuid().ToString("N")[..8],
            PatientId = Patient1EntityId, DoctorId = doctor.Id, SpecialtyId = SpecialtyEntityId, FacilityId = department.FacilityId,
            AppointmentSlotId = slot.Id, AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.Confirmed };
        var leave = new DoctorLeaveRequest { DoctorId = doctor.Id, StartDateTime = date.ToDateTime(new(8, 0)), EndDateTime = date.ToDateTime(new(12, 0)), Reason = "Nghỉ kiểm thử", Status = DoctorLeaveRequestStatus.Pending };
        db.AddRange(appointment, leave);
        await db.SaveChangesAsync();
        await AuthenticateAsync("admin@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/admin/leave-requests/{leave.Id}/approve", new { });
        var message = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()!;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Có 1 lịch hẹn bị ảnh hưởng", message);
        Assert.Contains(appointment.AppointmentCode, message);
        Assert.Contains($"{date:dd/MM/yyyy} 09:00–09:30", message);
        Assert.Contains((await db.Users.FindAsync(Patient1Id))!.FullName, message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteConsultation_AuditIncludesVisitCode(bool scheduled)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var (doctor, department) = await DoctorAsync(db);
        var visit = new PatientVisit { VisitCode = "VIS-" + Guid.NewGuid().ToString("N")[..12], PatientId = Patient1EntityId,
            FacilityId = department.FacilityId, DepartmentId = department.Id, AssignedDoctorId = doctor.Id,
            VisitDate = clock.VietnamToday, Status = VisitStatus.InConsultation, CreatedByUserId = ReceptionistId };
        if (scheduled) visit.Appointment = new Appointment { AppointmentCode = "APT-" + Guid.NewGuid().ToString("N")[..12],
            PatientId = Patient1EntityId, DoctorId = doctor.Id, SpecialtyId = SpecialtyEntityId, FacilityId = department.FacilityId,
            AppointmentSlotId = SlotEntityId, AppointmentDate = clock.VietnamToday, StartTime = new(9, 0), EndTime = new(9, 30), Status = AppointmentStatus.InConsultation };
        db.PatientVisits.Add(visit);
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = ReceptionistId, FacilityId = department.FacilityId, Role = "Receptionist", IsActive = true });
        await db.SaveChangesAsync();
        var current = new Mock<ICurrentUserService>(); current.SetupGet(c => c.UserId).Returns(doctor.UserId);
        var doctorContext = new Mock<IDoctorContextService>(); doctorContext.Setup(c => c.GetCurrentActiveDoctorAsync()).ReturnsAsync(doctor);
        var service = new DoctorAppointmentService(db, doctorContext.Object, clock, current.Object, Mock.Of<IPatientVisitService>());
        if (scheduled) await service.CompleteAppointmentAsync(visit.AppointmentId!.Value, new CompleteConsultationRequest { Diagnosis = "Đau đầu" });
        else await service.CompleteVisitConsultationAsync(visit.Id, new CompleteConsultationRequest { Diagnosis = "Đau đầu" });
        Assert.Equal($"Bác sĩ hoàn tất khám lượt {visit.VisitCode}",
            (await db.SystemAuditLogs.SingleAsync(a => a.Action == "COMPLETE_CONSULTATION" && a.EntityId == visit.Id.ToString())).Description);
        var notification = await db.Notifications.SingleAsync(n => n.RelatedEntityType == "PatientVisit" && n.RelatedEntityId == visit.Id.ToString() && n.UserId == ReceptionistId);
        Assert.Equal("Chờ lập hóa đơn", notification.Title);
        Assert.Contains(visit.VisitCode, notification.Message);
    }

    [Fact]
    public async Task UnpaidPharmacyVisit_LeavesBillingQueueAfterPayment_AndSendsShortPrescriptionTitle()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var (doctor, department) = await DoctorAsync(db);
        var visit = new PatientVisit { VisitCode = "VIS-" + Guid.NewGuid().ToString("N")[..12], PatientId = Patient1EntityId,
            FacilityId = department.FacilityId, DepartmentId = department.Id, AssignedDoctorId = doctor.Id,
            VisitDate = clock.VietnamToday, Status = VisitStatus.InPharmacy, CreatedByUserId = ReceptionistId };
        var prescription = new Prescription { PatientVisit = visit, PatientId = Patient1EntityId, DoctorId = doctor.Id, Status = PrescriptionStatus.Issued,
            Items = [new PrescriptionItem { MedicineId = MedicineEntityId, Quantity = 1, Dosage = "1 viên", Frequency = "Mỗi ngày", DurationDays = 1 }] };
        db.Prescriptions.Add(prescription);
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = PharmacistId, FacilityId = department.FacilityId, Role = "Pharmacist", IsActive = true });
        await db.SaveChangesAsync();
        var fee = (await db.Specialties.FindAsync(SpecialtyEntityId))!.ConsultationFee;
        var invoice = new Invoice { InvoiceCode = "INV-" + Guid.NewGuid().ToString("N")[..12], PatientId = Patient1EntityId,
            PatientVisitId = visit.Id, CreatedByUserId = ReceptionistId, TotalAmount = fee + 1000, Subtotal = fee + 1000,
            Items = [new InvoiceItem { ItemCode = "CONS", Description = "Khám", Quantity = 1, UnitPrice = fee, LineTotal = fee, ReferenceType = "Consultation", ReferenceId = visit.Id },
                new InvoiceItem { ItemCode = "MED", Description = "Thuốc", Quantity = 1, UnitPrice = 1000, LineTotal = 1000,
                    ReferenceType = "PrescriptionItem:v2", ReferenceId = prescription.Id * 4294967296L + MedicineEntityId }] };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        var current = new Mock<ICurrentUserService>(); current.SetupGet(c => c.UserId).Returns(ReceptionistId);
        var access = new Mock<IFacilityAuthorizationService>();
        access.Setup(a => a.HasFullFacilityAccessAsync(ReceptionistId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        access.Setup(a => a.GetUserAccessibleFacilityIdsAsync(ReceptionistId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<long>());
        var notifications = new NotificationService(db, current.Object, NullLogger<NotificationService>.Instance);
        var billing = new BillingService(db, notifications, clock, NullLogger<BillingService>.Instance, access.Object, current.Object);
        var pending = await billing.GetUnbilledVisitsAsync(department.FacilityId, ReceptionistId);
        Assert.True(Assert.Single(pending.Items).HasPendingPayment);
        await billing.ProcessPaymentAsync(invoice.Id, new ProcessPaymentRequest { Amount = invoice.TotalAmount, Method = PaymentMethod.Cash }, ReceptionistId);
        Assert.Empty((await billing.GetUnbilledVisitsAsync(department.FacilityId, ReceptionistId)).Items);
        var notification = await db.Notifications.SingleAsync(n => n.UserId == PharmacistId && n.RelatedEntityId == prescription.Id.ToString() && n.RelatedEntityType == "Prescription");
        Assert.Equal("Đơn thuốc chờ cấp phát", notification.Title);
        Assert.Contains("đã thanh toán, chờ cấp phát", notification.Message);
    }

    [Fact]
    public async Task LabOverview_ListsUnfinishedOrdersOnlyWithinAssignedFacility()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, department) = await DoctorAsync(db);
        var (_, outsideDepartment) = await DoctorAsync(db);
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync("LabSupervisor")) await roles.CreateAsync(new IdentityRole<Guid>("LabSupervisor"));
        var email = $"lab-{Guid.NewGuid():N}@test.com";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, PhoneNumber = "09" + Random.Shared.Next(10000000, 99999999), FullName = "Trưởng khoa xét nghiệm", IsActive = true };
        Assert.True((await manager.CreateAsync(user, "Pass@123")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, "LabSupervisor")).Succeeded);
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = user.Id, FacilityId = department.FacilityId, Role = "LabSupervisor", IsActive = true });
        var orders = new[] { DiagnosticOrderStatus.Ordered, DiagnosticOrderStatus.InProgress, DiagnosticOrderStatus.Completed, DiagnosticOrderStatus.Cancelled, DiagnosticOrderStatus.Ordered }
            .Select((status, index) => new DiagnosticOrder { OrderCode = "DX-" + Guid.NewGuid().ToString("N")[..12],
                PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId,
                FacilityId = index == 4 ? outsideDepartment.FacilityId : department.FacilityId, Status = status,
                OrderedAtUtc = DateTime.UtcNow.AddMinutes(-45), ClinicalIndication = "Kiểm thử tổng quan", VerificationStatus = DiagnosticVerificationStatus.NotRequired,
                Items = [new DiagnosticOrderItem { Status = DiagnosticItemStatus.Ordered,
                    DiagnosticService = new DiagnosticService { Code = "LAB-" + Guid.NewGuid().ToString("N")[..10], Name = "Xét nghiệm tổng quát", Category = DiagnosticCategory.Laboratory } }] }).ToArray();
        db.DiagnosticOrders.AddRange(orders);
        await db.SaveChangesAsync();
        await AuthenticateAsync(email);
        var body = await Client.GetFromJsonAsync<JsonElement>("/api/v1/diagnostic-verification/queue?active=true");
        var items = body.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(orders.Take(2).Select(o => o.OrderCode).Order(), items.Select(o => o.GetProperty("orderCode").GetString()).Order());
        Assert.All(items, item => { Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("patientName").GetString())); Assert.Single(item.GetProperty("items").EnumerateArray()); });
        await AuthenticateAsync("pat1@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("/api/v1/diagnostic-verification/queue?active=true")).StatusCode);
    }
}
