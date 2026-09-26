using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/diagnostic-services")]
[Authorize(Roles = $"{RoleNames.Doctor},{RoleNames.DiagnosticTechnician},{RoleNames.Admin}")]
public class DiagnosticCatalogController : ControllerBase
{
    private readonly IDiagnosticWorkflowService _workflowService;

    public DiagnosticCatalogController(IDiagnosticWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpGet]
    public async Task<IActionResult> GetServices([FromQuery] DiagnosticCategory? category, [FromQuery] string? search)
    {
        var services = await _workflowService.GetDiagnosticServicesAsync(category, search);
        return Ok(ApiResponse<List<DiagnosticServiceDto>>.Ok(services));
    }
}
