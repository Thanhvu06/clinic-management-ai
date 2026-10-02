using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Organization.DTOs;
using ClinicManagement.Application.Organization.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers.Organization;

[ApiController]
[Route("api/v1/beds")]
public class BedsController : ControllerBase
{
    private readonly IOrganizationService _organizationService;

    public BedsController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    [HttpGet("by-room/{roomId}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BedDto>>>> GetBedsByRoom(long roomId, CancellationToken cancellationToken = default)
    {
        var beds = await _organizationService.GetBedsByRoomAsync(roomId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BedDto>>.Ok(beds));
    }

    [HttpGet("by-department/{departmentId}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BedDto>>>> GetBedsByDepartment(long departmentId, CancellationToken cancellationToken = default)
    {
        var beds = await _organizationService.GetBedsByDepartmentAsync(departmentId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BedDto>>.Ok(beds));
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<BedDto>>> CreateBed([FromBody] CreateBedRequest request, CancellationToken cancellationToken = default)
    {
        var created = await _organizationService.CreateBedAsync(request, cancellationToken);
        return Ok(ApiResponse<BedDto>.Ok(created, "Thêm giường bệnh thành công."));
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Doctor},{RoleNames.Receptionist}")]
    public async Task<ActionResult<ApiResponse<BedDto>>> UpdateBedStatus(long id, [FromBody] UpdateBedStatusRequest request, CancellationToken cancellationToken = default)
    {
        var updated = await _organizationService.UpdateBedStatusAsync(id, request, cancellationToken);
        return Ok(ApiResponse<BedDto>.Ok(updated, "Cập nhật trạng thái giường bệnh thành công."));
    }
}
