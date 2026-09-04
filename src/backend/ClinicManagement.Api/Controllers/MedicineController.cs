using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Application.Medicines.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
public class MedicineController : ControllerBase
{
    private readonly IMedicineService _medicineService;

    public MedicineController(IMedicineService medicineService)
    {
        _medicineService = medicineService;
    }

    [HttpGet("api/v1/medicines/active")]
    [Authorize]
    public async Task<IActionResult> GetActiveMedicines()
    {
        var result = await _medicineService.GetActiveMedicinesAsync();
        return Ok(ApiResponse<List<ActiveMedicineDto>>.Ok(result));
    }

    [HttpGet("api/v1/admin/medicines")]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Pharmacist)]
    public async Task<IActionResult> GetMedicines([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _medicineService.GetMedicinesAsync(search, isActive, page, pageSize);
        return Ok(ApiResponse<PagedResult<MedicineDto>>.Ok(result));
    }

    [HttpGet("api/v1/admin/medicines/{id}")]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Pharmacist)]
    public async Task<IActionResult> GetMedicineById(long id)
    {
        var result = await _medicineService.GetMedicineByIdAsync(id);
        return Ok(ApiResponse<MedicineDto>.Ok(result));
    }

    [HttpPost("api/v1/admin/medicines")]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Pharmacist)]
    public async Task<IActionResult> CreateMedicine([FromBody] CreateMedicineDto dto)
    {
        var result = await _medicineService.CreateMedicineAsync(dto);
        return Ok(ApiResponse<MedicineDto>.Ok(result, "Thêm thuốc mới vào danh mục thành công."));
    }

    [HttpPut("api/v1/admin/medicines/{id}")]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Pharmacist)]
    public async Task<IActionResult> UpdateMedicine(long id, [FromBody] UpdateMedicineDto dto)
    {
        var result = await _medicineService.UpdateMedicineAsync(id, dto);
        return Ok(ApiResponse<MedicineDto>.Ok(result, "Cập nhật thông tin thuốc thành công."));
    }

    [HttpPatch("api/v1/admin/medicines/{id}/toggle-status")]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Pharmacist)]
    public async Task<IActionResult> ToggleMedicineStatus(long id)
    {
        await _medicineService.ToggleMedicineStatusAsync(id);
        return Ok(ApiResponse.Ok("Thay đổi trạng thái thuốc thành công."));
    }
}
