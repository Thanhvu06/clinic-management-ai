using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/patients/me/diagnostic-orders")]
[Authorize(Roles = RoleNames.Patient)]
public class PatientDiagnosticController : ControllerBase
{
    private readonly IDiagnosticWorkflowService _workflowService;

    public PatientDiagnosticController(IDiagnosticWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _workflowService.GetPatientOrdersAsync(page, pageSize);
        return Ok(ApiResponse<PagedResult<DiagnosticOrderDto>>.Ok(result));
    }

    [HttpGet("{orderId}")]
    public async Task<IActionResult> GetMyOrderById(long orderId)
    {
        var order = await _workflowService.GetPatientOrderByIdAsync(orderId);
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(order));
    }
}
