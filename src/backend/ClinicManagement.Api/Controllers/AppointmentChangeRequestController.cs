using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/appointment-change-requests")]
[Authorize(Roles = RoleNames.Patient)]
public class AppointmentChangeRequestController : ControllerBase
{
    private readonly IChangeRequestService _changeRequestService;

    public AppointmentChangeRequestController(IChangeRequestService changeRequestService)
    {
        _changeRequestService = changeRequestService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyChangeRequests([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _changeRequestService.GetMyChangeRequestsAsync(status, page, pageSize);
        return Ok(ApiResponse<PagedResult<ChangeRequestDto>>.Ok(result));
    }

    [HttpPost("{id}/withdraw")]
    public async Task<IActionResult> WithdrawRequest(long id)
    {
        await _changeRequestService.WithdrawRequestAsync(id);
        return Ok(ApiResponse.Ok("Đã rút yêu cầu thành công."));
    }
}
