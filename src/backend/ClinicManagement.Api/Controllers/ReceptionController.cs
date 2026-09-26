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
    private readonly ClinicManagement.Application.Visits.Interfaces.IPatientVisitService _patientVisitService;

    public ReceptionController(
        IReceptionService receptionService,
        IChangeRequestService changeRequestService,
        ClinicManagement.Application.Visits.Interfaces.IPatientVisitService patientVisitService)
    {
        _receptionService = receptionService;
        _changeRequestService = changeRequestService;
        _patientVisitService = patientVisitService;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] long? facilityId = null)
    {
        var result = await _receptionService.GetStatsAsync(facilityId);
        return Ok(ApiResponse<ReceptionStatsDto>.Ok(result));
    }

    [HttpGet("appointments")]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? tab,
        [FromQuery] long? facilityId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _receptionService.GetAppointmentsAsync(status, tab, facilityId, search, page, pageSize);
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
    public async Task<IActionResult> CheckInAppointment(long id, System.Threading.CancellationToken cancellationToken)
    {
        var ticket = await _patientVisitService.CheckInAppointmentAsync(new ClinicManagement.Application.Visits.DTOs.AppointmentCheckInRequest { AppointmentId = id }, cancellationToken);
        return Ok(ApiResponse<ClinicManagement.Application.Visits.DTOs.CheckInTicketDto>.Ok(ticket, "Tiếp nhận và check-in bệnh nhân thành công."));
    }

    [HttpPost("walk-in")]
    public async Task<IActionResult> RegisterWalkIn([FromBody] ClinicManagement.Application.Visits.DTOs.WalkInRegistrationRequest request, System.Threading.CancellationToken cancellationToken)
    {
        var ticket = await _patientVisitService.CreateWalkInVisitAsync(request, cancellationToken);
        return Ok(ApiResponse<ClinicManagement.Application.Visits.DTOs.CheckInTicketDto>.Ok(ticket, "Tiếp nhận bệnh nhân vãng lai thành công."));
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
