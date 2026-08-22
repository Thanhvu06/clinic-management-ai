using ClinicManagement.Application.Authentication.DTOs;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ICurrentUserService _currentUserService;

    public AuthController(IAuthenticationService authService, ICurrentUserService currentUserService)
    {
        _authService = authService;
        _currentUserService = currentUserService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        await _authService.RegisterPatientAsync(request);
        return Created("", ApiResponse.Ok("Đăng ký thành công"));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);
        return Ok(ApiResponse<AuthResponse>.Ok(response, "Đăng nhập thành công"));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            return Unauthorized(new ApiErrorResponse { ErrorCode = "UNAUTHORIZED", Message = "Chưa đăng nhập" });

        var user = await _authService.GetCurrentUserAsync(userId.Value);
        return Ok(ApiResponse<UserDto>.Ok(user, "Thao tác thành công"));
    }

    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return Ok(ApiResponse.Ok("Thao tác thành công"));
    }
}
