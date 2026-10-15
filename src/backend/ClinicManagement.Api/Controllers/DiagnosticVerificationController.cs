using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/diagnostic-verification")]
[Authorize(Roles = RoleNames.DiagnosticTechnician + "," + RoleNames.LabSupervisor + "," + RoleNames.Doctor)]
public class DiagnosticVerificationController(IDiagnosticWorkflowService workflowService) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats() =>
        Ok(ApiResponse<VerificationStatsDto>.Ok(await workflowService.GetVerificationStatsAsync()));

    [HttpGet("queue")]
    public async Task<IActionResult> Queue([FromQuery] int page = 1, [FromQuery] int pageSize = 15) =>
        Ok(ApiResponse<PagedResult<DiagnosticOrderDto>>.Ok(await workflowService.GetVerificationQueueAsync(page, pageSize)));

    [HttpGet("orders/{orderId}")]
    public async Task<IActionResult> Order(long orderId) =>
        Ok(ApiResponse<DiagnosticOrderDto>.Ok(await workflowService.GetOrderForVerificationAsync(orderId)));

    [HttpPost("orders/{orderId}/verify")]
    public async Task<IActionResult> Verify(long orderId, [FromBody] VerifyDiagnosticOrderRequest request) =>
        Ok(ApiResponse<DiagnosticOrderDto>.Ok(await workflowService.VerifyOrderAsync(orderId, request)));

    [HttpPost("orders/{orderId}/return")]
    public async Task<IActionResult> Return(long orderId, [FromBody] ReturnDiagnosticOrderRequest request) =>
        Ok(ApiResponse<DiagnosticOrderDto>.Ok(await workflowService.ReturnOrderAsync(orderId, request)));
}
