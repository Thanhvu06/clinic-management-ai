using System;
using System.Collections.Generic;

namespace ClinicManagement.Domain.Enums;

public static class AppointmentStatusExtensions
{
    public static readonly AppointmentStatus[] HoldingSlotStatuses = new[]
    {
        AppointmentStatus.Pending,
        AppointmentStatus.Confirmed,
        AppointmentStatus.PendingReschedule,
        AppointmentStatus.PendingCancellation,
        AppointmentStatus.CheckedIn,
        AppointmentStatus.InConsultation
    };

    public static readonly AppointmentStatus[] ReleasedSlotStatuses = new[]
    {
        AppointmentStatus.Cancelled,
        AppointmentStatus.Completed,
        AppointmentStatus.NoShow
    };

    public static bool HoldsSlot(this AppointmentStatus status)
    {
        return status switch
        {
            AppointmentStatus.Pending => true,
            AppointmentStatus.Confirmed => true,
            AppointmentStatus.PendingReschedule => true,
            AppointmentStatus.PendingCancellation => true,
            AppointmentStatus.CheckedIn => true,
            AppointmentStatus.InConsultation => true,
            AppointmentStatus.Cancelled => false,
            AppointmentStatus.Completed => false,
            AppointmentStatus.NoShow => false,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Trạng thái lịch hẹn không hợp lệ.")
        };
    }
}
