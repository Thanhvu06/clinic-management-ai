using ClinicManagement.Application.Authentication.DTOs;

namespace ClinicManagement.Application.Authentication.Interfaces;

public interface IAuthenticationService
{
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task RegisterPatientAsync(RegisterRequest request);
    Task<UserDto> GetCurrentUserAsync(Guid userId);
    Task<string> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);
}
