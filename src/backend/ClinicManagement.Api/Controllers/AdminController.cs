using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Admin.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = RoleNames.Admin)]
public class AdminController : ControllerBase
{
    private readonly IAdminUserService _adminUserService;
    private readonly IAdminSpecialtyService _adminSpecialtyService;
    private readonly IAdminDoctorService _adminDoctorService;

    public AdminController(IAdminUserService adminUserService, IAdminSpecialtyService adminSpecialtyService, IAdminDoctorService adminDoctorService)
    {
        _adminUserService = adminUserService;
        _adminSpecialtyService = adminSpecialtyService;
        _adminDoctorService = adminDoctorService;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromServices] AppDbContext dbContext)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var totalPatients = await dbContext.Patients.CountAsync();
        var totalDoctors = await dbContext.Doctors.CountAsync();
        var totalAppointmentsToday = await dbContext.Appointments.CountAsync(a => a.AppointmentDate == today);
        var totalAppointmentsAll = await dbContext.Appointments.CountAsync();
        var totalPrescriptions = await dbContext.Prescriptions.CountAsync();
        var totalHealthPackages = await dbContext.HealthPackages.CountAsync();
        var lowStockCount = await dbContext.Medicines.CountAsync(m => m.StockQuantity <= m.ReorderLevel);

        var stats = new AdminStatsDto
        {
            TotalPatients = totalPatients,
            TotalDoctors = totalDoctors,
            TotalAppointmentsToday = totalAppointmentsToday,
            TotalAppointmentsAll = totalAppointmentsAll,
            TotalPrescriptions = totalPrescriptions,
            TotalHealthPackages = totalHealthPackages,
            LowStockMedicinesCount = lowStockCount
        };

        return Ok(ApiResponse<AdminStatsDto>.Ok(stats));
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromServices] AppDbContext dbContext,
        [FromQuery] string? action,
        [FromQuery] string? entityName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15)
    {
        var query = from l in dbContext.SystemAuditLogs.AsNoTracking()
                    join u in dbContext.Users.AsNoTracking() on l.UserId equals u.Id into uJoin
                    from user in uJoin.DefaultIfEmpty()
                    select new
                    {
                        Log = l,
                        UserName = user != null ? user.FullName : "System"
                    };

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Log.Action == action.Trim());
        }

        if (!string.IsNullOrWhiteSpace(entityName))
        {
            query = query.Where(x => x.Log.EntityName == entityName.Trim());
        }

        query = query.OrderByDescending(x => x.Log.CreatedAt);

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SystemAuditLogDto
            {
                Id = x.Log.Id,
                UserId = x.Log.UserId,
                UserFullName = x.UserName,
                Action = x.Log.Action,
                EntityName = x.Log.EntityName,
                EntityId = x.Log.EntityId,
                Description = x.Log.Description,
                CreatedAt = x.Log.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<PagedResult<SystemAuditLogDto>>.Ok(new PagedResult<SystemAuditLogDto>(items, totalItems, page, pageSize)));
    }

    // USERS
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? role, [FromQuery] bool? isActive, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _adminUserService.GetUsersAsync(role, isActive, search, page, pageSize);
        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(result));
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUserById(Guid id)
    {
        var result = await _adminUserService.GetUserByIdAsync(id);
        return Ok(ApiResponse<UserDto>.Ok(result));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateStaffUser([FromBody] CreateStaffUserDto request)
    {
        var result = await _adminUserService.CreateStaffUserAsync(request);
        return Ok(ApiResponse<UserDto>.Ok(result, "Tạo tài khoản thành công."));
    }

    [HttpPatch("users/{id}/status")]
    public async Task<IActionResult> ToggleUserStatus(Guid id, [FromBody] ToggleUserStatusDto request)
    {
        await _adminUserService.ToggleUserStatusAsync(id, request);
        return Ok(ApiResponse.Ok("Cập nhật trạng thái tài khoản thành công."));
    }

    // SPECIALTIES
    [HttpGet("specialties")]
    public async Task<IActionResult> GetSpecialties([FromQuery] bool? isActive, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _adminSpecialtyService.GetSpecialtiesAsync(isActive, search, page, pageSize);
        return Ok(ApiResponse<PagedResult<AdminSpecialtyDto>>.Ok(result));
    }

    [HttpGet("specialties/{id}")]
    public async Task<IActionResult> GetSpecialtyById(long id)
    {
        var result = await _adminSpecialtyService.GetSpecialtyByIdAsync(id);
        return Ok(ApiResponse<AdminSpecialtyDto>.Ok(result));
    }

    [HttpPost("specialties")]
    public async Task<IActionResult> CreateSpecialty([FromBody] CreateSpecialtyDto request)
    {
        var result = await _adminSpecialtyService.CreateSpecialtyAsync(request);
        return Ok(ApiResponse<AdminSpecialtyDto>.Ok(result, "Tạo chuyên khoa thành công."));
    }

    [HttpPut("specialties/{id}")]
    public async Task<IActionResult> UpdateSpecialty(long id, [FromBody] UpdateSpecialtyDto request)
    {
        var result = await _adminSpecialtyService.UpdateSpecialtyAsync(id, request);
        return Ok(ApiResponse<AdminSpecialtyDto>.Ok(result, "Cập nhật chuyên khoa thành công."));
    }

    // DOCTORS
    [HttpGet("doctors")]
    public async Task<IActionResult> GetDoctors([FromQuery] bool? isActive, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _adminDoctorService.GetDoctorsAsync(isActive, search, page, pageSize);
        return Ok(ApiResponse<PagedResult<AdminDoctorDto>>.Ok(result));
    }

    [HttpGet("doctors/{id}")]
    public async Task<IActionResult> GetDoctorById(long id)
    {
        var result = await _adminDoctorService.GetDoctorByIdAsync(id);
        return Ok(ApiResponse<AdminDoctorDto>.Ok(result));
    }

    [HttpPost("doctors")]
    public async Task<IActionResult> CreateDoctor([FromBody] CreateDoctorDto request)
    {
        var result = await _adminDoctorService.CreateDoctorAsync(request);
        return Ok(ApiResponse<AdminDoctorDto>.Ok(result, "Tạo hồ sơ bác sĩ thành công."));
    }

    [HttpPut("doctors/{id}")]
    public async Task<IActionResult> UpdateDoctor(long id, [FromBody] UpdateDoctorDto request)
    {
        var result = await _adminDoctorService.UpdateDoctorAsync(id, request);
        return Ok(ApiResponse<AdminDoctorDto>.Ok(result, "Cập nhật hồ sơ bác sĩ thành công."));
    }

    [HttpPut("doctors/{id}/specialties")]
    public async Task<IActionResult> AssignSpecialties(long id, [FromBody] List<AssignSpecialtyDto> request)
    {
        await _adminDoctorService.AssignSpecialtiesAsync(id, request);
        return Ok(ApiResponse.Ok("Cập nhật chuyên khoa thành công."));
    }
}
