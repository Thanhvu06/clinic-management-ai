using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Revisit;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/revisit-requests")]
[Authorize(Roles = RoleNames.Patient)]
public class RevisitRequestController : ControllerBase
{
    private readonly IRevisitService _revisitService;

    public RevisitRequestController(IRevisitService revisitService)
    {
        _revisitService = revisitService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyRevisitRequests([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _revisitService.GetMyRevisitRequestsAsync(status, page, pageSize);
        return Ok(ApiResponse<PagedResult<RevisitRequestDto>>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRevisitRequestById(long id)
    {
        var result = await _revisitService.GetRevisitRequestByIdAsync(id);
        return Ok(ApiResponse<RevisitRequestDto>.Ok(result));
    }

    [HttpPost("{id}/accept")]
    public async Task<IActionResult> AcceptRevisitRequest(long id, [FromBody] AcceptRevisitRequestDto request)
    {
        var appointment = await _revisitService.AcceptRevisitRequestAsync(id, request);
        return Ok(ApiResponse<ClinicManagement.Application.Appointments.DTOs.AppointmentDto>.Ok(
            appointment,
            "Đã đồng ý và đặt lịch tái khám thành công."));
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectRevisitRequest(long id, [FromBody] RejectRevisitRequestDto request)
    {
        await _revisitService.RejectRevisitRequestAsync(id, request);
        return Ok(ApiResponse.Ok("Đã từ chối đề xuất tái khám."));
    }
}
