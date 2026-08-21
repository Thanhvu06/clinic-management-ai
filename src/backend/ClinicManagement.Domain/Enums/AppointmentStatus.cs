namespace ClinicManagement.Domain.Enums;

public enum AppointmentStatus
{
    Pending,
    Confirmed,
    PendingReschedule,
    PendingCancellation,
    Cancelled,
    Completed,
    NoShow
}
