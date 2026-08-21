using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class AppointmentHistory
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public AppointmentHistoryAction Action { get; set; }
    public AppointmentStatus? OldStatus { get; set; }
    public AppointmentStatus? NewStatus { get; set; }
    public string? Note { get; set; }
    public Guid PerformedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Appointment Appointment { get; set; } = null!;
}
