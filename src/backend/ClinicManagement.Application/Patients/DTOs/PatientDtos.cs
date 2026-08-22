using ClinicManagement.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Patients.DTOs;

public class PatientProfileDto
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? Address { get; set; }
}

public class UpdatePatientProfileRequest
{
    [Required(ErrorMessage = "FullName là bắt buộc.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "PhoneNumber là bắt buộc.")]
    public string PhoneNumber { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? Address { get; set; }
}
