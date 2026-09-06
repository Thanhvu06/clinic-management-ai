using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Appointments.DTOs.Reception;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/reception")]
[Authorize(Roles = RoleNames.Receptionist + "," + RoleNames.Admin)]
public class ReceptionController : ControllerBase
{
    private readonly IReceptionService _receptionService;
    private readonly IChangeRequestService _changeRequestService;

    public ReceptionController(IReceptionService receptionService, IChangeRequestService changeRequestService)
    {
        _receptionService = receptionService;
        _changeRequestService = changeRequestService;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var result = await _receptionService.GetStatsAsync();
        return Ok(ApiResponse<ReceptionStatsDto>.Ok(result));
    }

    [HttpGet("appointments")]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _receptionService.GetAppointmentsAsync(status, search, page, pageSize);
        return Ok(ApiResponse<PagedResult<ReceptionAppointmentDto>>.Ok(result));
    }

    [HttpGet("appointments/{id}")]
    public async Task<IActionResult> GetAppointmentById(long id)
    {
        var result = await _receptionService.GetAppointmentByIdAsync(id);
        return Ok(ApiResponse<ReceptionAppointmentDto>.Ok(result));
    }

    [HttpGet("appointments/{id}/history")]
    public async Task<IActionResult> GetAppointmentHistory(long id)
    {
        var result = await _receptionService.GetAppointmentHistoryAsync(id);
        return Ok(ApiResponse<List<AppointmentHistoryDto>>.Ok(result));
    }

    [HttpPost("appointments/{id}/confirm")]
    public async Task<IActionResult> ConfirmAppointment(long id)
    {
        await _receptionService.ConfirmAppointmentAsync(id);
        return Ok(ApiResponse.Ok("Xác nhận lịch khám thành công."));
    }

    [HttpPost("appointments/{id}/check-in")]
    public async Task<IActionResult> CheckInAppointment(long id)
    {
        await _receptionService.CheckInAppointmentAsync(id);
        return Ok(ApiResponse.Ok("Tiếp nhận và check-in bệnh nhân thành công."));
    }

    [HttpGet("change-requests")]
    public async Task<IActionResult> GetChangeRequests([FromQuery] string? requestType, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _changeRequestService.GetAllChangeRequestsAsync(requestType, status, page, pageSize);
        return Ok(ApiResponse<PagedResult<ChangeRequestDto>>.Ok(result));
    }

    [HttpGet("change-requests/{id}")]
    public async Task<IActionResult> GetChangeRequestById(long id)
    {
        var result = await _changeRequestService.GetChangeRequestByIdAsync(id);
        return Ok(ApiResponse<ChangeRequestDto>.Ok(result));
    }

    [HttpPost("change-requests/{id}/approve-reschedule")]
    public async Task<IActionResult> ApproveReschedule(long id, [FromBody] ProcessChangeRequestDto request)
    {
        await _changeRequestService.ApproveRescheduleAsync(id, request);
        return Ok(ApiResponse.Ok("Đã duyệt yêu cầu đổi lịch."));
    }

    [HttpPost("change-requests/{id}/approve-cancellation")]
    public async Task<IActionResult> ApproveCancellation(long id, [FromBody] ProcessChangeRequestDto request)
    {
        await _changeRequestService.ApproveCancellationAsync(id, request);
        return Ok(ApiResponse.Ok("Đã duyệt yêu cầu hủy lịch."));
    }

    [HttpPost("change-requests/{id}/reject")]
    public async Task<IActionResult> RejectChangeRequest(long id, [FromBody] ProcessChangeRequestDto request)
    {
        await _changeRequestService.RejectRequestAsync(id, request);
        return Ok(ApiResponse.Ok("Đã từ chối yêu cầu."));
    }
}
