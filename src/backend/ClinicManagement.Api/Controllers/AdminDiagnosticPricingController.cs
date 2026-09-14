using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/admin/diagnostic-services")]
[Authorize(Roles = RoleNames.Admin)]
public class AdminDiagnosticPricingController : ControllerBase
{
    private readonly IDiagnosticWorkflowService _workflowService;

    public AdminDiagnosticPricingController(IDiagnosticWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpGet("pricing")]
    public async Task<IActionResult> GetPricing(CancellationToken cancellationToken)
    {
        var services = await _workflowService.GetAllDiagnosticServicesPricingAsync(cancellationToken);
        return Ok(ApiResponse<List<DiagnosticServiceDto>>.Ok(services));
    }

    [HttpPut("{id}/pricing")]
    public async Task<IActionResult> UpdatePricing(long id, [FromBody] UpdateDiagnosticPriceRequest request, CancellationToken cancellationToken)
    {
        var updated = await _workflowService.UpdateServicePriceAsync(id, request.Price, cancellationToken);
        return Ok(ApiResponse<DiagnosticServiceDto>.Ok(updated));
    }
}
