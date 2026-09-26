using System;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Appointments.DTOs.Revisit;

public class RevisitRequestDto
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public long SpecialtyId { get; set; }
    public DateOnly SuggestedDate { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? NewAppointmentId { get; set; }
    
    // For display
    public string DoctorName { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
}

public class AcceptRevisitRequestDto
{
    [Required]
    public long TargetSlotId { get; set; }
    public string? Reason { get; set; }
}

public class RejectRevisitRequestDto
{
    public string? Reason { get; set; }
}
