using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Authentication.DTOs;

public class LoginRequest
{
    [Required]
    public string EmailOrPhone { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
