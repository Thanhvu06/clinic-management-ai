using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Pharmacy.DTOs;
using ClinicManagement.Infrastructure.Pharmacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController, Route("api/v1/pharmacy/batches")]
[Authorize(Roles = RoleNames.Pharmacist + "," + RoleNames.Admin)]
public class PharmacyBatchesController(MedicineBatchService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(ApiResponse<PagedResult<MedicineBatchDto>>.Ok(await service.ListAsync(status, search, page, pageSize)));

    [HttpPost]
    public async Task<IActionResult> Create(CreateMedicineBatchDto request) =>
        Ok(ApiResponse<MedicineBatchDto>.Ok(await service.CreateAsync(request), "Nhập lô thuốc thành công."));

    [HttpPost("{id}/dispose")]
    public async Task<IActionResult> Dispose(long id, DisposeMedicineBatchDto request) =>
        Ok(ApiResponse<MedicineBatchDto>.Ok(await service.DisposeAsync(id, request), "Hủy lô thuốc thành công."));
}
