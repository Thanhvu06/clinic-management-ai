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
[Route("api/v1/facilities")]
public class FacilitiesController : ControllerBase
{
    private readonly IOrganizationService _organizationService;

    public FacilitiesController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FacilityDto>>>> GetFacilities([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var facilities = await _organizationService.GetFacilitiesAsync(includeInactive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<FacilityDto>>.Ok(facilities));
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<FacilityDto>>> GetFacilityById(long id, CancellationToken cancellationToken = default)
    {
        var facility = await _organizationService.GetFacilityByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<FacilityDto>.Ok(facility));
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<FacilityDto>>> CreateFacility([FromBody] CreateFacilityRequest request, CancellationToken cancellationToken = default)
    {
        var created = await _organizationService.CreateFacilityAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetFacilityById), new { id = created.Id }, ApiResponse<FacilityDto>.Ok(created, "Tạo cơ sở y tế thành công."));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<FacilityDto>>> UpdateFacility(long id, [FromBody] UpdateFacilityRequest request, CancellationToken cancellationToken = default)
    {
        var updated = await _organizationService.UpdateFacilityAsync(id, request, cancellationToken);
        return Ok(ApiResponse<FacilityDto>.Ok(updated, "Cập nhật cơ sở y tế thành công."));
    }

    [HttpPatch("{id}/toggle-status")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<FacilityDto>>> ToggleStatus(long id, CancellationToken cancellationToken = default)
    {
        var updated = await _organizationService.ToggleFacilityStatusAsync(id, cancellationToken);
        return Ok(ApiResponse<FacilityDto>.Ok(updated, "Thay đổi trạng thái cơ sở y tế thành công."));
    }

    [HttpGet("{id}/buildings")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BuildingDto>>>> GetBuildings(long id, CancellationToken cancellationToken = default)
    {
        var buildings = await _organizationService.GetBuildingsByFacilityAsync(id, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BuildingDto>>.Ok(buildings));
    }

    [HttpPost("{id}/buildings")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<BuildingDto>>> CreateBuilding(long id, [FromBody] CreateBuildingRequest request, CancellationToken cancellationToken = default)
    {
        request.FacilityId = id;
        var building = await _organizationService.CreateBuildingAsync(request, cancellationToken);
        return Ok(ApiResponse<BuildingDto>.Ok(building, "Thêm tòa nhà thành công."));
    }
}
