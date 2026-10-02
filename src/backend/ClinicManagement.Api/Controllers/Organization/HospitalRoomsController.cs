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
[Route("api/v1/hospital-rooms")]
public class HospitalRoomsController : ControllerBase
{
    private readonly IOrganizationService _organizationService;

    public HospitalRoomsController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoomDto>>>> GetRooms(
        [FromQuery] long? departmentId = null,
        [FromQuery] long? facilityId = null,
        CancellationToken cancellationToken = default)
    {
        var rooms = await _organizationService.GetRoomsAsync(departmentId, facilityId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<RoomDto>>.Ok(rooms));
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<RoomDto>>> GetRoomById(long id, CancellationToken cancellationToken = default)
    {
        var room = await _organizationService.GetRoomByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<RoomDto>.Ok(room));
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<RoomDto>>> CreateRoom([FromBody] CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        var created = await _organizationService.CreateRoomAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetRoomById), new { id = created.Id }, ApiResponse<RoomDto>.Ok(created, "Tạo phòng thành công."));
    }
}
