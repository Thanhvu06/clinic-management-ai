using System.Text.Json;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Organization;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class CheckInDateAndReceptionFlowTests : IntegrationTestBase
{
    public CheckInDateAndReceptionFlowTests(CustomWebApplicationFactory factory) : base(factory) { }
    private static readonly DateOnly Today = new(2026, 10, 4);
    private static int _slotMinute;
    private static string Code() => "K-" + Guid.NewGuid().ToString("N")[..16];

    private static Mock<IDateTimeProvider> Clock()
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc));
        clock.SetupGet(x => x.VietnamNow).Returns(new DateTime(2026, 10, 4, 1, 0, 0));
        clock.SetupGet(x => x.VietnamToday).Returns(Today);
        clock.SetupGet(x => x.VietnamTime).Returns(new TimeOnly(1, 0));
        return clock;
    }

    private static ICurrentUserService Actor(Guid id)
    {
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(x => x.UserId).Returns(id);
        return actor.Object;
    }

    private static IDoctorContextService DoctorContext()
    {
        var doctor = new Mock<IDoctorContextService>();
        doctor.Setup(x => x.GetCurrentActiveDoctorAsync()).ReturnsAsync(new Doctor { Id = DoctorEntityId, UserId = DoctorId });
        return doctor.Object;
    }

    private async Task<Appointment> AppointmentAsync(AppDbContext db, DateOnly date, AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        var department = await db.Departments.FirstAsync(x => x.SpecialtyId == SpecialtyEntityId && x.IsActive);
        var start = new TimeOnly(0, 0).AddMinutes(Interlocked.Increment(ref _slotMinute));
        var appointment = new Appointment
        {
            AppointmentCode = Code(), PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId, FacilityId = department.FacilityId, AppointmentDate = date,
            StartTime = start, EndTime = start.AddMinutes(1), Status = status,
            AppointmentSlot = new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = date,
                StartTime = start, EndTime = start.AddMinutes(1), IsBooked = true }
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    [Theory]
    [InlineData("visit")]
    [InlineData("reception")]
    [InlineData("doctor")]
    public async Task K1_Checkin_rejects_yesterday_and_tomorrow_accepts_Vietnam_today_and_preserves_existing_checkin(string entry)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = Clock();
        var access = new Mock<IFacilityAuthorizationService>();
        var actor = Actor(entry == "doctor" ? DoctorId : ReceptionistId);
        var visits = new PatientVisitService(db, actor, clock.Object, scope.ServiceProvider.GetRequiredService<IMrnGenerator>(), access.Object);
        var reception = new ReceptionService(db, actor, clock.Object, access.Object);
        var doctor = new DoctorAppointmentService(db, DoctorContext(), clock.Object, actor, visits);
        async Task CheckInAsync(long id)
        {
            if (entry == "visit") await visits.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = id });
            else if (entry == "reception") await reception.CheckInAppointmentAsync(id);
            else await doctor.CheckInAppointmentAsync(id);
        }
        foreach (var offset in new[] { 1, -1 })
        {
            var appointment = await AppointmentAsync(db, Today.AddDays(offset));
            var error = await Assert.ThrowsAsync<BusinessException>(() => CheckInAsync(appointment.Id));
            Assert.Equal("CHECKIN_NOT_TODAY", error.ErrorCode);
            Assert.Equal($"Lịch hẹn ngày {appointment.AppointmentDate:dd/MM/yyyy}; chỉ tiếp nhận được vào đúng ngày khám.", error.Message);
            Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id)).Status);
            Assert.False(await db.PatientVisits.AnyAsync(x => x.AppointmentId == appointment.Id));
            Assert.False(await db.AppointmentHistories.AnyAsync(x => x.AppointmentId == appointment.Id));
        }
        var today = await AppointmentAsync(db, Today);
        await CheckInAsync(today.Id);
        Assert.Equal(AppointmentStatus.CheckedIn, (await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == today.Id)).Status);
        var historyCount = await db.AppointmentHistories.CountAsync(x => x.AppointmentId == today.Id);
        clock.SetupGet(x => x.VietnamToday).Returns(Today.AddDays(1));
        if (entry == "visit")
        {
            var existing = await db.PatientVisits.AsNoTracking().SingleAsync(x => x.AppointmentId == today.Id);
            var ticket = await visits.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = today.Id });
            Assert.Equal(existing.Id, ticket.VisitId);
            Assert.Equal(existing.VisitCode, ticket.VisitCode);
            Assert.Equal(existing.QueueNumber, ticket.QueueNumber);
            Assert.Equal(1, await db.PatientVisits.CountAsync(x => x.AppointmentId == today.Id));
        }
        else if (entry == "reception") await reception.CheckInAppointmentAsync(today.Id);
        Assert.Equal(historyCount, await db.AppointmentHistories.CountAsync(x => x.AppointmentId == today.Id));
    }

    [Fact]
    public async Task K2_NoShow_blocks_active_visit_without_changes_and_preserves_unchecked_and_cancelled_visit_cases()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new DoctorAppointmentService(db, DoctorContext(), Clock().Object, Actor(DoctorId), new Mock<ClinicManagement.Application.Visits.Interfaces.IPatientVisitService>().Object);
        var department = await db.Departments.FirstAsync(x => x.SpecialtyId == SpecialtyEntityId && x.IsActive);
        foreach (var status in new[] { AppointmentStatus.CheckedIn, AppointmentStatus.Confirmed, AppointmentStatus.Pending })
        {
            var appointment = await AppointmentAsync(db, Today, status);
            var visit = new PatientVisit { VisitCode = Code(), AppointmentId = appointment.Id, PatientId = Patient1EntityId,
                FacilityId = department.FacilityId, DepartmentId = department.Id, AssignedDoctorId = DoctorEntityId,
                VisitDate = Today, QueueNumber = TestQueueNumbers.Next(), Status = VisitStatus.WaitingForDoctor, CreatedByUserId = ReceptionistId };
            db.PatientVisits.Add(visit);
            await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<BusinessException>(() => service.MarkNoShowAsync(appointment.Id, new NoShowAppointmentDto()));
            Assert.Equal("APPOINTMENT_ALREADY_CHECKED_IN", error.ErrorCode);
            Assert.Equal("Bệnh nhân đã được tiếp nhận vào hàng đợi, không thể đánh dấu vắng mặt.", error.Message);
            Assert.Equal(status, (await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id)).Status);
            Assert.Equal(VisitStatus.WaitingForDoctor, (await db.PatientVisits.AsNoTracking().SingleAsync(x => x.Id == visit.Id)).Status);
            Assert.False(await db.AppointmentHistories.AnyAsync(x => x.AppointmentId == appointment.Id));
        }
        foreach (var cancelledVisit in new[] { false, true })
        {
            var appointment = await AppointmentAsync(db, Today);
            if (cancelledVisit)
            {
                db.PatientVisits.Add(new PatientVisit { VisitCode = Code(), AppointmentId = appointment.Id, PatientId = Patient1EntityId,
                    FacilityId = department.FacilityId, DepartmentId = department.Id, VisitDate = Today,
                    QueueNumber = TestQueueNumbers.Next(), Status = VisitStatus.Cancelled, CreatedByUserId = ReceptionistId });
                await db.SaveChangesAsync();
            }
            await service.MarkNoShowAsync(appointment.Id, new NoShowAppointmentDto());
            Assert.Equal(AppointmentStatus.NoShow, (await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id)).Status);
        }
    }

    [Fact]
    public async Task K3_Confirm_rejects_past_date_and_accepts_today_and_future()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new ReceptionService(db, Actor(ReceptionistId), Clock().Object, new Mock<IFacilityAuthorizationService>().Object);
        var past = await AppointmentAsync(db, Today.AddDays(-1), AppointmentStatus.Pending);
        var error = await Assert.ThrowsAsync<BusinessException>(() => service.ConfirmAppointmentAsync(past.Id));
        Assert.Equal("APPOINTMENT_DATE_PASSED", error.ErrorCode);
        Assert.Equal("Lịch hẹn ngày 03/10/2026 đã qua, không thể xác nhận.", error.Message);
        Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == past.Id)).Status);
        Assert.False(await db.AppointmentHistories.AnyAsync(x => x.AppointmentId == past.Id));
        foreach (var date in new[] { Today, Today.AddDays(1) })
        {
            var appointment = await AppointmentAsync(db, date, AppointmentStatus.Pending);
            await service.ConfirmAppointmentAsync(appointment.Id);
            Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id)).Status);
        }
    }

    [Fact]
    public async Task K4_Department_read_DTO_exposes_existing_specialty_for_list_and_detail()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await db.Departments.FirstAsync(x => x.SpecialtyId == SpecialtyEntityId);
        var service = new OrganizationService(db, new Mock<IFacilityAuthorizationService>().Object);
        var list = await service.GetDepartmentsAsync(department.FacilityId);
        var detail = await service.GetDepartmentByIdAsync(department.Id);
        foreach (var dto in new[] { list.Single(x => x.Id == department.Id), detail })
        {
            var json = JsonSerializer.SerializeToElement(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.True(json.TryGetProperty("specialtyId", out var specialty));
            Assert.Equal(SpecialtyEntityId, specialty.GetInt64());
        }
    }
}
