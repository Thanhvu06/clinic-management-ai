using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class PackagePhoneAndLeaveGuardsTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string PendingMessage = "Bác sĩ có lịch nghỉ đang chờ duyệt trong khung giờ này.";
    private static int _fixtureNumber;

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Theory]
    [InlineData("09012345678")]
    [InlineData("0|12345678")]
    [InlineData("0201234567")]
    public async Task InvalidPackagePhoneReturnsValidationEnvelopeWithOriginalFieldKey(string phone)
    {
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/patient/health-package-registrations", new
        {
            healthPackageId = PackageEntityId, preferredDate = "2083-06-01", contactPhone = phone
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Body(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.Equal(body.GetProperty("errors").GetProperty("ContactPhone")[0].GetString(), body.GetProperty("message").GetString());
        Assert.False(body.GetProperty("errors").TryGetProperty("contactPhone", out _));
    }

    private sealed record Fixture(long DoctorId, long SpecialtyId, long FacilityId, long DepartmentId,
        Appointment Appointment, AppointmentSlot Target, DateOnly Date);

    private async Task<Fixture> CreateFixture(bool today = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = Guid.NewGuid().ToString("N");
        var date = today ? scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday : new DateOnly(2083, 6, 1).AddDays(7 * Interlocked.Increment(ref _fixtureNumber));
        if (!today) while (date.DayOfWeek != DayOfWeek.Tuesday) date = date.AddDays(1);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"leave-{token}@test.com", Email = $"leave-{token}@test.com", PhoneNumber = token, FullName = "Bác sĩ kiểm thử lịch nghỉ", IsActive = true };
        db.Users.Add(user);
        var doctor = new Doctor { UserId = user.Id, IsActive = true };
        var specialty = new Specialty { SpecialtyCode = "L-" + token, Name = "Khoa kiểm thử lịch nghỉ", IsActive = true };
        var facility = new Facility { Code = "L-" + token, Name = "Cơ sở kiểm thử lịch nghỉ", IsActive = true };
        db.Doctors.Add(doctor); db.Specialties.Add(specialty); db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        var department = new Department { FacilityId = facility.Id, SpecialtyId = specialty.Id, Code = "L-" + token, Name = "Khoa tiếp nhận", IsActive = true };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = specialty.Id, IsPrimary = true });
        db.StaffFacilityAssignments.AddRange(
            new StaffFacilityAssignment { UserId = user.Id, FacilityId = facility.Id, DepartmentId = department.Id, Role = "Doctor", IsActive = true },
            new StaffFacilityAssignment { UserId = ReceptionistId, FacilityId = facility.Id, DepartmentId = department.Id, Role = "Receptionist", IsActive = true });
        db.DoctorWorkSchedules.Add(new DoctorWorkSchedule { DoctorId = doctor.Id, WorkDate = date, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0), IsActive = true });
        var original = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30), IsBooked = true };
        var target = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30) };
        db.AppointmentSlots.AddRange(original, target);
        await db.SaveChangesAsync();
        var appointment = new Appointment { AppointmentCode = "LEAVE-" + token, PatientId = Patient1EntityId, DoctorId = doctor.Id, SpecialtyId = specialty.Id, FacilityId = facility.Id,
            AppointmentSlotId = original.Id, AppointmentDate = date, StartTime = original.StartTime, EndTime = original.EndTime, Status = AppointmentStatus.Confirmed, Reason = "Khám kiểm thử lịch nghỉ" };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return new Fixture(doctor.Id, specialty.Id, facility.Id, department.Id, appointment, target, date);
    }

    private async Task<long> AddLeave(Fixture fixture, DoctorLeaveRequestStatus status, DateTime? start = null, DateTime? end = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var leave = new DoctorLeaveRequest { DoctorId = fixture.DoctorId, Status = status, Reason = "Đơn kiểm thử",
            StartDateTime = start ?? fixture.Date.ToDateTime(new TimeOnly(9, 0)), EndDateTime = end ?? fixture.Date.ToDateTime(new TimeOnly(10, 0)) };
        db.DoctorLeaveRequests.Add(leave); await db.SaveChangesAsync(); return leave.Id;
    }

    [Theory]
    [InlineData(DoctorLeaveRequestStatus.Pending, false)]
    [InlineData(DoctorLeaveRequestStatus.Approved, false)]
    [InlineData(DoctorLeaveRequestStatus.Rejected, false)]
    [InlineData(DoctorLeaveRequestStatus.Cancelled, false)]
    [InlineData(DoctorLeaveRequestStatus.Pending, true)]
    public async Task BookingAndAvailableSlotsBlockPendingOrApprovedButAllowRejectedOrCancelled(DoctorLeaveRequestStatus status, bool alsoApproved)
    {
        var fixture = await CreateFixture();
        await AddLeave(fixture, status);
        if (alsoApproved) await AddLeave(fixture, DoctorLeaveRequestStatus.Approved);
        var blocked = status is DoctorLeaveRequestStatus.Pending or DoctorLeaveRequestStatus.Approved;
        var available = await Body(await Client.GetAsync($"/api/v1/doctors/{fixture.DoctorId}/available-slots?fromDate={fixture.Date:yyyy-MM-dd}&toDate={fixture.Date:yyyy-MM-dd}"));
        Assert.Equal(!blocked, available.GetProperty("data").EnumerateArray().Any(slot => slot.GetProperty("slotId").GetInt64() == fixture.Target.Id));
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/appointments", new { doctorId = fixture.DoctorId, specialtyId = fixture.SpecialtyId, facilityId = fixture.FacilityId, appointmentSlotId = fixture.Target.Id, reason = "Đặt lịch kiểm tra lịch nghỉ" });
        var body = await Body(response);
        if (blocked)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("DOCTOR_NOT_AVAILABLE", body.GetProperty("errorCode").GetString());
            if (status == DoctorLeaveRequestStatus.Pending && !alsoApproved) Assert.Equal(PendingMessage, body.GetProperty("message").GetString());
            else Assert.Equal("Bác sĩ đang trong lịch nghỉ đã được duyệt.", body.GetProperty("message").GetString());
        }
        else Assert.True(response.IsSuccessStatusCode, body.ToString());
        using var scope = Factory.Services.CreateScope();
        Assert.Equal(!blocked, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppointmentSlots.FindAsync(fixture.Target.Id))!.IsBooked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingLeaveBlocksRescheduleCreationAndApprovalWithoutMovingAppointment(bool approve)
    {
        var fixture = await CreateFixture();
        await AuthenticateAsync("pat1@test.com");
        long requestId = 0;
        if (approve)
        {
            var created = await Client.PostAsJsonAsync($"/api/v1/appointments/{fixture.Appointment.Id}/reschedule-requests", new { requestedSlotId = fixture.Target.Id, reason = "Đổi lịch kiểm thử nghỉ" });
            Assert.True(created.IsSuccessStatusCode, (await Body(created)).ToString());
            using var scope = Factory.Services.CreateScope();
            requestId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppointmentChangeRequests.SingleAsync(r => r.AppointmentId == fixture.Appointment.Id)).Id;
        }
        await AddLeave(fixture, DoctorLeaveRequestStatus.Pending);
        HttpResponseMessage response;
        if (approve)
        {
            await AuthenticateAsync("rec@test.com");
            response = await Client.PostAsJsonAsync($"/api/v1/reception/change-requests/{requestId}/approve-reschedule", new { note = "Duyệt kiểm thử" });
        }
        else response = await Client.PostAsJsonAsync($"/api/v1/appointments/{fixture.Appointment.Id}/reschedule-requests", new { requestedSlotId = fixture.Target.Id, reason = "Đổi lịch kiểm thử nghỉ" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(PendingMessage, (await Body(response)).GetProperty("message").GetString());
        using var verifyScope = Factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(fixture.Appointment.AppointmentSlotId, (await db.Appointments.FindAsync(fixture.Appointment.Id))!.AppointmentSlotId);
        Assert.False((await db.AppointmentSlots.FindAsync(fixture.Target.Id))!.IsBooked);
    }

    [Fact]
    public async Task PendingLeaveBlocksRevisitAcceptanceWithoutBookingSlot()
    {
        var fixture = await CreateFixture();
        long requestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Appointments.FindAsync(fixture.Appointment.Id))!.Status = AppointmentStatus.Completed;
            var request = new RevisitRequest { AppointmentId = fixture.Appointment.Id, PatientId = Patient1EntityId, DoctorId = fixture.DoctorId, SuggestedDate = fixture.Date, Status = RevisitRequestStatus.PendingPatientResponse, Note = "Kiểm thử tái khám" };
            db.RevisitRequests.Add(request); await db.SaveChangesAsync(); requestId = request.Id;
        }
        await AddLeave(fixture, DoctorLeaveRequestStatus.Pending);
        await AuthenticateAsync("pat1@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/revisit-requests/{requestId}/accept", new { targetSlotId = fixture.Target.Id, reason = "Đồng ý tái khám kiểm thử" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(PendingMessage, (await Body(response)).GetProperty("message").GetString());
        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await verifyDb.AppointmentSlots.FindAsync(fixture.Target.Id))!.IsBooked);
        Assert.Equal(RevisitRequestStatus.PendingPatientResponse, (await verifyDb.RevisitRequests.FindAsync(requestId))!.Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public async Task LeaveApprovalListsAtMostTenAffectedCodesAndReportsRemainder(int count)
    {
        var fixture = await CreateFixture();
        var prefix = "AFFECT-" + Guid.NewGuid().ToString("N")[..8] + "-";
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < count; i++) db.Appointments.Add(new Appointment { AppointmentCode = prefix + i.ToString("D2"), DoctorId = fixture.DoctorId, PatientId = Patient1EntityId, SpecialtyId = fixture.SpecialtyId,
                FacilityId = fixture.FacilityId, AppointmentDate = fixture.Date, AppointmentSlotId = fixture.Target.Id, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30), Status = AppointmentStatus.Confirmed, Reason = "Lịch hẹn bị ảnh hưởng" });
            await db.SaveChangesAsync();
        }
        var leaveId = await AddLeave(fixture, DoctorLeaveRequestStatus.Pending);
        await AuthenticateAsync("admin@test.com");
        var response = await Client.PostAsJsonAsync($"/api/v1/admin/leave-requests/{leaveId}/approve", new { adminNote = "Duyệt kiểm thử" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("LEAVE_HAS_AFFECTED_APPOINTMENTS", body.GetProperty("errorCode").GetString());
        var message = body.GetProperty("message").GetString()!;
        for (var i = 0; i < Math.Min(count, 10); i++) Assert.Contains(prefix + i.ToString("D2"), message);
        if (count > 10) { Assert.DoesNotContain(prefix + "10", message); Assert.Contains($"và {count - 10} lịch khác", message); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceptionIntakeAndAppointmentCheckInRejectDoctorOnCurrentApprovedLeave(bool checkIn)
    {
        var fixture = await CreateFixture(today: true);
        using (var scope = Factory.Services.CreateScope())
        {
            var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamNow;
            await AddLeave(fixture, DoctorLeaveRequestStatus.Approved, now.AddHours(-1), now.AddHours(1));
        }
        await AuthenticateAsync("rec@test.com");
        var response = checkIn
            ? await Client.PostAsJsonAsync("/api/v1/patient-visits/check-in-appointment", new { appointmentId = fixture.Appointment.Id, facilityId = fixture.FacilityId, departmentId = fixture.DepartmentId })
            : await Client.PostAsJsonAsync("/api/v1/patient-visits/intake", new { idempotencyKey = Guid.NewGuid().ToString(), existingPatientId = Patient1EntityId, facilityId = fixture.FacilityId, departmentId = fixture.DepartmentId, assignedDoctorId = fixture.DoctorId, chiefComplaint = "Tiếp nhận kiểm thử lịch nghỉ" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("DOCTOR_ON_LEAVE", body.GetProperty("errorCode").GetString());
        Assert.Equal("Bác sĩ đang trong lịch nghỉ đã được duyệt, vui lòng chọn bác sĩ khác.", body.GetProperty("message").GetString());
        using var verifyScope = Factory.Services.CreateScope();
        Assert.False(await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.AnyAsync(v => v.FacilityId == fixture.FacilityId));
    }

    [Theory]
    [InlineData(DoctorLeaveRequestStatus.Pending, 0)]
    [InlineData(DoctorLeaveRequestStatus.Rejected, 0)]
    [InlineData(DoctorLeaveRequestStatus.Approved, -1)]
    [InlineData(DoctorLeaveRequestStatus.Approved, 1)]
    public async Task IntakeAllowsPendingRejectedAndNonCurrentApprovedLeave(DoctorLeaveRequestStatus status, int daysAway)
    {
        var fixture = await CreateFixture(today: true);
        long patientId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var patient = new Patient { MedicalRecordNumber = "LEAVE-" + Guid.NewGuid().ToString("N"), FullName = "Người bệnh vãng lai kiểm thử" };
            db.Patients.Add(patient); await db.SaveChangesAsync(); patientId = patient.Id;
            var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamNow.AddDays(daysAway);
            await AddLeave(fixture, status, now.AddHours(-1), now.AddHours(1));
        }
        await AuthenticateAsync("rec@test.com");
        var response = await Client.PostAsJsonAsync("/api/v1/patient-visits/intake", new { idempotencyKey = Guid.NewGuid().ToString(), existingPatientId = patientId, facilityId = fixture.FacilityId, departmentId = fixture.DepartmentId, assignedDoctorId = fixture.DoctorId, chiefComplaint = "Tiếp nhận kiểm thử lịch nghỉ không chặn" });
        Assert.True(response.IsSuccessStatusCode, (await Body(response)).ToString());
        using var verifyScope = Factory.Services.CreateScope();
        var visit = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().PatientVisits.SingleAsync(v => v.PatientId == patientId);
        Assert.Equal(fixture.DoctorId, visit.AssignedDoctorId);
    }
}
