using System;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Appointments.DTOs.ChangeRequests;

public class CreateRescheduleRequestDto
{
    [Required]
    public long RequestedSlotId { get; set; }
    
    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class CreateCancellationRequestDto
{
    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class ChangeRequestDto
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public long? RequestedSlotId { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid RequestedByUserId { get; set; }
    public Guid? ProcessedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public string? AppointmentCode { get; set; }
    public string? PatientName { get; set; }
    public string? DoctorName { get; set; }
    public string? SpecialtyName { get; set; }
    public DateOnly? CurrentSlotDate { get; set; }
    public TimeOnly? CurrentStartTime { get; set; }
    public TimeOnly? CurrentEndTime { get; set; }
    public DateOnly? RequestedSlotDate { get; set; }
    public TimeOnly? RequestedStartTime { get; set; }
    public TimeOnly? RequestedEndTime { get; set; }
}

public class ProcessChangeRequestDto
{
    public long? NewSlotId { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
