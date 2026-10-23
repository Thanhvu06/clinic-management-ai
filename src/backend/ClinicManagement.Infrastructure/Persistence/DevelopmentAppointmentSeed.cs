using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DevelopmentAppointmentSeed
{
    private const string SeedAction = "InitializeAppointments";
    private const string SeedEntity = "DevelopmentData";

    private static Task<bool> IsInitializedAsync(AppDbContext db) => db.SystemAuditLogs
        .AnyAsync(log => log.Action == SeedAction && log.EntityName == SeedEntity && log.EntityId == "appointments-v1");

    private static void MarkInitialized(AppDbContext db, IDateTimeProvider clock, Guid actor) => db.SystemAuditLogs.Add(new SystemAuditLog
    {
        UserId = actor, Action = SeedAction, EntityName = SeedEntity, EntityId = "appointments-v1",
        Description = "Khởi tạo dữ liệu lịch hẹn", CreatedAt = clock.UtcNow
    });

    private static async Task<string> GenerateCodeAsync(AppDbContext db, IDateTimeProvider clock)
    {
        string code;
        do
        {
            code = $"APT-{clock.VietnamNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        } while (db.Appointments.Local.Any(a => a.AppointmentCode == code) || await db.Appointments.AnyAsync(a => a.AppointmentCode == code));
        return code;
    }

    internal static async Task NormalizeLegacyCodesAsync(AppDbContext db, IDateTimeProvider clock, Guid actor)
    {
        var legacy = await db.Appointments.Where(a => a.AppointmentCode.StartsWith("DEMO-")).ToListAsync();
        if (legacy.Count == 0) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var appointment in legacy)
        {
            var previous = appointment.AppointmentCode;
            var replacement = await GenerateCodeAsync(db, clock);
            appointment.AppointmentCode = replacement;
            appointment.Reason = appointment.Reason?.Replace(previous, replacement, StringComparison.Ordinal);
            foreach (var history in await db.AppointmentHistories.Where(h => h.AppointmentId == appointment.Id && h.Note != null && h.Note.Contains(previous)).ToListAsync())
                history.Note = history.Note!.Replace(previous, replacement, StringComparison.Ordinal);
            foreach (var notification in await db.Notifications.Where(n => n.Message.Contains(previous) || n.Title.Contains(previous)).ToListAsync())
            {
                notification.Message = notification.Message.Replace(previous, replacement, StringComparison.Ordinal);
                notification.Title = notification.Title.Replace(previous, replacement, StringComparison.Ordinal);
            }
            foreach (var log in await db.SystemAuditLogs.Where(log => log.Description.Contains(previous)).ToListAsync())
                log.Description = log.Description.Replace(previous, replacement, StringComparison.Ordinal);
            foreach (var visit in await db.PatientVisits.Where(v => v.AppointmentId == appointment.Id).ToListAsync())
            {
                visit.VisitCode = visit.VisitCode.Replace(previous, replacement, StringComparison.Ordinal);
                visit.ChiefComplaint = visit.ChiefComplaint?.Replace(previous, replacement, StringComparison.Ordinal);
            }
            foreach (var invoice in await db.Invoices.Where(i => i.AppointmentId == appointment.Id || (i.PatientVisit != null && i.PatientVisit.AppointmentId == appointment.Id)).Include(i => i.Items).ToListAsync())
            {
                invoice.InvoiceCode = invoice.InvoiceCode.Replace(previous, replacement, StringComparison.Ordinal);
                invoice.CancellationReason = invoice.CancellationReason?.Replace(previous, replacement, StringComparison.Ordinal);
                foreach (var item in invoice.Items)
                    item.Description = item.Description.Replace(previous, replacement, StringComparison.Ordinal);
            }
        }
        if (!await IsInitializedAsync(db)) MarkInitialized(db, clock, actor);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    internal static async Task SeedAsync(
        AppDbContext db,
        List<Doctor> doctors,
        List<ApplicationUser> docUsers,
        ApplicationUser adminId1,
        List<Patient> patients,
        List<Specialty> specs,
        ClinicManagement.Application.Common.Interfaces.IDateTimeProvider dateTimeProvider,
        ILogger logger)
    {
        if (await IsInitializedAsync(db))
        {
            logger.LogInformation("Demo appointments already exist. Skipping.");
            return;
        }

        var today = dateTimeProvider.VietnamToday;
        var slots = await db.AppointmentSlots.ToListAsync();
        if (slots.Count == 0)
        {
            logger.LogWarning("No appointment slots found to link demo appointments.");
            return;
        }

        var mainDoc = doctors.FirstOrDefault(d => d.UserId == docUsers[0].Id) ?? doctors[0];
        var mainDocSpecId = mainDoc.DoctorSpecialties.FirstOrDefault(ds => ds.IsPrimary)?.SpecialtyId ?? specs[0].Id;
        var initialized = false;

        async Task CreateApt(Patient p, Doctor d, AppointmentSlot s, AppointmentStatus status, string reason)
        {
            if (s.DoctorId != d.Id)
            {
                throw new InvalidOperationException($"Appointment slot doctor {s.DoctorId} must match appointment doctor {d.Id}");
            }

            var docSpecId = d.DoctorSpecialties.FirstOrDefault(ds => ds.IsPrimary)?.SpecialtyId
                ?? d.DoctorSpecialties.FirstOrDefault()?.SpecialtyId
                ?? specs[0].Id;
            var code = await GenerateCodeAsync(db, dateTimeProvider);
            var a = new Appointment
            {
                AppointmentCode = code,
                PatientId = p.Id,
                DoctorId = d.Id,
                SpecialtyId = docSpecId,
                AppointmentSlotId = s.Id,
                AppointmentDate = s.SlotDate,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Reason = reason,
                Status = status
            };
            if (!initialized)
            {
                MarkInitialized(db, dateTimeProvider, adminId1.Id);
                initialized = true;
            }
            db.Appointments.Add(a);
            if (status != AppointmentStatus.Cancelled)
            {
                s.IsBooked = true;
            }
            await db.SaveChangesAsync();

            db.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = a.Id,
                Action = AppointmentHistoryAction.Created,
                NewStatus = AppointmentStatus.Pending,
                Note = "Bệnh nhân tự đặt lịch hẹn qua ứng dụng",
                PerformedByUserId = p.UserId ?? Guid.Empty,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            });

            if (status != AppointmentStatus.Pending && status != AppointmentStatus.Cancelled)
            {
                db.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = a.Id,
                    Action = AppointmentHistoryAction.Confirmed,
                    OldStatus = AppointmentStatus.Pending,
                    NewStatus = AppointmentStatus.Confirmed,
                    Note = "Lễ tân liên hệ và xác nhận lịch hẹn",
                    PerformedByUserId = adminId1.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-9)
                });
            }

            if (status == AppointmentStatus.Completed)
            {
                db.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = a.Id,
                    Action = AppointmentHistoryAction.Completed,
                    OldStatus = AppointmentStatus.Confirmed,
                    NewStatus = AppointmentStatus.Completed,
                    Note = "Bác sĩ hoàn thành phiên khám",
                    PerformedByUserId = d.UserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-1)
                });
                db.VisitSummaries.Add(new VisitSummary
                {
                    AppointmentId = a.Id,
                    DoctorId = d.Id,
                    Summary = "Sức khỏe bệnh nhân tương đối ổn định. Đã kê đơn thuốc và tư vấn chế độ ăn uống, sinh hoạt lành mạnh.",
                    FollowUpInstruction = "Uống nhiều nước ấm, tập thể dục nhẹ nhàng 30 phút mỗi ngày. Tái khám sau 2 tuần nếu triệu chứng tái phát."
                });
            }
            else if (status == AppointmentStatus.NoShow)
            {
                db.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = a.Id,
                    Action = AppointmentHistoryAction.NoShow,
                    OldStatus = AppointmentStatus.Confirmed,
                    NewStatus = AppointmentStatus.NoShow,
                    Note = "Bệnh nhân không có mặt tại phòng khám vào giờ hẹn",
                    PerformedByUserId = d.UserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-1)
                });
            }
            else if (status == AppointmentStatus.Cancelled)
            {
                db.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = a.Id,
                    Action = AppointmentHistoryAction.Cancelled,
                    OldStatus = AppointmentStatus.Pending,
                    NewStatus = AppointmentStatus.Cancelled,
                    Note = "Bệnh nhân thông báo bận việc đột xuất xin hủy lịch",
                    PerformedByUserId = p.UserId ?? Guid.Empty,
                    CreatedAt = DateTime.UtcNow.AddDays(-2)
                });
            }
            await db.SaveChangesAsync();
        }

        var futureSlots = slots.Where(x => x.SlotDate > today).ToList();

        var pastDate = today.AddDays(-5);
        var pastSchedule = await db.DoctorWorkSchedules.FirstOrDefaultAsync(s => s.DoctorId == mainDoc.Id && s.WorkDate == pastDate);
        if (pastSchedule == null)
        {
            pastSchedule = new DoctorWorkSchedule
            {
                DoctorId = mainDoc.Id,
                WorkDate = pastDate,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0),
                IsActive = true
            };
            db.DoctorWorkSchedules.Add(pastSchedule);
            await db.SaveChangesAsync();
        }

        var pastSlots = await db.AppointmentSlots.Where(s => s.DoctorId == mainDoc.Id && s.SlotDate == pastDate).ToListAsync();
        if (pastSlots.Count < 15)
        {
            for (int i = pastSlots.Count; i < 15; i++)
            {
                var sTime = new TimeOnly(8, 0).AddMinutes(i * 30);
                if (!pastSlots.Any(x => x.StartTime == sTime))
                {
                    var s = new AppointmentSlot
                    {
                        DoctorId = mainDoc.Id,
                        SlotDate = pastDate,
                        StartTime = sTime,
                        EndTime = sTime.AddMinutes(30),
                        IsBooked = true
                    };
                    pastSlots.Add(s);
                    db.AppointmentSlots.Add(s);
                }
            }
            await db.SaveChangesAsync();
        }

        for (int i = 0; i < 12; i++)
        {
            await CreateApt(patients[i % patients.Count], mainDoc, pastSlots[i % pastSlots.Count], AppointmentStatus.Completed, "Khám kiểm tra sức khỏe tổng quát định kỳ");
        }
        for (int i = 12; i < 15; i++)
        {
            await CreateApt(patients[i % patients.Count], mainDoc, pastSlots[i % pastSlots.Count], AppointmentStatus.NoShow, "Tư vấn sức khỏe định kỳ");
        }

        var pastDate2 = today.AddDays(-4);
        var pastSchedule2 = await db.DoctorWorkSchedules.FirstOrDefaultAsync(s => s.DoctorId == mainDoc.Id && s.WorkDate == pastDate2);
        if (pastSchedule2 == null)
        {
            pastSchedule2 = new DoctorWorkSchedule
            {
                DoctorId = mainDoc.Id,
                WorkDate = pastDate2,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0),
                IsActive = true
            };
            db.DoctorWorkSchedules.Add(pastSchedule2);
            await db.SaveChangesAsync();
        }

        var pastSlots2 = await db.AppointmentSlots.Where(s => s.DoctorId == mainDoc.Id && s.SlotDate == pastDate2).ToListAsync();
        if (pastSlots2.Count < 5)
        {
            for (int i = pastSlots2.Count; i < 5; i++)
            {
                var sTime = new TimeOnly(8, 0).AddMinutes(i * 30);
                if (!pastSlots2.Any(x => x.StartTime == sTime))
                {
                    var s = new AppointmentSlot
                    {
                        DoctorId = mainDoc.Id,
                        SlotDate = pastDate2,
                        StartTime = sTime,
                        EndTime = sTime.AddMinutes(30),
                        IsBooked = false
                    };
                    pastSlots2.Add(s);
                    db.AppointmentSlots.Add(s);
                }
            }
            await db.SaveChangesAsync();
        }

        for (int i = 0; i < 5; i++)
        {
            await CreateApt(patients[i % patients.Count], mainDoc, pastSlots2[i % pastSlots2.Count], AppointmentStatus.Cancelled, "Tái khám theo hẹn của bác sĩ");
        }

        var todaySlots = await db.AppointmentSlots
            .Where(s => s.DoctorId == mainDoc.Id && s.SlotDate == today && !s.IsBooked)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        if (todaySlots.Count >= 3)
        {
            await CreateApt(patients[0], mainDoc, todaySlots[0], AppointmentStatus.Confirmed, "Kiểm tra định kỳ huyết áp và đường huyết sáng nay");
            await CreateApt(patients[1], mainDoc, todaySlots[1], AppointmentStatus.Confirmed, "Tái khám viêm họng hạt và sốt nhẹ");
            await CreateApt(patients[2], mainDoc, todaySlots[2], AppointmentStatus.Confirmed, "Tư vấn dinh dưỡng và chăm sóc sức khỏe");
        }

        for (int i = 0; i < 6; i++)
        {
            var doc = doctors[i % doctors.Count];
            var docSlot = futureSlots.FirstOrDefault(s => s.DoctorId == doc.Id && !s.IsBooked);
            if (docSlot != null)
            {
                await CreateApt(patients[i % patients.Count], doc, docSlot, AppointmentStatus.Pending, "Đau đầu kéo dài kèm mệt mỏi nhẹ");
            }
        }
        for (int i = 6; i < 16; i++)
        {
            var doc = doctors[(i + 1) % doctors.Count];
            var docSlot = futureSlots.FirstOrDefault(s => s.DoctorId == doc.Id && !s.IsBooked);
            if (docSlot != null)
            {
                await CreateApt(patients[i % patients.Count], doc, docSlot, AppointmentStatus.Confirmed, "Kiểm tra huyết áp và tư vấn lối sống");
            }
        }

        logger.LogInformation("Appointments seeded.");

        if (!await db.AppointmentChangeRequests.AnyAsync())
        {
            var pendingAppt = await db.Appointments.Include(a => a.Patient).FirstOrDefaultAsync(a => a.Status == AppointmentStatus.Pending);
            if (pendingAppt != null)
            {
                db.AppointmentChangeRequests.Add(new AppointmentChangeRequest
                {
                    AppointmentId = pendingAppt.Id,
                    RequestType = AppointmentChangeRequestType.Cancellation,
                    Reason = "Bận chuyến công tác đột xuất tại Hà Nội",
                    Status = AppointmentChangeRequestStatus.Pending,
                    RequestedByUserId = pendingAppt.Patient.UserId ?? Guid.Empty,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var confAppt = await db.Appointments.Include(a => a.Patient).Skip(1).FirstOrDefaultAsync(a => a.Status == AppointmentStatus.Confirmed);
            if (confAppt != null)
            {
                var rescheduleSlot = futureSlots.FirstOrDefault(s => s.DoctorId == confAppt.DoctorId && !s.IsBooked);
                db.AppointmentChangeRequests.Add(new AppointmentChangeRequest
                {
                    AppointmentId = confAppt.Id,
                    RequestType = AppointmentChangeRequestType.Reschedule,
                    RequestedSlotId = rescheduleSlot?.Id ?? confAppt.AppointmentSlotId,
                    Reason = "Xin dời ngày khám sang tuần sau vì việc gia đình",
                    Status = AppointmentChangeRequestStatus.Pending,
                    RequestedByUserId = confAppt.Patient.UserId ?? Guid.Empty,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        if (!await db.RevisitRequests.AnyAsync())
        {
            var compAppt = await db.Appointments.FirstOrDefaultAsync(a => a.Status == AppointmentStatus.Completed);
            if (compAppt != null)
            {
                db.RevisitRequests.Add(new RevisitRequest
                {
                    AppointmentId = compAppt.Id,
                    PatientId = compAppt.PatientId,
                    DoctorId = compAppt.DoctorId,
                    SuggestedDate = today.AddDays(7),
                    Note = "Tái khám sau 1 tuần để kiểm tra lại đường huyết và men gan",
                    Status = RevisitRequestStatus.PendingPatientResponse
                });
                await db.SaveChangesAsync();
            }
        }

        if (!await db.DoctorLeaveRequests.AnyAsync())
        {
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest
            {
                DoctorId = mainDoc.Id,
                StartDateTime = DateTime.UtcNow.AddDays(2),
                EndDateTime = DateTime.UtcNow.AddDays(3),
                Reason = "Nghỉ phép cá nhân giải quyết việc gia đình",
                Status = DoctorLeaveRequestStatus.Pending
            });
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest
            {
                DoctorId = doctors[1].Id,
                StartDateTime = DateTime.UtcNow.AddDays(5),
                EndDateTime = DateTime.UtcNow.AddDays(6),
                Reason = "Tham gia hội thảo chuyên ngành tại Hà Nội",
                Status = DoctorLeaveRequestStatus.Approved,
                AdminNote = "Đã duyệt, yêu cầu chuyển giao ca trực và hỗ trợ bệnh nhân ngày 5."
            });
            await db.SaveChangesAsync();
        }
        logger.LogInformation("Requests and Leaves seeded.");
    }

}
