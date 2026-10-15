using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/lab-reports")]
[Authorize(Roles = RoleNames.LabSupervisor)]
public class LabReportsController(IDiagnosticWorkflowService workflowService, IDateTimeProvider dateTimeProvider) : ControllerBase
{
    private const int DefaultRangeDays = 7;

    [HttpGet("turnaround")]
    public async Task<IActionResult> Turnaround([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] DiagnosticCategory? category)
    {
        var end = to ?? dateTimeProvider.VietnamToday;
        var start = from ?? end.AddDays(-(DefaultRangeDays - 1));
        return Ok(ApiResponse<DiagnosticTurnaroundReportDto>.Ok(await workflowService.GetTurnaroundReportAsync(start, end, category)));
    }
}
