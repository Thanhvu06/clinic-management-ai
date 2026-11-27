using System;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Appointments;

public partial class ChangeRequestService
{
    public const string AppointmentNotPendingErrorCode = "APPOINTMENT_NOT_PENDING";

    public async Task<PatientCancellationResultDto> CancelPendingAppointmentAsync(long appointmentId, CreateCancellationRequestDto request)
    {
        var userId = GetUserId();
        var patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 5)
            throw new ValidationException("Reason", "Lý do hủy lịch phải có ít nhất 5 ký tự.");
        if (reason.Length > 500)
            throw new ValidationException("Reason", "Lý do hủy lịch không được vượt quá 500 ký tự.");

        return await ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var appointment = await _dbContext.Appointments
                    .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientId == patient.Id)
                    ?? throw new NotFoundException("Lịch hẹn không tồn tại.");

                // The stored status decides the path; a client asking to cancel a confirmed appointment
                // immediately must go through the reception-reviewed request instead.
                if (appointment.Status != AppointmentStatus.Pending)
                    throw NotPending(appointment.Status);
                if (appointment.AppointmentDate.ToDateTime(appointment.StartTime) <= _dateTimeProvider.VietnamNow)
                    throw new BusinessException("APPOINTMENT_STARTED", "Lịch hẹn đã tới giờ bắt đầu nên không thể hủy trực tuyến. Vui lòng liên hệ lễ tân.");

                var affected = await _dbContext.Appointments
                    .Where(a => a.Id == appointmentId && a.PatientId == patient.Id && a.Status == AppointmentStatus.Pending)
                    .ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, AppointmentStatus.Cancelled));
                if (affected == 0)
                    throw NotPending(null);

                await _dbContext.AppointmentSlots
                    .Where(s => s.Id == appointment.AppointmentSlotId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, false));

                var now = _dateTimeProvider.UtcNow;
                _dbContext.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = appointment.Id,
                    Action = AppointmentHistoryAction.Cancelled,
                    OldStatus = AppointmentStatus.Pending,
                    NewStatus = AppointmentStatus.Cancelled,
                    Note = $"Bệnh nhân hủy lịch chờ xác nhận: {reason}",
                    PerformedByUserId = userId,
                    CreatedAt = now
                });
                UserActionAudit.Appointment(_dbContext, userId, UserActionAudit.CancelAppointment, appointment, "Bệnh nhân hủy ngay lịch hẹn chờ xác nhận", now);

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                return new PatientCancellationResultDto { AppointmentId = appointment.Id, AppointmentCode = appointment.AppointmentCode };
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { }
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }, AppointmentNotPendingErrorCode, "Lịch hẹn vừa thay đổi trạng thái. Vui lòng tải lại và thử lại.");
    }

    private static ConflictException NotPending(AppointmentStatus? status) => new(AppointmentNotPendingErrorCode,
        status == AppointmentStatus.Confirmed
            ? "Lịch hẹn đã được xác nhận nên không thể hủy ngay. Vui lòng gửi yêu cầu hủy để lễ tân duyệt."
            : "Lịch hẹn không còn ở trạng thái Chờ xác nhận nên không thể hủy ngay.");

    private static string RejectAuditDescription(AppointmentChangeRequestType type, string note) =>
        $"Từ chối yêu cầu {(type == AppointmentChangeRequestType.Cancellation ? "hủy" : "đổi")} lịch (ghi chú: {note}) cho lịch hẹn";
}
