namespace ClinicManagement.Domain.Enums;

public enum AppointmentStatus
{
    Pending = 0,
    Confirmed = 1,
    PendingReschedule = 2,
    PendingCancellation = 3,
    Cancelled = 4,
    Completed = 5,
    NoShow = 6,
    CheckedIn = 7,
    InConsultation = 8
}
