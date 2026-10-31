using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Appointments;

public partial class DoctorAppointmentService
{
    private async Task<HashSet<long>> FindIssuedPrescriptionIdsAsync(long? appointmentId, long? visitId) =>
        (await _dbContext.Prescriptions.Where(p =>
            (visitId.HasValue && p.PatientVisitId == visitId || appointmentId.HasValue && p.AppointmentId == appointmentId)
            && p.Status == PrescriptionStatus.Issued).Select(p => p.Id).ToListAsync()).ToHashSet();

    private async Task RecordCompletionAuditAsync(Guid actor, long? appointmentId, long? visitId, HashSet<long> issuedBefore)
    {
        var code = visitId.HasValue
            ? await _dbContext.PatientVisits.Where(v => v.Id == visitId).Select(v => v.VisitCode).SingleAsync()
            : await _dbContext.Appointments.Where(a => a.Id == appointmentId).Select(a => a.AppointmentCode).SingleAsync();
        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = actor, Action = "COMPLETE_CONSULTATION",
            EntityName = visitId.HasValue ? "PatientVisit" : "Appointment",
            EntityId = (visitId ?? appointmentId)!.Value.ToString(),
            Description = $"Bác sĩ hoàn tất khám {(visitId.HasValue ? "lượt" : "lịch hẹn")} {code}", CreatedAt = _dateTimeProvider.UtcNow
        });
        var prescriptions = await _dbContext.Prescriptions.Where(p =>
            (visitId.HasValue && p.PatientVisitId == visitId || appointmentId.HasValue && p.AppointmentId == appointmentId)).ToListAsync();
        foreach (var prescription in prescriptions.Where(p => p.Status == PrescriptionStatus.Issued && !issuedBefore.Contains(p.Id)))
            _dbContext.SystemAuditLogs.Add(new SystemAuditLog
            {
                UserId = actor, Action = "ISSUE_PRESCRIPTION", EntityName = "Prescription",
                EntityId = prescription.Id.ToString(), CreatedAt = _dateTimeProvider.UtcNow,
                Description = $"Bác sĩ ký đơn thuốc {BusinessDisplay.PrescriptionCode(prescription, _dateTimeProvider)}"
            });
    }
}
