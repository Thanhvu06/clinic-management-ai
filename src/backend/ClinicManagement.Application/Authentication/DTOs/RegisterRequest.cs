using ClinicManagement.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Authentication.DTOs;

public class RegisterRequest
{
    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? Address { get; set; }
}
