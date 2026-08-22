using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Admin.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Admin;

public class AdminUserService : IAdminUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public AdminUserService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<PagedResult<UserDto>> GetUsersAsync(string? role, bool? isActive, string? search, int page, int pageSize)
    {
        var query = _userManager.Users.AsNoTracking();

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(u => u.FullName.Contains(search) 
                                  || u.Email!.Contains(search) 
                                  || u.PhoneNumber!.Contains(search));
        }
        
        // If filtering by role, it's a bit tricky with Identity. We'll join UserRoles
        if (!string.IsNullOrEmpty(role))
        {
            var roleEntity = await _roleManager.FindByNameAsync(role);
            if (roleEntity != null)
            {
                var roleId = roleEntity.Id;
                var userIdsInRole = _dbContext.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId);
                query = query.Where(u => userIdsInRole.Contains(u.Id));
            }
            else
            {
                // If role doesn't exist, return empty
                return new PagedResult<UserDto>(new List<UserDto>(), 0, page, pageSize);
            }
        }

        query = query.OrderByDescending(u => u.CreatedAt);

        var totalItems = await query.CountAsync();
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var dtos = new List<UserDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            dtos.Add(new UserDto
            {
                Id = u.Id,
                Username = u.UserName ?? string.Empty,
                Email = u.Email ?? string.Empty,
                PhoneNumber = u.PhoneNumber ?? string.Empty,
                FullName = u.FullName,
                IsActive = u.IsActive,
                Roles = roles.ToList(),
                CreatedAt = u.CreatedAt
            });
        }

        return new PagedResult<UserDto>(dtos, totalItems, page, pageSize);
    }

    public async Task<UserDto> GetUserByIdAsync(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) throw new NotFoundException("Tài khoản không tồn tại.");

        var roles = await _userManager.GetRolesAsync(user);
        return new UserDto
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            FullName = user.FullName,
            IsActive = user.IsActive,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<UserDto> CreateStaffUserAsync(CreateStaffUserDto request)
    {
        // Only allow creating Doctor, Receptionist, Admin
        if (request.Role != RoleNames.Doctor && request.Role != RoleNames.Receptionist && request.Role != RoleNames.Admin)
        {
            throw new BusinessException("INVALID_ROLE", "Vai trò không hợp lệ để tạo tài khoản nhân sự.");
        }

        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
            throw new BusinessException("EMAIL_EXISTS", "Email đã tồn tại.");

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
            throw new BusinessException("CREATE_FAILED", string.Join("; ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, request.Role);

        return new UserDto
        {
            Id = user.Id,
            Username = user.UserName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            FullName = user.FullName,
            IsActive = user.IsActive,
            Roles = new List<string> { request.Role },
            CreatedAt = user.CreatedAt
        };
    }

    public async Task ToggleUserStatusAsync(Guid id, ToggleUserStatusDto request)
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == id)
            throw new BusinessException("ACTION_FORBIDDEN", "Không thể tự khóa tài khoản của chính mình.");

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) throw new NotFoundException("Tài khoản không tồn tại.");

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(RoleNames.Admin) && !request.IsActive)
        {
            // Verify there is another active admin
            var adminRole = await _roleManager.FindByNameAsync(RoleNames.Admin);
            if (adminRole != null)
            {
                var activeAdminCount = await (from ur in _dbContext.UserRoles
                                              join u in _dbContext.Users on ur.UserId equals u.Id
                                              where ur.RoleId == adminRole.Id && u.IsActive && u.Id != id
                                              select u.Id).CountAsync();
                
                if (activeAdminCount == 0)
                    throw new BusinessException("LAST_ADMIN", "Không thể vô hiệu hóa Admin hoạt động cuối cùng của hệ thống.");
            }
        }

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
    }
}
