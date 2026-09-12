using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class AppointmentChangeRequest
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public AppointmentChangeRequestType RequestType { get; set; }
    public long? RequestedSlotId { get; set; }
    public string? Reason { get; set; }
    public AppointmentChangeRequestStatus Status { get; set; } = AppointmentChangeRequestStatus.Pending;
    public AppointmentStatus? OriginalAppointmentStatus { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? ProcessedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public AppointmentSlot? RequestedSlot { get; set; }
}
