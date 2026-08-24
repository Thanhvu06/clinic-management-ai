using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/doctor/appointments")]
[Authorize(Roles = RoleNames.Doctor)]
public class DoctorAppointmentController : ControllerBase
{
    private readonly IDoctorAppointmentService _doctorAppointmentService;

    public DoctorAppointmentController(IDoctorAppointmentService doctorAppointmentService)
    {
        _doctorAppointmentService = doctorAppointmentService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyAppointments([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _doctorAppointmentService.GetMyAppointmentsAsync(status, search, page, pageSize);
        return Ok(ApiResponse<PagedResult<DoctorAppointmentDto>>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAppointmentById(long id)
    {
        var result = await _doctorAppointmentService.GetAppointmentByIdAsync(id);
        return Ok(ApiResponse<DoctorAppointmentDto>.Ok(result));
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetAppointmentHistory(long id)
    {
        var result = await _doctorAppointmentService.GetAppointmentHistoryAsync(id);
        return Ok(ApiResponse<List<AppointmentHistoryDto>>.Ok(result));
    }

    [HttpPost("{id}/complete")]
    public async Task<IActionResult> CompleteAppointment(long id, [FromBody] CompleteAppointmentDto request)
    {
        await _doctorAppointmentService.CompleteAppointmentAsync(id, request);
        return Ok(ApiResponse.Ok("Đã hoàn thành khám bệnh."));
    }

    [HttpPost("{id}/noshow")]
    public async Task<IActionResult> MarkNoShow(long id, [FromBody] NoShowAppointmentDto request)
    {
        await _doctorAppointmentService.MarkNoShowAsync(id, request);
        return Ok(ApiResponse.Ok("Đã đánh dấu vắng mặt."));
    }

    [HttpPost("{id}/revisit-requests")]
    public async Task<IActionResult> CreateRevisitRequest(long id, [FromBody] CreateRevisitRequestDto request)
    {
        var result = await _doctorAppointmentService.CreateRevisitRequestAsync(id, request);
        return Ok(ApiResponse<ClinicManagement.Application.Appointments.DTOs.Revisit.RevisitRequestDto>.Ok(result, "Đã tạo đề xuất tái khám."));
    }
}
