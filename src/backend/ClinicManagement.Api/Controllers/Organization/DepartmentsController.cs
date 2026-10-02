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
[Route("api/v1/departments")]
public class DepartmentsController : ControllerBase
{
    private readonly IOrganizationService _organizationService;

    public DepartmentsController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DepartmentDto>>>> GetDepartments([FromQuery] long? facilityId = null, CancellationToken cancellationToken = default)
    {
        var departments = await _organizationService.GetDepartmentsAsync(facilityId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<DepartmentDto>>.Ok(departments));
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> GetDepartmentById(long id, CancellationToken cancellationToken = default)
    {
        var department = await _organizationService.GetDepartmentByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<DepartmentDto>.Ok(department));
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> CreateDepartment([FromBody] CreateDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        var created = await _organizationService.CreateDepartmentAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetDepartmentById), new { id = created.Id }, ApiResponse<DepartmentDto>.Ok(created, "Tạo khoa phòng thành công."));
    }
}
