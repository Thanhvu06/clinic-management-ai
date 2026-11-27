using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Common;

public static class UserActionAudit
{
    public const string CreateAppointment = "CreateAppointment";
    public const string ConfirmAppointment = "ConfirmAppointment";
    public const string CheckInPatient = "CheckInPatient";
    public const string RequestAppointmentCancellation = "RequestAppointmentCancellation";
    public const string CancelAppointment = "CancelAppointment";
    public const string RequestAppointmentReschedule = "RequestAppointmentReschedule";
    public const string RescheduleAppointment = "RescheduleAppointment";
    public const string RejectAppointmentChangeRequest = "RejectAppointmentChangeRequest";
    public const string StartConsultation = "StartConsultation";
    public const string CreateLeaveRequest = "CreateLeaveRequest";
    public const string WithdrawLeaveRequest = "WithdrawLeaveRequest";
    public const string ApproveLeaveRequest = "ApproveLeaveRequest";
    public const string RejectLeaveRequest = "RejectLeaveRequest";

    public static void Record(AppDbContext db, Guid? userId, string action, string entityName, long entityId, string description, DateTime utcNow) =>
        db.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            Description = description,
            CreatedAt = utcNow
        });

    public static void Appointment(AppDbContext db, Guid? userId, string action, Appointment appointment, string description, DateTime utcNow) =>
        Record(db, userId, action, nameof(Domain.Entities.Appointment), appointment.Id, $"{description} {appointment.AppointmentCode}", utcNow);
}
