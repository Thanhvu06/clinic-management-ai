using System.Data;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class TwentyFourSevenDemoTopUp
{
    private static readonly string[] DoctorEmails = ["doctor@cliniccare.local", "bacsi.02@cliniccare.local", "bacsi.03@cliniccare.local"];

    internal static async Task ApplyAsync(AppDbContext db, IDateTimeProvider clock, DemoTopUpOptions options,
        ILogger logger, CancellationToken ct = default, bool initialSeed = false, Guid? seedActor = null)
    {
        if (!db.OperatingHours.IsTwentyFourSeven) return;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                await ApplyCoreAsync(db, clock, options, logger, ct, initialSeed, seedActor);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                db.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private static async Task ApplyCoreAsync(AppDbContext db, IDateTimeProvider clock, DemoTopUpOptions options,
        ILogger logger, CancellationToken ct, bool initialSeed, Guid? seedActor)
    {
        var users = await db.Users.Where(x => DoctorEmails.Contains(x.Email) && x.IsActive).ToListAsync(ct);
        var userIds = users.Select(x => x.Id).ToArray();
        var doctors = await db.Doctors.Include(x => x.DoctorSpecialties).ThenInclude(x => x.Specialty)
            .Where(x => userIds.Contains(x.UserId) && x.IsActive).ToListAsync(ct);
        doctors = DoctorEmails.Select(email => doctors.Single(x => x.UserId == users.Single(u => u.Email == email).Id)).ToList();
        var actor = seedActor ?? await db.Users.Where(x => x.Email == "reception@cliniccare.local" && x.IsActive).Select(x => x.Id).SingleAsync(ct);
        await TwentyFourSevenDemoSeed.EnsurePatientsAsync(db, clock, ct);
        var emails = Enumerable.Range(1, 16).Select(x => $"patient.{x:D2}@cliniccare.local")
            .Concat(Enumerable.Range(1, 16).Select(x => $"patient.child.{x:D2}@cliniccare.local")).Append("patient@cliniccare.local").ToArray();
        var phones = Enumerable.Range(1, 32).Select(x => $"089900{x:D4}").ToArray();
        var patients = await db.Patients.Where(x => emails.Contains(x.Email) || phones.Contains(x.PhoneNumber)).OrderBy(x => x.Id).ToListAsync(ct);
        var demoPatient = patients.Single(x => x.Email == "patient@cliniccare.local");
        var now = clock.VietnamNow;
        var earliest = now.AddMinutes(30);
        var horizon = initialSeed ? earliest.AddHours(24) : now.AddHours(24);
        var today = clock.VietnamToday;
        var lastDay = DateOnly.FromDateTime(horizon);
        var doctorIds = doctors.Select(x => x.Id).ToArray();
        await DevelopmentDemoTopUpSlots.EnsureAsync(db, doctorIds, today, lastDay, ct);
        var appointments = await db.Appointments.Where(x => x.AppointmentDate >= today && x.AppointmentDate <= lastDay).ToListAsync(ct);
        var visits = await db.PatientVisits.Where(x => x.VisitDate >= today && x.VisitDate <= lastDay).ToListAsync(ct);
        var used = appointments.Select(x => (x.PatientId, x.AppointmentDate)).Concat(visits.Select(x => (x.PatientId, x.VisitDate))).ToHashSet();
        await EnsureWaitingAsync(db, clock, doctors[0], patients, used, visits, actor, ct);
        var schedules = await db.DoctorWorkSchedules.Where(x => doctorIds.Contains(x.DoctorId) && x.IsActive && x.WorkDate >= today && x.WorkDate <= lastDay).ToListAsync(ct);
        var leaves = await db.DoctorLeaveRequests.Where(x => doctorIds.Contains(x.DoctorId) && (x.Status == DoctorLeaveRequestStatus.Pending || x.Status == DoctorLeaveRequestStatus.Approved)).ToListAsync(ct);
        var assignments = await db.StaffFacilityAssignments.Include(x => x.Facility).Include(x => x.Department)
            .Where(x => userIds.Contains(x.UserId) && x.IsActive && x.Role == "Doctor" && x.Facility.IsActive)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id).ToListAsync(ct);
        var departments = await db.Departments.Where(x => x.IsActive).ToListAsync(ct);
        var candidates = (await db.AppointmentSlots.Where(x => doctorIds.Contains(x.DoctorId) && !x.IsBooked && x.SlotDate >= today && x.SlotDate <= lastDay).ToListAsync(ct))
            .Where(x => x.SlotDate.ToDateTime(x.StartTime) >= earliest && x.SlotDate.ToDateTime(x.StartTime) < horizon &&
                x.EndTime.ToTimeSpan() - x.StartTime.ToTimeSpan() == TimeSpan.FromMinutes(30) &&
                schedules.Any(s => s.DoctorId == x.DoctorId && s.WorkDate == x.SlotDate && s.StartTime <= x.StartTime && s.EndTime >= x.EndTime) &&
                !leaves.Any(l => l.DoctorId == x.DoctorId && l.StartDateTime < x.SlotDate.ToDateTime(x.EndTime) && l.EndDateTime > x.SlotDate.ToDateTime(x.StartTime)) &&
                !appointments.Any(a => a.DoctorId == x.DoctorId && a.AppointmentDate == x.SlotDate && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status) && a.StartTime < x.EndTime && a.EndTime > x.StartTime))
            .OrderBy(x => x.SlotDate).ThenBy(x => x.StartTime).ThenBy(x => x.DoctorId).ToList();
        var start = new DateTime(earliest.Year, earliest.Month, earliest.Day, earliest.Hour, earliest.Minute < 30 ? 0 : 30, 0);
        if (start < earliest) start = start.AddMinutes(30);
        var preferred = Enumerable.Range(0, 24).Select(index =>
        {
            var at = start.AddHours(index);
            if (TimeOnly.FromDateTime(at) == new TimeOnly(23, 30)) at = at.AddMinutes(30);
            return candidates.FirstOrDefault(x => x.DoctorId == doctors[index % doctors.Count].Id && x.SlotDate == DateOnly.FromDateTime(at) && x.StartTime == TimeOnly.FromDateTime(at));
        }).OfType<AppointmentSlot>().ToList();
        if (!initialSeed) preferred = preferred.Where((_, i) => i % 2 == 0).Concat(preferred.Where((_, i) => i % 2 != 0)).ToList();
        candidates = preferred.Concat(candidates).DistinctBy(x => x.Id).ToList();
        bool Upcoming(Appointment a) => doctorIds.Contains(a.DoctorId) && a.AppointmentDate.ToDateTime(a.StartTime) >= (initialSeed ? earliest : now) && a.AppointmentDate.ToDateTime(a.StartTime) < horizon;
        var confirmed = appointments.Count(x => Upcoming(x) && x.Status == AppointmentStatus.Confirmed);
        var pending = appointments.Count(x => Upcoming(x) && x.Status == AppointmentStatus.Pending);
        var targetConfirmed = Math.Clamp(options.MinUpcomingConfirmed, 0, 100);
        var targetPending = Math.Clamp(options.MinPendingConfirmations, 0, 100);
        var demoBooked = appointments.Any(x => x.PatientId == demoPatient.Id && Upcoming(x) && x.Status is AppointmentStatus.Confirmed or AppointmentStatus.Pending);
        if (!initialSeed && !demoBooked)
        {
            var demoSlot = candidates.FirstOrDefault(slot =>
            {
                var doctor = doctors.Single(x => x.Id == slot.DoctorId);
                var specialty = doctor.DoctorSpecialties.FirstOrDefault(x => x.IsPrimary && x.Specialty.IsActive)?.Specialty;
                return specialty is not null && !used.Contains((demoPatient.Id, slot.SlotDate)) &&
                    DemoPatientDevelopmentSeed.Allows(demoPatient, specialty.SpecialtyCode, slot.SlotDate) &&
                    assignments.Any(a => a.UserId == doctor.UserId && departments.Any(d => d.FacilityId == a.FacilityId && d.SpecialtyId == specialty.Id));
            });
            if (demoSlot is not null) candidates = new[] { demoSlot }.Concat(candidates.Where(x => x.Id != demoSlot.Id)).ToList();
        }
        foreach (var slot in candidates)
        {
            if (confirmed >= targetConfirmed && pending >= targetPending && demoBooked) break;
            var doctor = doctors.Single(x => x.Id == slot.DoctorId);
            var specialty = doctor.DoctorSpecialties.FirstOrDefault(x => x.IsPrimary && x.Specialty.IsActive)?.Specialty;
            if (specialty is null) continue;
            var facility = assignments.FirstOrDefault(x => x.UserId == doctor.UserId && departments.Any(d => d.FacilityId == x.FacilityId && d.SpecialtyId == specialty.Id));
            if (facility is null) continue;
            var at = slot.SlotDate.ToDateTime(slot.StartTime);
            var chooseDemo = !demoBooked && (!initialSeed || at >= now.AddHours(1) && at <= now.AddHours(6)) && !used.Contains((demoPatient.Id, slot.SlotDate)) && DemoPatientDevelopmentSeed.Allows(demoPatient, specialty.SpecialtyCode, slot.SlotDate);
            var patient = chooseDemo ? demoPatient : patients.FirstOrDefault(x => x.Id != demoPatient.Id && !used.Contains((x.Id, slot.SlotDate)) && DemoPatientDevelopmentSeed.Allows(x, specialty.SpecialtyCode, slot.SlotDate));
            if (patient is null) continue;
            if (confirmed >= targetConfirmed && pending >= targetPending && !chooseDemo) continue;
            var status = confirmed < targetConfirmed || chooseDemo ? AppointmentStatus.Confirmed : AppointmentStatus.Pending;
            var appointment = CreateAppointment(slot, doctor, specialty, patient, facility.FacilityId, status, actor, clock, confirmed + pending);
            db.Appointments.Add(appointment);
            appointments.Add(appointment);
            slot.IsBooked = true;
            used.Add((patient.Id, slot.SlotDate));
            demoBooked |= chooseDemo;
            if (status == AppointmentStatus.Confirmed) confirmed++; else pending++;
        }
        if (confirmed < targetConfirmed || pending < targetPending || !demoBooked)
            logger.LogWarning("Demo top-up thiếu nguồn hợp lệ: Confirmed {Confirmed}/{TargetConfirmed}, Pending {Pending}/{TargetPending}, lịch bệnh nhân demo {DemoBooked}.", confirmed, targetConfirmed, pending, targetPending, demoBooked);
    }

    private static Appointment CreateAppointment(AppointmentSlot slot, Doctor doctor, Specialty specialty, Patient patient,
        long facilityId, AppointmentStatus status, Guid actor, IDateTimeProvider clock, int variant)
    {
        var histories = new List<AppointmentHistory>
        {
            new() { Action = AppointmentHistoryAction.Created, NewStatus = AppointmentStatus.Pending,
                PerformedByUserId = patient.UserId ?? actor, CreatedAt = clock.UtcNow }
        };
        if (status == AppointmentStatus.Confirmed)
            histories.Add(new() { Action = AppointmentHistoryAction.Confirmed, OldStatus = AppointmentStatus.Pending,
                NewStatus = AppointmentStatus.Confirmed, PerformedByUserId = actor, CreatedAt = clock.UtcNow });
        return new Appointment
        {
            AppointmentCode = $"APT-{slot.SlotDate:yyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            PatientId = patient.Id, DoctorId = doctor.Id, SpecialtyId = specialty.Id, FacilityId = facilityId,
            AppointmentSlotId = slot.Id, AppointmentDate = slot.SlotDate, StartTime = slot.StartTime, EndTime = slot.EndTime,
            Status = status, Reason = RealisticDevelopmentSeed.Reason(specialty.SpecialtyCode, variant), Histories = histories
        };
    }

    private static async Task EnsureWaitingAsync(AppDbContext db, IDateTimeProvider clock, Doctor doctor, List<Patient> patients,
        HashSet<(long, DateOnly)> used, List<PatientVisit> visits, Guid actor, CancellationToken ct)
    {
        if (visits.Any(x => x.AssignedDoctorId == doctor.Id && x.VisitDate == clock.VietnamToday && x.Status == VisitStatus.WaitingForDoctor)) return;
        var specialty = doctor.DoctorSpecialties.First(x => x.IsPrimary && x.Specialty.IsActive).Specialty;
        var department = await db.Departments.Where(x => x.IsActive && x.SpecialtyId == specialty.Id && x.Facility.IsActive &&
            db.StaffFacilityAssignments.Any(a => a.UserId == doctor.UserId && a.FacilityId == x.FacilityId && a.IsActive && a.Role == "Doctor" && (!a.DepartmentId.HasValue || a.DepartmentId == x.Id)))
            .OrderBy(x => x.Id).FirstAsync(ct);
        var patient = patients.First(x => x.Email != "patient@cliniccare.local" && !used.Contains((x.Id, clock.VietnamToday)) && DemoPatientDevelopmentSeed.Allows(x, specialty.SpecialtyCode, clock.VietnamToday));
        var queue = visits.Where(x => x.DepartmentId == department.Id && x.VisitDate == clock.VietnamToday).Select(x => x.QueueNumber).DefaultIfEmpty(999).Max() + 1;
        while (await db.PatientVisits.AnyAsync(x => x.VisitCode == $"VIS-{clock.VietnamToday:yyyyMMdd}-{queue:D4}", ct)) queue++;
        db.PatientVisits.Add(new PatientVisit
        {
            VisitCode = $"VIS-{clock.VietnamToday:yyyyMMdd}-{queue:D4}",
            PatientId = patient.Id, FacilityId = department.FacilityId, DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id, VisitDate = clock.VietnamToday, QueueNumber = queue, Status = VisitStatus.WaitingForDoctor,
            CheckedInAtUtc = clock.UtcNow, CreatedAtUtc = clock.UtcNow, CreatedByUserId = actor, ChiefComplaint = "Khám nội tổng quát"
        });
        used.Add((patient.Id, clock.VietnamToday));
    }
}
