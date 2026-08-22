using System;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Leaves.DTOs;

public class LeaveRequestDto
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AdminNote { get; set; }
}

public class CreateLeaveRequestDto
{
    [Required]
    public DateTime StartDateTime { get; set; }
    
    [Required]
    public DateTime EndDateTime { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class AdminProcessLeaveRequestDto
{
    public string? AdminNote { get; set; }
}
