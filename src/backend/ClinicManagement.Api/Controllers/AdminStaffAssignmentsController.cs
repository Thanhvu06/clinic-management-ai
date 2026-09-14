using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Organization.DTOs;
using ClinicManagement.Application.Organization.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/admin/staff-assignments")]
[Authorize(Roles = RoleNames.Admin)]
public class AdminStaffAssignmentsController : ControllerBase
{
    private readonly IOrganizationService _organizationService;

    public AdminStaffAssignmentsController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAssignments([FromQuery] long? facilityId, CancellationToken cancellationToken)
    {
        var assignments = await _organizationService.GetStaffAssignmentsAsync(facilityId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<StaffFacilityAssignmentDto>>.Ok(assignments));
    }

    [HttpPost]
    public async Task<IActionResult> CreateAssignment([FromBody] CreateStaffAssignmentRequest request, CancellationToken cancellationToken)
    {
        var created = await _organizationService.CreateStaffAssignmentAsync(request, cancellationToken);
        return Ok(ApiResponse<StaffFacilityAssignmentDto>.Ok(created));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAssignment(long id, CancellationToken cancellationToken)
    {
        await _organizationService.DeleteStaffAssignmentAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Hủy phân công nhân viên thành công."));
    }
}
