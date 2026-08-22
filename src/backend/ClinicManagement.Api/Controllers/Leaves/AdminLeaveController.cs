using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Leaves.DTOs;
using ClinicManagement.Application.Leaves.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers.Leaves;

[ApiController]
[Route("api/v1/admin/leave-requests")]
[Authorize(Roles = RoleNames.Admin)]
public class AdminLeaveController : ControllerBase
{
    private readonly IAdminLeaveService _adminLeaveService;

    public AdminLeaveController(IAdminLeaveService adminLeaveService)
    {
        _adminLeaveService = adminLeaveService;
    }

    [HttpGet]
    public async Task<IActionResult> GetLeaveRequests([FromQuery] long? doctorId, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _adminLeaveService.GetLeaveRequestsAsync(doctorId, status, page, pageSize);
        return Ok(ApiResponse<PagedResult<LeaveRequestDto>>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLeaveRequestById(long id)
    {
        var result = await _adminLeaveService.GetLeaveRequestByIdAsync(id);
        return Ok(ApiResponse<LeaveRequestDto>.Ok(result));
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApproveLeaveRequest(long id, [FromBody] AdminProcessLeaveRequestDto request)
    {
        await _adminLeaveService.ApproveLeaveRequestAsync(id, request);
        return Ok(ApiResponse.Ok("Đã duyệt yêu cầu nghỉ."));
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectLeaveRequest(long id, [FromBody] AdminProcessLeaveRequestDto request)
    {
        await _adminLeaveService.RejectLeaveRequestAsync(id, request);
        return Ok(ApiResponse.Ok("Đã từ chối yêu cầu nghỉ."));
    }
}
