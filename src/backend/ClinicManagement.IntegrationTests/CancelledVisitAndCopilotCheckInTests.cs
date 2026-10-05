using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Tools;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class CancelledVisitAndCopilotCheckInTests : IntegrationTestBase
{
    private static readonly DateOnly Today = new(2026, 10, 5);
    private static int _minute = 400;
    private static int _cancelledQueue = 700;

    public CancelledVisitAndCopilotCheckInTests(CustomWebApplicationFactory factory) : base(factory) { }

    private sealed class Fixture : IDisposable
    {
        private readonly IServiceScope _scope;
        public AppDbContext Db { get; }
        public IDateTimeProvider Clock { get; }
        public PatientVisitService Visits { get; }
        public DoctorAppointmentService Doctor { get; }

        public Fixture(CustomWebApplicationFactory factory)
        {
            _scope = factory.Services.CreateScope();
            Db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(x => x.VietnamToday).Returns(Today);
            clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc));
            clock.SetupGet(x => x.VietnamNow).Returns(new DateTime(2026, 10, 5, 10, 0, 0));
            clock.SetupGet(x => x.VietnamTime).Returns(new TimeOnly(10, 0));
            Clock = clock.Object;
            Visits = VisitService(ReceptionistId);
            var doctor = new Mock<IDoctorContextService>();
            doctor.Setup(x => x.GetCurrentActiveDoctorAsync()).ReturnsAsync(new Doctor { Id = DoctorEntityId, UserId = DoctorId });
            Doctor = new DoctorAppointmentService(Db, doctor.Object, Clock, Actor(DoctorId), VisitService(DoctorId));
        }

        private static ICurrentUserService Actor(Guid userId)
        {
            var actor = new Mock<ICurrentUserService>();
            actor.SetupGet(x => x.UserId).Returns(userId);
            return actor.Object;
        }

        private PatientVisitService VisitService(Guid userId) => new(Db, Actor(userId), Clock,
            _scope.ServiceProvider.GetRequiredService<IMrnGenerator>(),
            _scope.ServiceProvider.GetRequiredService<IFacilityAuthorizationService>());

        public RoleConfirmedActionToolHandler Handler() => new(Db, Actor(ReceptionistId), Clock, Visits,
            Mock.Of<IDiagnosticWorkflowService>(), Doctor, Mock.Of<IPharmacyService>());

        public async Task<(AiToolInvocation Invocation, AiToolExecutionContext Context)> PrepareCheckInAsync(Appointment appointment)
        {
            var department = await Db.Departments.FirstAsync(x => x.FacilityId == appointment.FacilityId && x.IsActive);
            var sessionId = "sess_r3_" + Guid.NewGuid().ToString("N");
            AiToolExecutionContext Context(AiToolInvocationChannel channel) => new()
            {
                ActorId = ReceptionistId, IsAuthenticated = true,
                Roles = new HashSet<AiActorRole> { AiActorRole.Receptionist },
                SessionId = sessionId, InvocationChannel = channel
            };
            var prepared = await Handler().ExecuteAsync(new AiToolInvocation
            {
                ToolName = "reception.prepare_check_in_appointment", ToolVersion = "1.0",
                ArgumentsJson = JsonSerializer.Serialize(new { appointmentId = appointment.Id, departmentId = department.Id })
            }, Context(AiToolInvocationChannel.DirectHumanPreparation));
            Assert.Null(prepared.Error);
            Assert.True(prepared.RequiresConfirmation);
            Assert.NotNull(prepared.ActionId);
            var token = JsonSerializer.SerializeToElement(prepared.Data).GetProperty("confirmationToken").GetString();
            return (new AiToolInvocation
            {
                ToolName = "role.execute_confirmed_action", ToolVersion = "1.0",
                ArgumentsJson = JsonSerializer.Serialize(new { actionId = prepared.ActionId, confirm = true, concurrencyToken = token })
            }, Context(AiToolInvocationChannel.DirectHumanConfirmation));
        }

        public void AdvanceOneMinute()
        {
            var utc = Clock.UtcNow.AddMinutes(1);
            var vietnam = Clock.VietnamNow.AddMinutes(1);
            Mock.Get(Clock).SetupGet(x => x.UtcNow).Returns(utc);
            Mock.Get(Clock).SetupGet(x => x.VietnamNow).Returns(vietnam);
            Mock.Get(Clock).SetupGet(x => x.VietnamTime).Returns(TimeOnly.FromDateTime(vietnam));
        }

        public async Task<Appointment> AppointmentAsync(int dayOffset = 0, AppointmentStatus status = AppointmentStatus.Confirmed)
        {
            var department = await Db.Departments.FirstAsync(x => x.SpecialtyId == SpecialtyEntityId && x.IsActive);
            var date = Today.AddDays(dayOffset);
            var start = new TimeOnly(0, 0).AddMinutes(Interlocked.Increment(ref _minute));
            var appointment = new Appointment
            {
                AppointmentCode = "R-" + Guid.NewGuid().ToString("N"), PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, FacilityId = department.FacilityId,
                AppointmentDate = date, StartTime = start, EndTime = start.AddMinutes(1), Status = status,
                Reason = "Khám định kỳ", AppointmentSlot = new AppointmentSlot
                { DoctorId = DoctorEntityId, SlotDate = date, StartTime = start, EndTime = start.AddMinutes(1), IsBooked = true }
            };
            Db.Appointments.Add(appointment);
            await Db.SaveChangesAsync();
            return appointment;
        }

        public async Task<PatientVisit> CancelledVisitAsync(Appointment appointment)
        {
            var department = await Db.Departments.FirstAsync(x => x.FacilityId == appointment.FacilityId && x.IsActive);
            var visit = new PatientVisit
            {
                VisitCode = "R-V-" + Guid.NewGuid().ToString("N"), PatientId = appointment.PatientId,
                AppointmentId = appointment.Id, FacilityId = department.FacilityId, DepartmentId = department.Id,
                AssignedDoctorId = appointment.DoctorId, VisitDate = appointment.AppointmentDate,
                QueueNumber = Interlocked.Increment(ref _cancelledQueue), ArrivalType = VisitArrivalType.Scheduled, Status = VisitStatus.Cancelled,
                CreatedByUserId = ReceptionistId, CheckedInAtUtc = Clock.UtcNow.AddMinutes(-10),
                CancelledAtUtc = Clock.UtcNow.AddMinutes(-5), CancellationReason = "Đã hủy"
            };
            Db.PatientVisits.Add(visit);
            await Db.SaveChangesAsync();
            return visit;
        }

        public void Dispose() => _scope.Dispose();
    }

    [Fact]
    public async Task R1_Cancellation_releases_only_checked_in_appointments_with_one_attributed_history()
    {
        using var f = new Fixture(Factory);
        foreach (var status in new[] { AppointmentStatus.CheckedIn, AppointmentStatus.InConsultation, AppointmentStatus.Completed, AppointmentStatus.Cancelled })
        {
            var appointment = await f.AppointmentAsync();
            var ticket = await f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id });
            appointment.Status = status;
            await f.Db.SaveChangesAsync();
            var before = await f.Db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.Id);
            await f.Visits.UpdateVisitStatusAsync(ticket.VisitId, VisitStatus.Cancelled, "Bệnh nhân yêu cầu hủy");
            f.Db.ChangeTracker.Clear();
            var visit = await f.Db.PatientVisits.SingleAsync(x => x.Id == ticket.VisitId);
            var saved = await f.Db.Appointments.SingleAsync(x => x.Id == appointment.Id);
            Assert.Equal(VisitStatus.Cancelled, visit.Status);
            Assert.Equal(f.Clock.UtcNow, visit.CancelledAtUtc);
            Assert.Equal("Bệnh nhân yêu cầu hủy", visit.CancellationReason);
            Assert.Equal(status == AppointmentStatus.CheckedIn ? AppointmentStatus.Confirmed : status, saved.Status);
            var histories = await f.Db.AppointmentHistories.Where(x => x.AppointmentId == appointment.Id).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(before + (status == AppointmentStatus.CheckedIn ? 1 : 0), histories.Count);
            if (status == AppointmentStatus.CheckedIn)
            {
                Assert.Equal(AppointmentStatus.CheckedIn, histories[^1].OldStatus);
                Assert.Equal(AppointmentStatus.Confirmed, histories[^1].NewStatus);
                Assert.Equal(ReceptionistId, histories[^1].PerformedByUserId);
                Assert.Contains(ticket.VisitCode, histories[^1].Note);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R1_Recheck_reuses_cancelled_visit_with_new_queue_and_repeated_ticket_is_idempotent(bool doctor)
    {
        using var f = new Fixture(Factory);
        var appointment = await f.AppointmentAsync();
        var first = await f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id });
        await f.Visits.UpdateVisitStatusAsync(first.VisitId, VisitStatus.Cancelled, "Đã hủy");
        f.AdvanceOneMinute();
        var room = new Room { DepartmentId = first.DepartmentId, RoomNumber = "R-" + Guid.NewGuid().ToString("N")[..12], Name = "Phòng tiếp nhận mới", IsActive = true };
        f.Db.Rooms.Add(room);
        await f.Db.SaveChangesAsync();
        var request = new AppointmentCheckInRequest { AppointmentId = appointment.Id, DepartmentId = first.DepartmentId,
            RoomId = room.Id, AssignedDoctorId = Doctor2EntityId };
        var second = doctor ? await f.Doctor.CheckInAppointmentAsync(appointment.Id) : await f.Visits.CheckInAppointmentAsync(request);
        f.Db.ChangeTracker.Clear();
        var visit = await f.Db.PatientVisits.SingleAsync(x => x.AppointmentId == appointment.Id);
        Assert.Equal(first.VisitId, second.VisitId);
        Assert.Equal(first.VisitCode, second.VisitCode);
        Assert.Equal(VisitStatus.WaitingForDoctor, visit.Status);
        Assert.True(second.QueueNumber > first.QueueNumber);
        Assert.Equal(second.QueueNumber, visit.QueueNumber);
        Assert.Equal(Today, visit.VisitDate);
        Assert.Equal(f.Clock.UtcNow, visit.CheckedInAtUtc);
        Assert.Equal(f.Clock.UtcNow, visit.UpdatedAtUtc);
        Assert.Null(visit.CancelledAtUtc);
        Assert.Null(visit.CancellationReason);
        Assert.Equal(doctor ? DoctorEntityId : Doctor2EntityId, visit.AssignedDoctorId);
        Assert.Equal(doctor ? null : (long?)room.Id, visit.RoomId);
        Assert.Equal(AppointmentStatus.CheckedIn, (await f.Db.Appointments.SingleAsync(x => x.Id == appointment.Id)).Status);
        Assert.Equal(1, await f.Db.PatientVisits.CountAsync(x => x.AppointmentId == appointment.Id));
        var historyCount = await f.Db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.Id);
        Assert.Equal(3, historyCount);
        var sequenceBefore = await f.Db.DailyQueueSequences.SingleAsync(x => x.DepartmentId == visit.DepartmentId && x.Date == Today);
        var numberBefore = sequenceBefore.LastNumber;
        var notificationsBefore = await f.Db.Notifications.CountAsync(x => x.RelatedEntityId == appointment.Id.ToString());
        var third = doctor ? await f.Doctor.CheckInAppointmentAsync(appointment.Id) : await f.Visits.CheckInAppointmentAsync(request);
        Assert.Equal(second.VisitId, third.VisitId);
        Assert.Equal(second.VisitCode, third.VisitCode);
        Assert.Equal(second.QueueNumber, third.QueueNumber);
        Assert.Equal(second.CheckedInAtUtc, third.CheckedInAtUtc);
        Assert.Equal(second.ReceptionistName, third.ReceptionistName);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(numberBefore, (await f.Db.DailyQueueSequences.SingleAsync(x => x.DepartmentId == visit.DepartmentId && x.Date == Today)).LastNumber);
        Assert.Equal(historyCount, await f.Db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.Id));
        Assert.Equal(notificationsBefore, await f.Db.Notifications.CountAsync(x => x.RelatedEntityId == appointment.Id.ToString()));
    }

    [Fact]
    public async Task R1_Doctor_recheck_requires_confirmed_even_when_cancelled_visit_exists()
    {
        using var f = new Fixture(Factory);
        var appointment = await f.AppointmentAsync(status: AppointmentStatus.Pending);
        var visit = await f.CancelledVisitAsync(appointment);
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Doctor.CheckInAppointmentAsync(appointment.Id));
        Assert.Equal("INVALID_STATE_TRANSITION", error.ErrorCode);
        Assert.Equal(VisitStatus.Cancelled, (await f.Db.PatientVisits.SingleAsync(x => x.Id == visit.Id)).Status);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, -1)]
    [InlineData(true, 1)]
    [InlineData(true, -1)]
    public async Task R1_Cancelled_recheck_on_other_days_is_rejected_without_mutating_visit(bool doctor, int offset)
    {
        using var f = new Fixture(Factory);
        var appointment = await f.AppointmentAsync(offset);
        var visit = await f.CancelledVisitAsync(appointment);
        var originalQueue = visit.QueueNumber;
        var error = await Assert.ThrowsAsync<BusinessException>(async () =>
        {
            if (doctor) await f.Doctor.CheckInAppointmentAsync(appointment.Id);
            else await f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id });
        });
        Assert.Equal("CHECKIN_NOT_TODAY", error.ErrorCode);
        Assert.Equal($"Lịch hẹn ngày {appointment.AppointmentDate:dd/MM/yyyy}; chỉ tiếp nhận được vào đúng ngày khám.", error.Message);
        f.Db.ChangeTracker.Clear();
        var saved = await f.Db.PatientVisits.SingleAsync(x => x.Id == visit.Id);
        Assert.Equal(VisitStatus.Cancelled, saved.Status);
        Assert.Equal(originalQueue, saved.QueueNumber);
        Assert.NotNull(saved.CancelledAtUtc);
        Assert.Equal("Đã hủy", saved.CancellationReason);
        Assert.Empty(await f.Db.AppointmentHistories.Where(x => x.AppointmentId == appointment.Id).ToListAsync());
    }

    [Fact]
    public async Task R1_Cancelled_recheck_revalidates_appointment_state_facility_and_assigned_doctor()
    {
        using var f = new Fixture(Factory);
        foreach (var status in new[] { AppointmentStatus.Cancelled, AppointmentStatus.Completed })
        {
            var blocked = await f.AppointmentAsync(status: status);
            await f.CancelledVisitAsync(blocked);
            var error = await Assert.ThrowsAsync<BusinessException>(() => f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = blocked.Id }));
            Assert.Equal(status == AppointmentStatus.Cancelled ? "APPOINTMENT_CANCELLED" : "APPOINTMENT_COMPLETED", error.ErrorCode);
        }
        var appointment = await f.AppointmentAsync();
        var visit = await f.CancelledVisitAsync(appointment);
        var facilityError = await Assert.ThrowsAsync<BusinessException>(() => f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest
        { AppointmentId = appointment.Id, FacilityId = appointment.FacilityId + 10000 }));
        Assert.Equal("FACILITY_SCOPE_DENIED", facilityError.ErrorCode);
        var unassignedDoctor = new Doctor { UserId = TechnicianId, IsActive = true };
        f.Db.Doctors.Add(unassignedDoctor);
        await f.Db.SaveChangesAsync();
        var doctorError = await Assert.ThrowsAsync<BusinessException>(() => f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest
        { AppointmentId = appointment.Id, AssignedDoctorId = unassignedDoctor.Id }));
        Assert.Equal("FACILITY_SCOPE_DENIED", doctorError.ErrorCode);
        Assert.Equal(VisitStatus.Cancelled, (await f.Db.PatientVisits.SingleAsync(x => x.Id == visit.Id)).Status);
        var department = new Department { FacilityId = appointment.FacilityId!.Value, SpecialtyId = SpecialtyEntityId,
            Code = "R-" + Guid.NewGuid().ToString("N")[..12], Name = "Khoa tiếp nhận mới", IsActive = true };
        f.Db.Departments.Add(department);
        await f.Db.SaveChangesAsync();
        var room = new Room { DepartmentId = department.Id, RoomNumber = "R-1", Name = "Phòng mới", IsActive = true };
        f.Db.Rooms.Add(room);
        await f.Db.SaveChangesAsync();
        var reopened = await f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest
        { AppointmentId = appointment.Id, DepartmentId = department.Id, RoomId = room.Id, AssignedDoctorId = Doctor2EntityId });
        Assert.Equal(visit.Id, reopened.VisitId);
        Assert.Equal(department.Id, reopened.DepartmentId);
        Assert.Equal(room.Id, reopened.RoomId);
        Assert.Equal(Doctor2EntityId, reopened.AssignedDoctorId);
    }

    [Fact]
    public async Task R1_Released_appointment_can_be_marked_no_show_after_visit_cancellation()
    {
        using var f = new Fixture(Factory);
        var appointment = await f.AppointmentAsync();
        var ticket = await f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id });
        await f.Visits.UpdateVisitStatusAsync(ticket.VisitId, VisitStatus.Cancelled, "Bệnh nhân ra về");
        Assert.Equal(AppointmentStatus.Confirmed, (await f.Db.Appointments.SingleAsync(x => x.Id == appointment.Id)).Status);
        await f.Doctor.MarkNoShowAsync(appointment.Id, new NoShowAppointmentDto());
        Assert.Equal(AppointmentStatus.NoShow, (await f.Db.Appointments.SingleAsync(x => x.Id == appointment.Id)).Status);
        Assert.Equal(VisitStatus.Cancelled, (await f.Db.PatientVisits.SingleAsync(x => x.Id == ticket.VisitId)).Status);
    }

    [Fact]
    public async Task R3_Copilot_rechecks_cancelled_visit_and_confirmation_replay_has_no_domain_writes()
    {
        using var f = new Fixture(Factory);
        var appointment = await f.AppointmentAsync();
        var firstAction = await f.PrepareCheckInAsync(appointment);
        var firstConfirmation = await f.Handler().ExecuteAsync(firstAction.Invocation, firstAction.Context);
        Assert.Null(firstConfirmation.Error);
        Assert.Equal("completed", firstConfirmation.Status);
        f.Db.ChangeTracker.Clear();
        var first = await f.Db.PatientVisits.AsNoTracking().SingleAsync(x => x.AppointmentId == appointment.Id);
        await f.Visits.UpdateVisitStatusAsync(first.Id, VisitStatus.Cancelled, "Bệnh nhân yêu cầu hủy");
        f.AdvanceOneMinute();
        f.Db.ChangeTracker.Clear();
        Assert.Equal(VisitStatus.Cancelled, (await f.Db.PatientVisits.AsNoTracking().SingleAsync(x => x.Id == first.Id)).Status);
        Assert.Equal(AppointmentStatus.Confirmed, (await f.Db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id)).Status);

        var action = await f.PrepareCheckInAsync(appointment);
        var confirmed = await f.Handler().ExecuteAsync(action.Invocation, action.Context);
        Assert.Null(confirmed.Error);
        Assert.Equal("completed", confirmed.Status);
        Assert.False(confirmed.IsIdempotentReplay);
        Assert.Equal("Đã check-in lịch hẹn và tạo lượt khám.", confirmed.DisplayText);
        Assert.Equal(first.Id.ToString(), JsonSerializer.SerializeToElement(confirmed.Data).GetProperty("reference").GetString());
        f.Db.ChangeTracker.Clear();
        var reopened = await f.Db.PatientVisits.AsNoTracking().SingleAsync(x => x.AppointmentId == appointment.Id);
        Assert.Equal(first.Id, reopened.Id);
        Assert.Equal(first.VisitCode, reopened.VisitCode);
        Assert.Equal(VisitStatus.WaitingForDoctor, reopened.Status);
        Assert.True(reopened.QueueNumber > first.QueueNumber);
        Assert.Null(reopened.CancelledAtUtc);
        Assert.Null(reopened.CancellationReason);
        Assert.Equal(f.Clock.UtcNow, reopened.CheckedInAtUtc);
        Assert.Equal(AppointmentStatus.CheckedIn, (await f.Db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointment.Id)).Status);
        Assert.Equal(1, await f.Db.PatientVisits.CountAsync(x => x.AppointmentId == appointment.Id));
        var histories = await f.Db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.Id);
        Assert.Equal(3, histories);
        var queue = await f.Db.DailyQueueSequences.AsNoTracking().SingleAsync(x => x.DepartmentId == reopened.DepartmentId && x.Date == Today);
        var notifications = await f.Db.Notifications.CountAsync(x => x.RelatedEntityId == appointment.Id.ToString());

        var replay = await f.Handler().ExecuteAsync(action.Invocation, action.Context);
        Assert.Null(replay.Error);
        Assert.Equal("completed", replay.Status);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal("idempotent_replay", replay.ResultType);
        Assert.Equal(first.Id.ToString(), JsonSerializer.SerializeToElement(replay.Data).GetProperty("reference").GetString());
        f.Db.ChangeTracker.Clear();
        var afterReplay = await f.Db.PatientVisits.AsNoTracking().SingleAsync(x => x.AppointmentId == appointment.Id);
        Assert.Equal(reopened.QueueNumber, afterReplay.QueueNumber);
        Assert.Equal(reopened.CheckedInAtUtc, afterReplay.CheckedInAtUtc);
        Assert.Equal(queue.LastNumber, (await f.Db.DailyQueueSequences.AsNoTracking().SingleAsync(x => x.DepartmentId == reopened.DepartmentId && x.Date == Today)).LastNumber);
        Assert.Equal(histories, await f.Db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.Id));
        Assert.Equal(notifications, await f.Db.Notifications.CountAsync(x => x.RelatedEntityId == appointment.Id.ToString()));
        Assert.Equal(1, await f.Db.PatientVisits.CountAsync(x => x.AppointmentId == appointment.Id));
    }

    [Fact]
    public async Task R3_Copilot_keeps_already_checked_in_response_for_active_visit_without_domain_writes()
    {
        using var f = new Fixture(Factory);
        var appointment = await f.AppointmentAsync();
        var ticket = await f.Visits.CheckInAppointmentAsync(new AppointmentCheckInRequest { AppointmentId = appointment.Id });
        f.Db.ChangeTracker.Clear();
        var historyCount = await f.Db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.Id);
        var queue = await f.Db.DailyQueueSequences.AsNoTracking().SingleAsync(x => x.DepartmentId == ticket.DepartmentId && x.Date == Today);
        var action = await f.PrepareCheckInAsync(appointment);
        var confirmed = await f.Handler().ExecuteAsync(action.Invocation, action.Context);
        Assert.Null(confirmed.Error);
        Assert.Equal("completed", confirmed.Status);
        Assert.True(confirmed.IsIdempotentReplay);
        Assert.Equal("Lịch hẹn đã được check-in trước đó.", confirmed.DisplayText);
        Assert.Equal(ticket.VisitId.ToString(), JsonSerializer.SerializeToElement(confirmed.Data).GetProperty("reference").GetString());
        f.Db.ChangeTracker.Clear();
        var visit = await f.Db.PatientVisits.AsNoTracking().SingleAsync(x => x.AppointmentId == appointment.Id);
        Assert.Equal(VisitStatus.WaitingForDoctor, visit.Status);
        Assert.Equal(ticket.QueueNumber, visit.QueueNumber);
        Assert.Equal(queue.LastNumber, (await f.Db.DailyQueueSequences.AsNoTracking().SingleAsync(x => x.DepartmentId == ticket.DepartmentId && x.Date == Today)).LastNumber);
        Assert.Equal(historyCount, await f.Db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.Id));
        Assert.Equal(1, await f.Db.PatientVisits.CountAsync(x => x.AppointmentId == appointment.Id));
    }

    [Fact]
    public async Task R2_Preparation_rejects_other_days_without_pending_actions_and_still_prepares_today()
    {
        using var f = new Fixture(Factory);
        var handler = f.Handler();
        var pendingBefore = await f.Db.AiPendingToolActions.CountAsync();
        async Task<AiToolExecutionResult> Prepare(Appointment appointment)
        {
            var department = await f.Db.Departments.FirstAsync(x => x.FacilityId == appointment.FacilityId && x.IsActive);
            return await handler.ExecuteAsync(new AiToolInvocation { ToolName = "reception.prepare_check_in_appointment", ToolVersion = "1.0",
                ArgumentsJson = JsonSerializer.Serialize(new { appointmentId = appointment.Id, departmentId = department.Id }) },
                new AiToolExecutionContext { ActorId = ReceptionistId, IsAuthenticated = true,
                    Roles = new HashSet<AiActorRole> { AiActorRole.Receptionist }, SessionId = "sess_recheck_" + Guid.NewGuid().ToString("N"),
                    InvocationChannel = AiToolInvocationChannel.DirectHumanPreparation });
        }
        foreach (var offset in new[] { 1, -1 })
        {
            var appointment = await f.AppointmentAsync(offset);
            var result = await Prepare(appointment);
            Assert.Equal("failed", result.Status);
            Assert.Equal("CHECKIN_NOT_TODAY", result.Error?.Code);
            Assert.Equal($"Lịch hẹn ngày {appointment.AppointmentDate:dd/MM/yyyy}; chỉ tiếp nhận được vào đúng ngày khám.", result.Error?.Message);
            Assert.False(result.RequiresConfirmation);
            Assert.Null(result.ActionId);
            Assert.Equal(pendingBefore, await f.Db.AiPendingToolActions.CountAsync());
        }
        var todayResult = await Prepare(await f.AppointmentAsync());
        Assert.Null(todayResult.Error);
        Assert.True(todayResult.RequiresConfirmation);
        Assert.NotNull(todayResult.ActionId);
        Assert.Equal(pendingBefore + 1, await f.Db.AiPendingToolActions.CountAsync());
    }
}
