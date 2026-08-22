using System;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Appointments.DTOs;

public class CreateAppointmentRequest
{
    [Required]
    public long DoctorId { get; set; }
    [Required]
    public long SpecialtyId { get; set; }
    [Required]
    public long AppointmentSlotId { get; set; }
    
    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class AppointmentDto
{
    public long Id { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public long SpecialtyId { get; set; }
    public string SpecialtyName { get; set; } = string.Empty;
    public long AppointmentSlotId { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class AppointmentHistoryDto
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? Note { get; set; }
    public Guid PerformedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
