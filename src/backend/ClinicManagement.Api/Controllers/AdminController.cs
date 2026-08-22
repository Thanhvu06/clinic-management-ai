using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Admin.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
