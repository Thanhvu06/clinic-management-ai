using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Infrastructure.Medicines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

// Same shared middleware/policies as SpecialtyController and DoctorController.
[ApiController, AllowAnonymous, Route("api/v1/public")]
public class PublicMedicinesController(MedicineCatalogService service) : ControllerBase
{
    [HttpGet("medicines")]
    public async Task<IActionResult> Medicines([FromQuery] string? search, [FromQuery] long? categoryId,
        [FromQuery] string? type, [FromQuery] int page = 1, [FromQuery] int pageSize = 10) =>
        Ok(ApiResponse<PagedResult<PublicMedicineDto>>.Ok(await service.GetPublicMedicinesAsync(search, categoryId, type, page, pageSize)));
    [HttpGet("medicines/{id}")]
    public async Task<IActionResult> Medicine(long id) => Ok(ApiResponse<PublicMedicineDto>.Ok(await service.GetPublicMedicineAsync(id)));
    [HttpGet("medicine-categories")]
    public async Task<IActionResult> Categories() => Ok(ApiResponse<List<PublicMedicineCategoryDto>>.Ok(await service.GetPublicCategoriesAsync()));
}
