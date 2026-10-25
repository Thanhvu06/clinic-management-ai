using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed partial class PatientCopilotToolHandler
{
    private async Task<AiToolExecutionResult> GetUpcomingAppointmentsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var patientId = await ResolvePatientIdAsync(_currentUser.UserId, cancellationToken);
        if (!patientId.HasValue) return AiToolExecutionResult.Failed("PATIENT_SCOPE_REQUIRED", "Không xác định được tài khoản bệnh nhân hiện tại.");
        var query = _db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == patientId.Value && a.AppointmentDate >= _dateTimeProvider.VietnamToday &&
                (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed ||
                 a.Status == AppointmentStatus.PendingReschedule || a.Status == AppointmentStatus.PendingCancellation));
        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime,
                status = a.Status.ToString(), doctorName = _db.Users.Where(u => u.Id == a.Doctor.UserId).Select(u => u.FullName).FirstOrDefault(),
                specialtyName = a.Specialty.Name, facilityName = a.Facility == null ? null : a.Facility.Name, a.Reason
            }).ToListAsync(cancellationToken);
        return Completed(new { items, totalItems, page, pageSize }, "appointments",
            totalItems == 0 ? "Bạn hiện chưa có lịch hẹn sắp tới." : $"Tìm thấy {totalItems} lịch hẹn sắp tới của bạn.");
    }
}
