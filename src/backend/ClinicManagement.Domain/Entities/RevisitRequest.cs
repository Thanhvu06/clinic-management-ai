using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class RevisitRequest
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public DateOnly SuggestedDate { get; set; }
    public string? Note { get; set; }
    public RevisitRequestStatus Status { get; set; } = RevisitRequestStatus.PendingPatientResponse;
    public long? NewAppointmentId { get; set; }

    public Appointment OriginalAppointment { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
    public Doctor Doctor { get; set; } = null!;
    public Appointment? NewAppointment { get; set; }
}
