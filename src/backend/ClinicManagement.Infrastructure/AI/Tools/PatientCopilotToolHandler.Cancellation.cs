using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Appointments;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed partial class PatientCopilotToolHandler
{
    private const string CancelToolName = "patient.prepare_cancel_appointment";

    private sealed record ChangeOutcome(string Reference, object Data, string DisplayText)
    {
        public static ChangeOutcome Request(ChangeRequestDto request, Guid actionId, string operation, long appointmentId) => new(
            request.Id.ToString(),
            new { actionId, changeRequestId = request.Id, operation, appointmentId, cancelled = false },
            operation == "reschedule" ? "Yêu cầu đổi lịch đã được tạo." : "Yêu cầu hủy lịch đã được tạo. Lịch đã được xác nhận nên cần lễ tân duyệt.");
    }

    // The status is re-read at execution time: a preview made while the appointment was
    // pending must still become a reception-reviewed request if staff confirmed it meanwhile.
    private async Task<ChangeOutcome> CancelAppointmentAsync(AppointmentDto current, string reason, Guid actionId)
    {
        if (IsPending(current))
        {
            try
            {
                var cancelled = await _changeRequests.CancelPendingAppointmentAsync(current.Id, new CreateCancellationRequestDto { Reason = reason });
                return new ChangeOutcome($"appointment:{cancelled.AppointmentId}",
                    new { actionId, operation = "cancel", appointmentId = cancelled.AppointmentId, appointmentCode = cancelled.AppointmentCode, cancelled = true },
                    $"Đã hủy lịch hẹn {cancelled.AppointmentCode}. Lịch đang Chờ xác nhận nên được hủy ngay và khung giờ đã được giải phóng.");
            }
            catch (ConflictException ex) when (ex.ErrorCode == ChangeRequestService.AppointmentNotPendingErrorCode)
            {
            }
        }
        var request = await _changeRequests.CreateCancellationRequestAsync(current.Id, new CreateCancellationRequestDto { Reason = reason }, actionId);
        return ChangeOutcome.Request(request, actionId, "cancel", current.Id);
    }

    private static bool IsPending(AppointmentDto appointment) =>
        string.Equals(appointment.Status, nameof(AppointmentStatus.Pending), StringComparison.OrdinalIgnoreCase);

    private static bool CancelsImmediately(AiPendingToolAction action, AppointmentDto appointment) =>
        action.ToolName.Equals(CancelToolName, StringComparison.OrdinalIgnoreCase) && IsPending(appointment);

    private static string PendingDisplayText(AiPendingToolAction action, AppointmentDto appointment)
    {
        if (!action.ToolName.Equals(CancelToolName, StringComparison.OrdinalIgnoreCase))
            return $"Thao tác trên lịch hẹn {appointment.AppointmentCode} đang chờ bạn xác nhận.";
        return IsPending(appointment)
            ? $"Thao tác trên lịch hẹn {appointment.AppointmentCode} đang chờ bạn xác nhận. Lịch đang Chờ xác nhận nên sẽ được hủy ngay sau khi bạn xác nhận."
            : $"Thao tác trên lịch hẹn {appointment.AppointmentCode} đang chờ bạn xác nhận. Lịch đã được xác nhận nên yêu cầu hủy sẽ được gửi lễ tân duyệt.";
    }
}
