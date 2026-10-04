using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Admin.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.HealthPackages.DTOs;
using ClinicManagement.Application.HealthPackages.Interfaces;
using ClinicManagement.Application.Leaves.DTOs;
using ClinicManagement.Application.Leaves.Interfaces;
using ClinicManagement.Application.Mpi.DTOs;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Application.Patients.DTOs;
using ClinicManagement.Application.Patients.Interfaces;
using ClinicManagement.Application.Schedules.DTOs;
using ClinicManagement.Application.Schedules.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Leaves;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class AuditRound5Tests : IntegrationTestBase
{
    public AuditRound5Tests(CustomWebApplicationFactory factory) : base(factory) { }
    private static string Code() => "R5-" + Guid.NewGuid().ToString("N")[..14];
    private static string Phone() => $"09{Random.Shared.Next(10000000, 99999999)}";
    private static async Task OkAsync(HttpResponseMessage response) => Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        await OkAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private WebApplicationFactory<Program> TimedFactory(DateTime utc, Guid actor)
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(utc);
        clock.SetupGet(x => x.VietnamNow).Returns(utc.AddHours(7));
        clock.SetupGet(x => x.VietnamToday).Returns(DateOnly.FromDateTime(utc.AddHours(7)));
        clock.SetupGet(x => x.VietnamTime).Returns(TimeOnly.FromDateTime(utc.AddHours(7)));
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns(actor);
        return Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(clock.Object);
            services.AddSingleton(user.Object);
        }));
    }

    private async Task<(Doctor Doctor, Specialty Specialty)> DoctorAsync(AppDbContext db)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = Code()+"@test.com", Email = Code()+"@test.com", PhoneNumber = Phone(), FullName = "Bác sĩ R5", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() };
        var specialty = new Specialty { SpecialtyCode = Code(), Name = "Chuyên khoa R5", IsActive = true };
        var doctor = new Doctor { UserId = user.Id, IsActive = true };
        doctor.DoctorSpecialties.Add(new DoctorSpecialty { Specialty = specialty, IsPrimary = true });
        db.Users.Add(user);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        return (doctor, specialty);
    }

    private async Task<Appointment> AppointmentAsync(AppDbContext db, Doctor doctor, Specialty specialty, AppointmentStatus status,
        DateOnly date, long? patientId = null)
    {
        var slot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date, StartTime = new(8, 0), EndTime = new(8, 30), IsBooked = true };
        var appointment = new Appointment { AppointmentCode = Code(), DoctorId = doctor.Id, SpecialtyId = specialty.Id,
            PatientId = patientId ?? Patient1EntityId, AppointmentSlot = slot, AppointmentDate = date,
            StartTime = slot.StartTime, EndTime = slot.EndTime, Status = status };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    private async Task<HealthPackageRegistration> RegistrationAsync(AppDbContext db)
    {
        var registration = new HealthPackageRegistration { RegistrationCode = Code(), PatientId = Patient1EntityId,
            HealthPackageId = PackageEntityId, PreferredDate = GetFutureWorkingDate(400), ContactPhone = "0901234567",
            Status = HealthPackageRegistrationStatus.Pending };
        db.HealthPackageRegistrations.Add(registration);
        await db.SaveChangesAsync();
        return registration;
    }

    [Theory]
    [InlineData("register")]
    [InlineData("confirm")]
    [InlineData("cancel")]
    public async Task G1_Package_notifications_use_registered_patient_and_reception_routes(string flow)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        long registrationId;
        if (flow == "register")
        {
            using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
            registrationId = (await DataAsync(await patient.PostAsJsonAsync("/api/v1/patient/health-package-registrations",
                new { healthPackageId = PackageEntityId, preferredDate = GetFutureWorkingDate(401), contactPhone = "0901234567" }))).GetProperty("id").GetInt64();
            var reg = await db.HealthPackageRegistrations.SingleAsync(x => x.Id == registrationId);
            Assert.Equal("/patient/health-package-registrations", (await db.Notifications.SingleAsync(x => x.DedupeKey == $"pkg_reg_pat_{reg.RegistrationCode}")).Route);
            var recipients = await db.Notifications.Where(x => x.DedupeKey != null && x.DedupeKey.StartsWith($"pkg_reg_rec_{reg.RegistrationCode}_")).ToListAsync();
            Assert.NotEmpty(recipients);
            Assert.All(recipients, x => Assert.Equal("/reception/package-registrations", x.Route));
        }
        else
        {
            var reg = await RegistrationAsync(db);
            registrationId = reg.Id;
            using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
            await OkAsync(await rec.PostAsJsonAsync($"/api/v1/reception/health-package-registrations/{registrationId}/{flow}", new { notes = "R5", cancellationReason = "R5" }));
            var suffix = flow == "confirm" ? "confirmed" : "cancelled";
            Assert.Equal("/patient/health-package-registrations", (await db.Notifications.SingleAsync(x => x.DedupeKey == $"pkg_reg_proc_{registrationId}_{suffix}")).Route);
        }
    }

    [Fact]
    public async Task G1_Visit_completion_notification_uses_patient_appointments_route()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await db.Departments.FirstAsync();
        var visit = new PatientVisit { VisitCode = Code(), PatientId = Patient1EntityId, AssignedDoctorId = DoctorEntityId,
            FacilityId = department.FacilityId, DepartmentId = department.Id, VisitDate = DateOnly.FromDateTime(DateTime.UtcNow),
            QueueNumber = TestQueueNumbers.Next(), Status = VisitStatus.InConsultation, CreatedByUserId = ReceptionistId };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        using var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        await OkAsync(await doctor.PostAsJsonAsync($"/api/v1/doctor/visits/{visit.Id}/complete", new { summary = "Hoàn tất R5", diagnosis = "R5" }));
        Assert.Equal("/patient/appointments", (await db.Notifications.SingleAsync(x => x.DedupeKey == $"visit_complete_{visit.Id}")).Route);
    }

    [Fact]
    public async Task G2_Package_preferred_date_uses_vietnam_today_after_utc_midnight_boundary()
    {
        var utcDay = DateOnly.FromDateTime(DateTime.UtcNow);
        using var factory = TimedFactory(utcDay.ToDateTime(new TimeOnly(18, 30), DateTimeKind.Utc), Patient1Id);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IHealthPackageRegistrationService>();
        var error = await Assert.ThrowsAsync<BusinessException>(() => service.RegisterPackageAsync(new CreatePackageRegistrationRequest
        { HealthPackageId = PackageEntityId, PreferredDate = utcDay, ContactPhone = "0901234567" }));
        Assert.Equal("INVALID_DATE", error.ErrorCode);
        var valid = await service.RegisterPackageAsync(new CreatePackageRegistrationRequest
        { HealthPackageId = PackageEntityId, PreferredDate = utcDay.AddDays(1), ContactPhone = "0901234567" });
        Assert.Equal(utcDay.AddDays(1), valid.PreferredDate);
    }

    [Fact]
    public async Task G2_Leave_at_0100_is_past_at_0130_vietnam_but_0200_is_approved()
    {
        var utc = new DateTime(2037, 1, 1, 18, 30, 0, DateTimeKind.Utc);
        using var factory = TimedFactory(utc, AdminId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (doctor, _) = await DoctorAsync(db);
        var date = DateOnly.FromDateTime(utc.AddHours(7));
        var past = new DoctorLeaveRequest { DoctorId = doctor.Id, StartDateTime = date.ToDateTime(new(1, 0)), EndDateTime = date.ToDateTime(new(1, 20)), Reason = "R5" };
        var future = new DoctorLeaveRequest { DoctorId = doctor.Id, StartDateTime = date.ToDateTime(new(2, 0)), EndDateTime = date.ToDateTime(new(3, 0)), Reason = "R5" };
        db.DoctorLeaveRequests.AddRange(past, future);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IAdminLeaveService>();
        Assert.Equal("INVALID_TIME", (await Assert.ThrowsAsync<BusinessException>(() => service.ApproveLeaveRequestAsync(past.Id, new()))).ErrorCode);
        await service.ApproveLeaveRequestAsync(future.Id, new());
        Assert.Equal(DoctorLeaveRequestStatus.Approved, future.Status);
        Assert.Equal(DoctorLeaveRequestStatus.Pending, past.Status);
    }

    [Fact]
    public async Task G2_Mpi_age_uses_vietnam_date_for_detail_mrn_and_search()
    {
        using var factory = TimedFactory(new DateTime(2037, 1, 1, 18, 30, 0, DateTimeKind.Utc), ReceptionistId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patient = new Patient { FullName = Code(), MedicalRecordNumber = Code(), DateOfBirth = new DateOnly(2000, 1, 2) };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        var mpi = scope.ServiceProvider.GetRequiredService<IMpiPatientService>();
        Assert.Equal(37, (await mpi.GetPatientByIdAsync(patient.Id)).Age);
        Assert.Equal(37, (await mpi.GetPatientByMrnAsync(patient.MedicalRecordNumber)).Age);
        Assert.Equal(37, Assert.Single((await mpi.SearchPatientsAsync(new PatientSearchQuery { MedicalRecordNumber = patient.MedicalRecordNumber })).Items).Age);
    }

    [Fact]
    public async Task G2_Patient_birth_date_accepts_vietnam_today_and_rejects_tomorrow()
    {
        var utcDay = DateOnly.FromDateTime(DateTime.UtcNow);
        using var factory = TimedFactory(utcDay.ToDateTime(new TimeOnly(18, 30), DateTimeKind.Utc), Patient1Id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patient = await db.Patients.SingleAsync(x => x.Id == Patient1EntityId);
        var originalDob = patient.DateOfBirth;
        var user = await db.Users.SingleAsync(x => x.Id == Patient1Id);
        var request = new UpdatePatientProfileRequest { FullName = user.FullName, PhoneNumber = user.PhoneNumber!, DateOfBirth = utcDay.AddDays(1), Gender = patient.Gender, Address = patient.Address };
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<IPatientService>();
            await service.UpdateMyProfileAsync(request);
            Assert.Equal(utcDay.AddDays(1), patient.DateOfBirth);
            request.DateOfBirth = utcDay.AddDays(2);
            var error = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateMyProfileAsync(request));
            Assert.Contains("DateOfBirth", error.Errors.Keys);
        }
        finally { patient.DateOfBirth = originalDob; await db.SaveChangesAsync(); }
    }

    [Theory]
    [InlineData("schedule", AppointmentStatus.CheckedIn)]
    [InlineData("schedule", AppointmentStatus.InConsultation)]
    [InlineData("leave", AppointmentStatus.CheckedIn)]
    [InlineData("leave", AppointmentStatus.InConsultation)]
    [InlineData("specialty", AppointmentStatus.CheckedIn)]
    [InlineData("specialty", AppointmentStatus.InConsultation)]
    public async Task G3_CheckedIn_and_InConsultation_hold_slots_for_schedule_leave_and_specialty_guards(string operation, AppointmentStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (doctor, specialty) = await DoctorAsync(db);
        var date = GetFutureWorkingDate(500);
        var appointment = await AppointmentAsync(db, doctor, specialty, status, date);
        BusinessException error;
        if (operation == "schedule")
        {
            var schedule = new DoctorWorkSchedule { DoctorId = doctor.Id, WorkDate = date, StartTime = new(8, 0), EndTime = new(9, 0), IsActive = true };
            db.DoctorWorkSchedules.Add(schedule);
            await db.SaveChangesAsync();
            error = await Assert.ThrowsAsync<BusinessException>(() => scope.ServiceProvider.GetRequiredService<IScheduleService>().UpdateWorkScheduleAsync(schedule.Id,
                new UpdateWorkScheduleRequest { WorkDate = date.AddDays(7), StartTime = new(8, 0), EndTime = new(9, 0) }));
            Assert.Equal("CONFLICT_APPOINTMENTS", error.ErrorCode);
            Assert.Equal(date, schedule.WorkDate);
        }
        else if (operation == "leave")
        {
            var leave = new DoctorLeaveRequest { DoctorId = doctor.Id, StartDateTime = date.ToDateTime(new(8, 0)), EndDateTime = date.ToDateTime(new(9, 0)), Reason = "R5" };
            db.DoctorLeaveRequests.Add(leave);
            await db.SaveChangesAsync();
            error = await Assert.ThrowsAsync<BusinessException>(() => scope.ServiceProvider.GetRequiredService<IAdminLeaveService>().ApproveLeaveRequestAsync(leave.Id, new()));
            Assert.Equal("LEAVE_HAS_AFFECTED_APPOINTMENTS", error.ErrorCode);
            Assert.Equal(DoctorLeaveRequestStatus.Pending, leave.Status);
        }
        else
        {
            error = await Assert.ThrowsAsync<BusinessException>(() => scope.ServiceProvider.GetRequiredService<IAdminDoctorService>().AssignSpecialtiesAsync(doctor.Id,
                new List<AssignSpecialtyDto> { new() { SpecialtyId = SpecialtyEntityId, IsPrimary = true } }));
            Assert.Equal("CANNOT_REMOVE_SPECIALTY", error.ErrorCode);
            Assert.True(await db.DoctorSpecialties.AnyAsync(x => x.DoctorId == doctor.Id && x.SpecialtyId == specialty.Id));
        }
        Assert.Equal(status, appointment.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn, false)]
    [InlineData(AppointmentStatus.InConsultation, false)]
    [InlineData(AppointmentStatus.Pending, true)]
    [InlineData(AppointmentStatus.CheckedIn, true)]
    public async Task G3_Leave_preview_includes_holding_appointments_and_walkin_patient_names(AppointmentStatus status, bool walkin)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (doctor, specialty) = await DoctorAsync(db);
        var date = GetFutureWorkingDate(510);
        var patient = walkin ? new Patient { FullName = "Bệnh nhân vãng lai R5", MedicalRecordNumber = Code() } : await db.Patients.SingleAsync(x => x.Id == Patient1EntityId);
        if (walkin) { db.Patients.Add(patient); await db.SaveChangesAsync(); }
        var appointment = await AppointmentAsync(db, doctor, specialty, status, date, patient.Id);
        var context = new Mock<IDoctorContextService>();
        context.Setup(x => x.GetCurrentActiveDoctorAsync()).ReturnsAsync(doctor);
        var service = new DoctorLeaveService(db, context.Object, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
        var preview = await service.PreviewLeaveAffectedAppointmentsAsync(date.ToDateTime(new(7, 0)), date.ToDateTime(new(10, 0)));
        var actual = Assert.Single(preview.AffectedAppointments);
        Assert.Equal(appointment.Id, actual.Id);
        Assert.Equal(status.ToString(), actual.Status);
        Assert.Equal(walkin ? patient.FullName : (await db.Users.SingleAsync(x => x.Id == Patient1Id)).FullName, actual.PatientName);
    }

    [Theory]
    [InlineData("doctor", AppointmentStatus.Pending)]
    [InlineData("doctor", AppointmentStatus.CheckedIn)]
    [InlineData("user", AppointmentStatus.Pending)]
    [InlineData("user", AppointmentStatus.CheckedIn)]
    [InlineData("specialty", AppointmentStatus.Pending)]
    [InlineData("specialty", AppointmentStatus.CheckedIn)]
    public async Task G4_Deactivation_requires_resolving_future_holding_appointments_but_allows_terminal_or_past_and_reactivation(string target, AppointmentStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (doctor, specialty) = await DoctorAsync(db);
        var appointment = await AppointmentAsync(db, doctor, specialty, status, GetFutureWorkingDate(520));
        async Task ChangeAsync(bool active)
        {
            if (target == "doctor") await scope.ServiceProvider.GetRequiredService<IAdminDoctorService>().UpdateDoctorAsync(doctor.Id, new() { IsActive = active });
            else if (target == "specialty") await scope.ServiceProvider.GetRequiredService<IAdminSpecialtyService>().UpdateSpecialtyAsync(specialty.Id, new() { Name = specialty.Name, IsActive = active });
            else await scope.ServiceProvider.GetRequiredService<IAdminUserService>().ToggleUserStatusAsync(doctor.UserId, new() { IsActive = active });
        }
        var error = await Assert.ThrowsAsync<BusinessException>(() => ChangeAsync(false));
        Assert.Equal(target == "specialty" ? "SPECIALTY_HAS_ACTIVE_APPOINTMENTS" : "DOCTOR_HAS_ACTIVE_APPOINTMENTS", error.ErrorCode);
        Assert.Contains("1", error.Message);
        Assert.True(doctor.IsActive);
        Assert.True(specialty.IsActive);
        Assert.True((await db.Users.SingleAsync(x => x.Id == doctor.UserId)).IsActive);
        foreach (var terminal in new[] { AppointmentStatus.Completed, AppointmentStatus.Cancelled })
        {
            appointment.Status = terminal;
            await db.SaveChangesAsync();
            await ChangeAsync(false);
            await ChangeAsync(true);
        }
        appointment.Status = status;
        appointment.AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await db.SaveChangesAsync();
        await ChangeAsync(false);
        await ChangeAsync(true);
        if (target == "user")
        {
            var ordinary = new ApplicationUser { Id = Guid.NewGuid(), UserName = Code(), Email = Code()+"@test.com", PhoneNumber = Phone(), FullName = "Nhân sự khác", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() };
            db.Users.Add(ordinary);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IAdminUserService>().ToggleUserStatusAsync(ordinary.Id, new() { IsActive = false });
            Assert.False(ordinary.IsActive);
        }
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid, "PACKAGE_INVOICE_PAID")]
    [InlineData(InvoiceStatus.Unpaid, "PACKAGE_INVOICE_ACTIVE")]
    public async Task G5_Package_cancellation_is_blocked_by_effective_invoice_then_allowed_after_unpaid_invoice_cancel(InvoiceStatus status, string code)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registration = await RegistrationAsync(db);
        var invoice = new Invoice { InvoiceCode = Code(), PatientId = Patient1EntityId, HealthPackageRegistrationId = registration.Id,
            Status = status, Subtotal = 1000, TotalAmount = 1000, SourceType = InvoiceSourceType.HealthPackageRegistration };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        using var rec = await CreateAuthenticatedClientAsync("rec@test.com");
        var route = $"/api/v1/reception/health-package-registrations/{registration.Id}/cancel";
        var response = await rec.PostAsJsonAsync(route, new { cancellationReason = "R5" });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
        await db.Entry(registration).ReloadAsync();
        Assert.Equal(HealthPackageRegistrationStatus.Pending, registration.Status);
        if (status == InvoiceStatus.Unpaid)
        {
            await OkAsync(await rec.PatchAsJsonAsync($"/api/v1/reception/billing/invoices/{invoice.Id}/cancel", new { reason = "Hủy trước R5" }));
            await OkAsync(await rec.PostAsJsonAsync(route, new { cancellationReason = "R5" }));
            await db.Entry(registration).ReloadAsync();
            Assert.Equal(HealthPackageRegistrationStatus.Cancelled, registration.Status);
        }
        var noInvoice = await RegistrationAsync(db);
        await OkAsync(await rec.PostAsJsonAsync($"/api/v1/reception/health-package-registrations/{noInvoice.Id}/cancel", new { cancellationReason = "R5" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G6_Sunday_schedule_create_and_update_are_rejected_and_saturday_is_allowed(bool update)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (doctor, _) = await DoctorAsync(db);
        var sunday = GetFutureWorkingDate(530);
        while (sunday.DayOfWeek != DayOfWeek.Sunday) sunday = sunday.AddDays(1);
        var service = scope.ServiceProvider.GetRequiredService<IScheduleService>();
        Task ChangeSundayAsync() => service.CreateWorkScheduleAsync(doctor.Id, new() { WorkDate = sunday, StartTime = new(8, 0), EndTime = new(9, 0) });
        var saturday = await service.CreateWorkScheduleAsync(doctor.Id, new() { WorkDate = sunday.AddDays(-1), StartTime = new(8, 0), EndTime = new(9, 0) });
        var error = await Assert.ThrowsAsync<BusinessException>(() => update
            ? service.UpdateWorkScheduleAsync(saturday.Id, new() { WorkDate = sunday, StartTime = new(8, 0), EndTime = new(9, 0) }) : ChangeSundayAsync());
        Assert.Equal("SUNDAY_CLOSED", error.ErrorCode);
        Assert.Equal("Phòng khám không làm việc vào Chủ nhật.", error.Message);
        Assert.Equal(DayOfWeek.Saturday, saturday.WorkDate.DayOfWeek);
        Assert.False(await db.DoctorWorkSchedules.AnyAsync(x => x.DoctorId == doctor.Id && x.WorkDate == sunday));
    }

    [Theory]
    [InlineData("doctors", 0, 1000, 100)]
    [InlineData("specialties", 0, 1000, 100)]
    [InlineData("leave-requests", 0, 1000, 100)]
    [InlineData("doctors", -1, 0, 10)]
    [InlineData("specialties", -1, 0, 10)]
    [InlineData("leave-requests", -1, 0, 10)]
    public async Task G7_Admin_list_paging_is_bounded(string endpoint, int page, int size, int expectedSize)
    {
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var response = await admin.GetAsync($"/api/v1/admin/{endpoint}?page={page}&pageSize={size}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(expectedSize, data.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("Password123", "Mật khẩu phải chứa ít nhất một ký tự đặc biệt.")]
    [InlineData("Password@@", "Mật khẩu phải chứa ít nhất một chữ số.")]
    [InlineData("password1@", "Mật khẩu phải chứa ít nhất một chữ cái hoa.")]
    public async Task G8_Staff_password_errors_are_vietnamese_and_patient_registration_messages_remain_identical(string password, string message)
    {
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var staffResponse = await admin.PostAsJsonAsync("/api/v1/admin/users", new { fullName = "Nhân sự R5", email = Code()+"@test.com", phoneNumber = Phone(), password, role = "Receptionist" });
        Assert.False(staffResponse.IsSuccessStatusCode);
        Assert.Equal(message, (await staffResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        var patientResponse = await Client.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Bệnh nhân R5", email = Code()+"@test.com", phoneNumber = Phone(), password });
        Assert.Equal(HttpStatusCode.BadRequest, patientResponse.StatusCode);
        var patientError = await patientResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(message, patientError.GetProperty("errors").GetProperty("Password")[0].GetString());
    }
}
