using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DevelopmentPatientBookingSeed
{
    internal const string ExpiredPrescriptionAdvice = "Đơn thuốc đã hết hạn. Vui lòng tái khám để bác sĩ đánh giá và kê đơn phù hợp.";
    internal const string CancellationReason = "Đã hủy lịch khám quá hạn. Vui lòng chọn lịch khám mới hoặc liên hệ lễ tân.";
    private const string LegacyCleanupNote = "Dọn dữ liệu demo cũ";

    internal static async Task RepairAsync(AppDbContext db, IDateTimeProvider clock, CancellationToken ct = default)
    {
        var appointments = await db.Appointments.Include(a => a.Patient)
            .Where(a => a.Patient.Email == "patient@cliniccare.local" && a.Status == AppointmentStatus.NoShow).ToListAsync(ct);
        foreach (var appointment in appointments)
        {
            appointment.Status = AppointmentStatus.Cancelled;
            appointment.Histories.Add(new AppointmentHistory
            {
                OldStatus = AppointmentStatus.NoShow, NewStatus = AppointmentStatus.Cancelled,
                Action = AppointmentHistoryAction.Cancelled, Note = CancellationReason, CreatedAt = clock.UtcNow
            });
        }
        var visits = await db.PatientVisits.Where(v => v.Patient.Email == "patient@cliniccare.local" && v.Status == VisitStatus.NoShow).ToListAsync(ct);
        foreach (var visit in visits)
        {
            visit.Status = VisitStatus.Cancelled;
            visit.CancellationReason = CancellationReason;
            visit.CancelledAtUtc = visit.UpdatedAtUtc = clock.UtcNow;
        }
        foreach (var prescription in await db.Prescriptions.Where(p => p.Notes != null && p.Notes.Contains(LegacyCleanupNote)).ToListAsync(ct))
            prescription.Notes = prescription.Notes!.Replace(LegacyCleanupNote, ExpiredPrescriptionAdvice, StringComparison.Ordinal);
        foreach (var order in await db.DiagnosticOrders.Where(o => o.Note != null && o.Note.Contains(LegacyCleanupNote)).ToListAsync(ct))
            order.Note = order.Note!.Replace(LegacyCleanupNote, "Chỉ định đã quá hạn thực hiện. Vui lòng tái khám để được đánh giá lại.", StringComparison.Ordinal);
        foreach (var visit in await db.PatientVisits.Where(v => v.CancellationReason != null && v.CancellationReason.Contains(LegacyCleanupNote)).ToListAsync(ct))
            visit.CancellationReason = visit.CancellationReason!.Replace(LegacyCleanupNote, CancellationReason, StringComparison.Ordinal);
        foreach (var history in await db.AppointmentHistories.Where(h => h.Note != null && h.Note.Contains(LegacyCleanupNote)).ToListAsync(ct))
            history.Note = history.Note!.Replace(LegacyCleanupNote, CancellationReason, StringComparison.Ordinal);
        await db.SaveChangesAsync(ct);
    }
}
