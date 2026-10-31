using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class StrictAppointmentCheckInTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static int _minute = 720;

    private async Task<(Appointment Appointment, Department Department, Room Room)> FixtureAsync(AppDbContext db, AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        var code = Guid.NewGuid().ToString("N")[..10];
        var facility = new Facility { Code = "CI-" + code, Name = "Cơ sở check-in", IsActive = true };
        var department = new Department { Facility = facility, Code = code, Name = "Khoa tiếp nhận", SpecialtyId = SpecialtyEntityId, IsActive = true };
        var room = new Room { Department = department, RoomNumber = "101", Name = "Phòng khám", IsActive = true };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { FacilityId = facility.Id, UserId = DoctorId, Role = "Doctor", IsActive = true });
        var clock = Factory.Services.GetRequiredService<IDateTimeProvider>();
        var time = new TimeOnly(0, 0).AddMinutes(Interlocked.Increment(ref _minute));
        var appointment = new Appointment { AppointmentCode = "APT-CI-" + code, PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId, FacilityId = facility.Id, AppointmentDate = clock.VietnamToday,
            StartTime = time, EndTime = time.AddMinutes(30), Status = status,
            AppointmentSlot = new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = clock.VietnamToday, StartTime = time, EndTime = time.AddMinutes(30) } };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return (appointment, department, room);
    }

    private PatientVisitService Service(AppDbContext db, IServiceProvider services)
    {
        var current = new Mock<ICurrentUserService>(); current.SetupGet(user => user.UserId).Returns(ReceptionistId);
        return new PatientVisitService(db, current.Object, services.GetRequiredService<IDateTimeProvider>(),
            services.GetRequiredService<IMrnGenerator>(), Mock.Of<IFacilityAuthorizationService>());
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending, "CHECKIN_APPOINTMENT_PENDING")]
    [InlineData(AppointmentStatus.NoShow, "CHECKIN_APPOINTMENT_NO_SHOW")]
    [InlineData(AppointmentStatus.CheckedIn, "CHECKIN_VISIT_MISSING")]
    [InlineData(AppointmentStatus.PendingReschedule, "CHECKIN_APPOINTMENT_NOT_CONFIRMED")]
    [InlineData(AppointmentStatus.PendingCancellation, "CHECKIN_APPOINTMENT_NOT_CONFIRMED")]
    [InlineData(AppointmentStatus.InConsultation, "CHECKIN_APPOINTMENT_NOT_CONFIRMED")]
    public async Task FreshCheckIn_RequiresConfirmedWithoutChangingAppointment(AppointmentStatus status, string code)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (appointment, _, _) = await FixtureAsync(db, status);
        var error = await Assert.ThrowsAsync<BusinessException>(() => Service(db, scope.ServiceProvider).CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id }));
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(status, appointment.Status);
        Assert.False(await db.PatientVisits.AnyAsync(visit => visit.AppointmentId == appointment.Id));
        Assert.False(await db.AppointmentHistories.AnyAsync(history => history.AppointmentId == appointment.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrInactiveSpecialtyDepartment_DoesNotFallbackToAnotherDepartment(bool inactive)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (appointment, department, _) = await FixtureAsync(db);
        if (inactive) department.IsActive = false;
        else department.SpecialtyId = CardiologySpecialtyId;
        db.Departments.Add(new Department { FacilityId = department.FacilityId, Code = "OTHER", Name = "Khoa khác", SpecialtyId = CardiologySpecialtyId, IsActive = true });
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => Service(db, scope.ServiceProvider).CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id }));
        Assert.Equal("CHECKIN_SPECIALTY_DEPARTMENT_UNAVAILABLE", error.ErrorCode);
        Assert.False(await db.PatientVisits.AnyAsync(visit => visit.AppointmentId == appointment.Id));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("wrong-specialty")]
    [InlineData("other-facility")]
    public async Task InvalidExplicitDepartment_IsRejectedInsteadOfIgnored(string variant)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (appointment, department, _) = await FixtureAsync(db);
        var invalid = new Department { FacilityId = department.FacilityId, Code = "INVALID", Name = "Khoa không hợp lệ", SpecialtyId = SpecialtyEntityId, IsActive = true };
        if (variant == "inactive") invalid.IsActive = false;
        if (variant == "wrong-specialty") invalid.SpecialtyId = CardiologySpecialtyId;
        if (variant == "other-facility") invalid.FacilityId = await db.Facilities.Where(f => f.Id != department.FacilityId).Select(f => f.Id).FirstAsync();
        db.Departments.Add(invalid);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => Service(db, scope.ServiceProvider).CheckInAppointmentAsync(new AppointmentCheckInRequest
            { AppointmentId = appointment.Id, DepartmentId = variant == "missing" ? long.MaxValue : invalid.Id }));
        Assert.Equal("CHECKIN_DEPARTMENT_INVALID", error.ErrorCode);
        Assert.False(await db.PatientVisits.AnyAsync(visit => visit.AppointmentId == appointment.Id));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("other-department")]
    public async Task InvalidExplicitRoom_IsRejectedInsteadOfIgnored(string variant)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (appointment, department, room) = await FixtureAsync(db);
        if (variant == "inactive") room.IsActive = false;
        if (variant == "other-department") room.DepartmentId = await db.Departments.Where(d => d.Id != department.Id).Select(d => d.Id).FirstAsync();
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => Service(db, scope.ServiceProvider).CheckInAppointmentAsync(new AppointmentCheckInRequest
            { AppointmentId = appointment.Id, RoomId = variant == "missing" ? long.MaxValue : room.Id }));
        Assert.Equal("CHECKIN_ROOM_INVALID", error.ErrorCode);
        Assert.False(await db.PatientVisits.AnyAsync(visit => visit.AppointmentId == appointment.Id));
    }

    [Fact]
    public async Task ValidCheckIn_ReturnsSameActiveTicketEvenWhenSubsequentStatusIsNotConfirmed()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (appointment, department, room) = await FixtureAsync(db);
        var service = Service(db, scope.ServiceProvider);
        var ticket = await service.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id, DepartmentId = department.Id, RoomId = room.Id });
        Assert.Equal(room.Id, ticket.RoomId);
        foreach (var status in new[] { AppointmentStatus.Pending, AppointmentStatus.NoShow, AppointmentStatus.CheckedIn })
        {
            appointment.Status = status;
            await db.SaveChangesAsync();
            var repeated = await service.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id });
            Assert.Equal(ticket.VisitId, repeated.VisitId);
            Assert.Equal(ticket.QueueNumber, repeated.QueueNumber);
        }
        Assert.Single(await db.PatientVisits.Where(visit => visit.AppointmentId == appointment.Id).ToListAsync());
    }
}
