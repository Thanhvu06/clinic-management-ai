using ClinicManagement.Application.Authentication.DTOs;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ClinicManagement.Infrastructure.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public AuthenticationService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext dbContext,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.EmailOrPhone) 
                   ?? await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.EmailOrPhone);

        if (user == null || !user.IsActive)
            throw new UnauthorizedException("Email/Số điện thoại hoặc mật khẩu không chính xác.");

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);

        if (!result.Succeeded)
            throw new UnauthorizedException("Email/Số điện thoại hoặc mật khẩu không chính xác.");

        var roles = await _userManager.GetRolesAsync(user);
        
        long? profileId = null;
        if (roles.Contains(RoleNames.Patient))
        {
            var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id);
            profileId = patient?.Id;
        }
        else if (roles.Contains(RoleNames.Doctor))
        {
            var doctor = await _dbContext.Doctors.FirstOrDefaultAsync(d => d.UserId == user.Id);
            profileId = doctor?.Id;
        }

        var tokenInfo = GenerateJwtToken(user, roles);

        return new AuthResponse
        {
            AccessToken = tokenInfo.Token,
            ExpiresAt = tokenInfo.ExpiresAt,
            User = new UserDto
            {
                Id = profileId,
                UserId = user.Id,
                FullName = user.FullName,
                Role = roles.FirstOrDefault() ?? string.Empty
            }
        };
    }

    public async Task RegisterPatientAsync(RegisterRequest request)
    {
        var existingEmail = await _userManager.FindByEmailAsync(request.Email);
        if (existingEmail != null)
            throw new ValidationException("Email", "Email đã được sử dụng.");

        var existingPhone = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);
        if (existingPhone != null)
            throw new ValidationException("PhoneNumber", "Số điện thoại đã được sử dụng.");

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                FullName = request.FullName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var errorMessages = result.Errors.Select(e => e.Code switch
                {
                    "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải chứa ít nhất một ký tự đặc biệt.",
                    "PasswordRequiresDigit" => "Mật khẩu phải chứa ít nhất một chữ số.",
                    "PasswordRequiresLower" => "Mật khẩu phải chứa ít nhất một chữ cái thường.",
                    "PasswordRequiresUpper" => "Mật khẩu phải chứa ít nhất một chữ cái hoa.",
                    "PasswordTooShort" => "Mật khẩu quá ngắn.",
                    _ => e.Description
                });
                var errors = string.Join(" ", errorMessages);
                throw new ValidationException("Password", errors);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, RoleNames.Patient);
            if (!roleResult.Succeeded)
                throw new Exception("Lỗi cấp quyền Patient.");

            var patient = new Patient
            {
                UserId = user.Id,
                DateOfBirth = request.DateOfBirth,
                Gender = request.Gender,
                Address = request.Address
            };

            _dbContext.Patients.Add(patient);
            await _dbContext.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<UserDto> GetCurrentUserAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null || !user.IsActive)
            throw new UnauthorizedException("Tài khoản không tồn tại hoặc đã bị khóa.");

        var roles = await _userManager.GetRolesAsync(user);
        
        long? profileId = null;
        if (roles.Contains(RoleNames.Patient))
        {
            var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id);
            profileId = patient?.Id;
        }
        else if (roles.Contains(RoleNames.Doctor))
        {
            var doctor = await _dbContext.Doctors.FirstOrDefaultAsync(d => d.UserId == user.Id);
            profileId = doctor?.Id;
        }

        return new UserDto
        {
            Id = profileId,
            UserId = user.Id,
            FullName = user.FullName,
            Role = roles.FirstOrDefault() ?? string.Empty
        };
    }

    private (string Token, DateTime ExpiresAt) GenerateJwtToken(ApplicationUser user, IList<string> roles)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = Convert.ToDouble(_configuration["Jwt:ExpiryMinutes"]);
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim("name", user.FullName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private bool IsDevelopmentOrDemo()
    {
        if (_environment.IsProduction())
        {
            return false;
        }

        return _environment.IsDevelopment()
            || _environment.IsEnvironment("Demo")
            || _environment.IsEnvironment("Testing");
    }

    public async Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || !user.IsActive)
        {
            return new ForgotPasswordResponse { ResetToken = null };
        }

        var isDevOrDemo = IsDevelopmentOrDemo();
        string? token = null;
        if (isDevOrDemo)
        {
            token = await _userManager.GeneratePasswordResetTokenAsync(user);
        }

        return new ForgotPasswordResponse
        {
            ResetToken = token
        };
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || !user.IsActive)
            throw new NotFoundException("Tài khoản không tồn tại hoặc đã bị vô hiệu hóa.");

        var token = request.Token?.Trim() ?? string.Empty;
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BusinessException("RESET_PASSWORD_FAILED", $"Đặt lại mật khẩu không thành công: {errors}");
        }
    }
}
