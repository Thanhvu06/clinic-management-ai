using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/appointments")]
[Authorize(Roles = RoleNames.Patient)]
public class AppointmentController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public AppointmentController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    [AllowAnonymous]
    [HttpGet("lookup")]
    public async Task<IActionResult> LookupAppointments([FromQuery] string query)
    {
        var results = await _appointmentService.LookupAppointmentsAsync(query);
        return Ok(ApiResponse<List<AppointmentLookupDto>>.Ok(results));
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequest request)
    {
        var appointment = await _appointmentService.CreateAppointmentAsync(request);
        return StatusCode(201, ApiResponse<AppointmentDto>.Ok(appointment, "Đặt lịch khám thành công."));
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetPatientAppointments([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var appointments = await _appointmentService.GetPatientAppointmentsAsync(status, page, pageSize);
        return Ok(ApiResponse<PagedResult<AppointmentDto>>.Ok(appointments));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPatientAppointmentById(long id)
    {
        var appointment = await _appointmentService.GetPatientAppointmentByIdAsync(id);
        return Ok(ApiResponse<AppointmentDto>.Ok(appointment));
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetAppointmentHistory(long id)
    {
        var history = await _appointmentService.GetAppointmentHistoryAsync(id);
        return Ok(ApiResponse<List<AppointmentHistoryDto>>.Ok(history));
    }

    [HttpPost("{id}/reschedule-requests")]
    public async Task<IActionResult> CreateRescheduleRequest(long id, [FromBody] ClinicManagement.Application.Appointments.DTOs.ChangeRequests.CreateRescheduleRequestDto request, [FromServices] IChangeRequestService changeRequestService)
    {
        var result = await changeRequestService.CreateRescheduleRequestAsync(id, request);
        return Ok(ApiResponse<ClinicManagement.Application.Appointments.DTOs.ChangeRequests.ChangeRequestDto>.Ok(result, "Yêu cầu đổi lịch đã được gửi."));
    }

    [HttpPost("{id}/cancellation-requests")]
    public async Task<IActionResult> CreateCancellationRequest(long id, [FromBody] ClinicManagement.Application.Appointments.DTOs.ChangeRequests.CreateCancellationRequestDto request, [FromServices] IChangeRequestService changeRequestService)
    {
        var result = await changeRequestService.CreateCancellationRequestAsync(id, request);
        return Ok(ApiResponse<ClinicManagement.Application.Appointments.DTOs.ChangeRequests.ChangeRequestDto>.Ok(result, "Yêu cầu hủy lịch đã được gửi."));
    }
}
