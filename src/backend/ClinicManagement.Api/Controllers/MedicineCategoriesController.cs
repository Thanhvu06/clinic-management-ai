using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Infrastructure.Medicines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController, Route("api/v1/admin/medicine-categories")]
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Pharmacist)]
public class MedicineCategoriesController(MedicineCatalogService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get() => Ok(ApiResponse<List<MedicineCategoryDto>>.Ok(await service.GetCategoriesAsync()));
    [HttpPost] public async Task<IActionResult> Create(SaveMedicineCategoryDto dto) => Ok(ApiResponse<MedicineCategoryDto>.Ok(await service.SaveCategoryAsync(null, dto)));
    [HttpPut("{id}")] public async Task<IActionResult> Update(long id, SaveMedicineCategoryDto dto) => Ok(ApiResponse<MedicineCategoryDto>.Ok(await service.SaveCategoryAsync(id, dto)));
    [HttpPatch("{id}/toggle-status")] public async Task<IActionResult> Toggle(long id) => Ok(ApiResponse<MedicineCategoryDto>.Ok(await service.ToggleCategoryAsync(id)));
}
