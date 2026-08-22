using System;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Application.Appointments.DTOs;

namespace ClinicManagement.Application.Appointments.DTOs.Doctor;

public class DoctorAppointmentDto : AppointmentDto
{
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public DateOnly? PatientDob { get; set; }
}

public class CompleteAppointmentDto
{
    [Required]
    public string Summary { get; set; } = string.Empty;
    public string? FollowUpInstruction { get; set; }
}

public class NoShowAppointmentDto
{
    public string? Reason { get; set; }
}

public class CreateRevisitRequestDto
{
    [Required]
    public DateOnly SuggestedDate { get; set; }
    public string? Note { get; set; }
}
