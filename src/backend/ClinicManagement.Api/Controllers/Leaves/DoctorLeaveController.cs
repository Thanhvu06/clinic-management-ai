using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Leaves.DTOs;
using ClinicManagement.Application.Leaves.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers.Leaves;

[ApiController]
[Route("api/v1/doctor/leave-requests")]
[Route("api/v1/doctor/leaves")]
[Authorize(Roles = RoleNames.Doctor)]
public class DoctorLeaveController : ControllerBase
{
    private readonly IDoctorLeaveService _doctorLeaveService;

    public DoctorLeaveController(IDoctorLeaveService doctorLeaveService)
    {
        _doctorLeaveService = doctorLeaveService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyLeaveRequests([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _doctorLeaveService.GetMyLeaveRequestsAsync(status, page, pageSize);
        return Ok(ApiResponse<PagedResult<LeaveRequestDto>>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLeaveRequestById(long id)
    {
        var result = await _doctorLeaveService.GetLeaveRequestByIdAsync(id);
        return Ok(ApiResponse<LeaveRequestDto>.Ok(result));
    }

    [HttpGet("preview")]
    [HttpGet("preview-impact")]
    public async Task<IActionResult> PreviewLeave(
        [FromQuery] DateTime? start, 
        [FromQuery] DateTime? end,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var actualStart = startDate ?? start ?? DateTime.Today;
        var actualEnd = endDate ?? end ?? actualStart;
        if (actualEnd.TimeOfDay == TimeSpan.Zero)
        {
            actualEnd = actualEnd.Date.AddDays(1).AddTicks(-1);
        }
        var result = await _doctorLeaveService.PreviewLeaveAffectedAppointmentsAsync(actualStart, actualEnd);
        return Ok(ApiResponse<LeavePreviewDto>.Ok(result));
    }

    [HttpPost]
    public async Task<IActionResult> CreateLeaveRequest([FromBody] CreateLeaveRequestDto request)
    {
        var result = await _doctorLeaveService.CreateLeaveRequestAsync(request);
        return Ok(ApiResponse<LeaveRequestDto>.Ok(result, "Tạo yêu cầu nghỉ thành công."));
    }

    [HttpPost("{id}/withdraw")]
    public async Task<IActionResult> WithdrawLeaveRequest(long id)
    {
        await _doctorLeaveService.WithdrawLeaveRequestAsync(id);
        return Ok(ApiResponse.Ok("Đã rút yêu cầu nghỉ."));
    }
}
