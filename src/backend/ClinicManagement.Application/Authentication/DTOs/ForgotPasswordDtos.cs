using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Authentication.DTOs;

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public sealed class ForgotPasswordResponse
{
    public string? ResetToken { get; init; }
}

public class ResetPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ 8 ký tự trở lên")]
    public string NewPassword { get; set; } = string.Empty;
}
