using System.Data;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Pharmacy;
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
        if (!initialSeed)
            await CleanStaleDemoDataAsync(db, clock, options, actor, ct);
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
        var demoHorizon = initialSeed ? horizon : now.AddDays(7);
        var demoLastDay = DateOnly.FromDateTime(demoHorizon);
        var dayStartHour = Math.Clamp(options.DemoPatientDayStartHour, 0, 23);
        var dayEndHour = Math.Clamp(options.DemoPatientDayEndHour, 1, 24);
        if (dayStartHour >= dayEndHour) (dayStartHour, dayEndHour) = (7, 20);
        bool DemoDaytime(TimeOnly time) => time.Hour >= dayStartHour && time.Hour < dayEndHour;
        var doctorIds = doctors.Select(x => x.Id).ToArray();
        var existingFutureSlotIds = await db.AppointmentSlots
            .Where(slot => doctorIds.Contains(slot.DoctorId) && slot.SlotDate >= today && slot.SlotDate <= demoLastDay)
            .Select(slot => slot.Id).ToListAsync(ct);
        await DevelopmentDemoTopUpSlots.EnsureAsync(db, doctorIds, today, lastDay, ct);
        var appointments = await db.Appointments.Where(x => x.AppointmentDate >= today && x.AppointmentDate <= demoLastDay).ToListAsync(ct);
        var visits = await db.PatientVisits.Where(x => x.VisitDate >= today && x.VisitDate <= demoLastDay).ToListAsync(ct);
        var used = appointments.Select(x => (x.PatientId, x.AppointmentDate)).Concat(visits.Select(x => (x.PatientId, x.VisitDate))).ToHashSet();
        await EnsureWaitingAsync(db, clock, doctors[0], patients, used, visits, actor, ct);
        var schedules = await db.DoctorWorkSchedules.Where(x => doctorIds.Contains(x.DoctorId) && x.IsActive && x.WorkDate >= today && x.WorkDate <= demoLastDay).ToListAsync(ct);
        var leaves = await db.DoctorLeaveRequests.Where(x => doctorIds.Contains(x.DoctorId) && (x.Status == DoctorLeaveRequestStatus.Pending || x.Status == DoctorLeaveRequestStatus.Approved)).ToListAsync(ct);
        var assignments = await db.StaffFacilityAssignments.Include(x => x.Facility).Include(x => x.Department)
            .Where(x => userIds.Contains(x.UserId) && x.IsActive && x.Role == "Doctor" && x.Facility.IsActive)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id).ToListAsync(ct);
        var departments = await db.Departments.Where(x => x.IsActive).ToListAsync(ct);
        var available = (await db.AppointmentSlots.Where(x => doctorIds.Contains(x.DoctorId) && !x.IsBooked && x.SlotDate >= today && x.SlotDate <= demoLastDay).ToListAsync(ct))
            .Where(x => x.SlotDate.ToDateTime(x.StartTime) >= earliest && x.SlotDate.ToDateTime(x.StartTime) < demoHorizon &&
                (x.SlotDate.ToDateTime(x.StartTime) < horizon || existingFutureSlotIds.Contains(x.Id)) &&
                x.EndTime.ToTimeSpan() - x.StartTime.ToTimeSpan() == TimeSpan.FromMinutes(30) &&
                schedules.Any(s => s.DoctorId == x.DoctorId && s.WorkDate == x.SlotDate && s.StartTime <= x.StartTime && s.EndTime >= x.EndTime) &&
                !leaves.Any(l => l.DoctorId == x.DoctorId && l.StartDateTime < x.SlotDate.ToDateTime(x.EndTime) && l.EndDateTime > x.SlotDate.ToDateTime(x.StartTime)) &&
                !appointments.Any(a => a.DoctorId == x.DoctorId && a.AppointmentDate == x.SlotDate && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status) && a.StartTime < x.EndTime && a.EndTime > x.StartTime))
            .OrderBy(x => x.SlotDate).ThenBy(x => x.StartTime).ThenBy(x => x.DoctorId).ToList();
        var candidates = available.Where(x => x.SlotDate.ToDateTime(x.StartTime) < horizon).ToList();
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
        var demoBooked = appointments.Any(x => x.PatientId == demoPatient.Id &&
            (initialSeed ? Upcoming(x) : doctorIds.Contains(x.DoctorId) &&
                x.AppointmentDate.ToDateTime(x.StartTime) >= now && x.AppointmentDate.ToDateTime(x.StartTime) < demoHorizon &&
                DemoDaytime(x.StartTime)) && x.Status is AppointmentStatus.Confirmed or AppointmentStatus.Pending);
        if (!initialSeed && !demoBooked)
        {
            var demoSlot = available.FirstOrDefault(slot =>
            {
                var doctor = doctors.Single(x => x.Id == slot.DoctorId);
                var specialty = doctor.DoctorSpecialties.FirstOrDefault(x => x.IsPrimary && x.Specialty.IsActive)?.Specialty;
                return DemoDaytime(slot.StartTime) && specialty is not null && !used.Contains((demoPatient.Id, slot.SlotDate)) &&
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
            var chooseDemo = !demoBooked && (initialSeed ? at >= now.AddHours(1) && at <= now.AddHours(6) : DemoDaytime(slot.StartTime)) && !used.Contains((demoPatient.Id, slot.SlotDate)) && DemoPatientDevelopmentSeed.Allows(demoPatient, specialty.SpecialtyCode, slot.SlotDate);
            var patient = chooseDemo ? demoPatient : slot.SlotDate.ToDateTime(slot.StartTime) < horizon
                ? patients.FirstOrDefault(x => x.Id != demoPatient.Id && !used.Contains((x.Id, slot.SlotDate)) && DemoPatientDevelopmentSeed.Allows(x, specialty.SpecialtyCode, slot.SlotDate)) : null;
            if (patient is null) continue;
            if (confirmed >= targetConfirmed && pending >= targetPending && !chooseDemo) continue;
            var status = confirmed < targetConfirmed || chooseDemo ? AppointmentStatus.Confirmed : AppointmentStatus.Pending;
            var appointment = CreateAppointment(slot, doctor, specialty, patient, facility.FacilityId, status, actor, clock, confirmed + pending);
            db.Appointments.Add(appointment);
            appointments.Add(appointment);
            slot.IsBooked = true;
            used.Add((patient.Id, slot.SlotDate));
            demoBooked |= chooseDemo;
            if (Upcoming(appointment))
            {
                if (status == AppointmentStatus.Confirmed) confirmed++; else pending++;
            }
        }
        if (confirmed < targetConfirmed || pending < targetPending || !demoBooked)
            logger.LogWarning("Demo top-up thiếu nguồn hợp lệ: Confirmed {Confirmed}/{TargetConfirmed}, Pending {Pending}/{TargetPending}, lịch bệnh nhân demo {DemoBooked}.", confirmed, targetConfirmed, pending, targetPending, demoBooked);
    }

    private static async Task CleanStaleDemoDataAsync(AppDbContext db, IDateTimeProvider clock,
        DemoTopUpOptions options, Guid actor, CancellationToken ct)
    {
        const string reason = "Dọn dữ liệu demo cũ";
        var staleHours = options.StaleAfterHours > 0 ? options.StaleAfterHours : 48;
        var cutoffUtc = clock.UtcNow.AddHours(-staleHours);

        var staleOrders = await db.DiagnosticOrders
            .Include(o => o.Items)
            .Where(o => (o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress) &&
                o.OrderedAtUtc <= cutoffUtc)
            .OrderBy(o => o.Id)
            .ToListAsync(ct);

        foreach (var order in staleOrders)
        {
            order.Status = DiagnosticOrderStatus.Cancelled;
            order.CancelledAtUtc = clock.UtcNow;
            var previousNote = order.Note?.Trim();
            order.Note = string.IsNullOrEmpty(previousNote) ? reason
                : $"{previousNote[..Math.Min(previousNote.Length, 980)]}\n{reason}";
            foreach (var item in order.Items.Where(i => i.Status == DiagnosticItemStatus.Ordered || i.Status == DiagnosticItemStatus.InProgress))
                item.Status = DiagnosticItemStatus.Cancelled;
            AddAudit(db, clock, nameof(DiagnosticOrder), order.Id, order.OrderCode, reason);
        }

        var stalePrescriptions = await db.Prescriptions
            .Include(p => p.Items)
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) &&
                p.CreatedAt <= cutoffUtc)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        var reservedPrescriptionIds = stalePrescriptions
            .Where(p => p.Status == PrescriptionStatus.ReservedForPurchase)
            .Select(p => p.Id)
            .ToList();
        if (reservedPrescriptionIds.Count > 0)
        {
            var reservations = await db.MedicineStockTransactions
                .Where(t => t.PrescriptionId.HasValue && reservedPrescriptionIds.Contains(t.PrescriptionId.Value) &&
                    (t.Type == MedicineStockTransactionType.Reservation || t.Type == MedicineStockTransactionType.ReservationCancelled))
                .Select(t => new { PrescriptionId = t.PrescriptionId!.Value, t.MedicineId })
                .Distinct()
                .ToListAsync(ct);
            var medicineIds = reservations.Select(r => r.MedicineId).Distinct().ToList();
            var medicines = await db.Medicines.Where(m => medicineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
            var allocator = new MedicineBatchAllocator(db, clock);
            foreach (var reservation in reservations)
                await allocator.Return(medicines[reservation.MedicineId], reservation.PrescriptionId, reason, actor, ct);
        }

        foreach (var prescription in stalePrescriptions)
        {
            prescription.Status = PrescriptionStatus.Expired;
            var previousNotes = prescription.Notes?.Trim();
            prescription.Notes = string.IsNullOrEmpty(previousNotes) ? reason : $"{previousNotes}\n{reason}";
            AddAudit(db, clock, nameof(Prescription), prescription.Id, BusinessDisplay.PrescriptionCode(prescription, clock), reason);
        }

        var staleVisits = await db.PatientVisits
            .Include(v => v.Appointment)
            .Where(v => v.Status != VisitStatus.Completed &&
                v.Status != VisitStatus.Cancelled &&
                v.Status != VisitStatus.NoShow &&
                v.CheckedInAtUtc <= cutoffUtc)
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

        foreach (var visit in staleVisits)
        {
            visit.Status = VisitStatus.Cancelled;
            visit.CancelledAtUtc = clock.UtcNow;
            visit.UpdatedAtUtc = clock.UtcNow;
            visit.CancellationReason = reason;
            if (visit.Appointment is { } appointment &&
                appointment.Status != AppointmentStatus.Completed &&
                appointment.Status != AppointmentStatus.Cancelled &&
                appointment.Status != AppointmentStatus.NoShow)
            {
                var oldStatus = appointment.Status;
                appointment.Status = AppointmentStatus.Cancelled;
                db.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = appointment.Id,
                    OldStatus = oldStatus,
                    NewStatus = AppointmentStatus.Cancelled,
                    Action = AppointmentHistoryAction.Cancelled,
                    Note = reason,
                    PerformedByUserId = null,
                    CreatedAt = clock.UtcNow
                });
                var slot = await db.AppointmentSlots.FindAsync([appointment.AppointmentSlotId], ct);
                if (slot != null && !await db.Appointments.AnyAsync(a => a.Id != appointment.Id &&
                        a.AppointmentSlotId == slot.Id &&
                        AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status), ct))
                    slot.IsBooked = false;
                AddAudit(db, clock, nameof(Appointment), appointment.Id, appointment.AppointmentCode, reason);
            }
            AddAudit(db, clock, nameof(PatientVisit), visit.Id, visit.VisitCode, reason);
        }

        if (staleOrders.Count > 0 || stalePrescriptions.Count > 0 || staleVisits.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private static void AddAudit(AppDbContext db, IDateTimeProvider clock, string entity, long id, string code, string reason) =>
        db.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = null,
            Action = "AutomaticExpiry",
            EntityName = entity,
            EntityId = id.ToString(),
            Description = $"Hệ thống: {reason} — {code}",
            CreatedAt = clock.UtcNow
        });

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
