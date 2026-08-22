using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Schedules.DTOs;
using ClinicManagement.Application.Schedules.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = RoleNames.Admin)]
public class AdminWorkScheduleController : ControllerBase
{
    private readonly IScheduleService _scheduleService;

    public AdminWorkScheduleController(IScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
    }

    [HttpGet("doctors/{doctorId}/work-schedules")]
    public async Task<IActionResult> GetDoctorWorkSchedules(long doctorId)
    {
        var schedules = await _scheduleService.GetDoctorWorkSchedulesAsync(doctorId);
        return Ok(ApiResponse<List<WorkScheduleDto>>.Ok(schedules));
    }

    [HttpPost("doctors/{doctorId}/work-schedules")]
    public async Task<IActionResult> CreateWorkSchedule(long doctorId, [FromBody] CreateWorkScheduleRequest request)
    {
        var schedule = await _scheduleService.CreateWorkScheduleAsync(doctorId, request);
        return StatusCode(201, ApiResponse<WorkScheduleDto>.Ok(schedule, "Tạo lịch làm việc thành công."));
    }

    [HttpPut("work-schedules/{scheduleId}")]
    public async Task<IActionResult> UpdateWorkSchedule(long scheduleId, [FromBody] UpdateWorkScheduleRequest request)
    {
        var schedule = await _scheduleService.UpdateWorkScheduleAsync(scheduleId, request);
        return Ok(ApiResponse<WorkScheduleDto>.Ok(schedule, "Cập nhật lịch làm việc thành công."));
    }

    [HttpPatch("work-schedules/{scheduleId}/status")]
    public async Task<IActionResult> UpdateWorkScheduleStatus(long scheduleId, [FromBody] UpdateWorkScheduleStatusRequest request)
    {
        await _scheduleService.UpdateWorkScheduleStatusAsync(scheduleId, request);
        return Ok(ApiResponse.Ok("Cập nhật trạng thái thành công."));
    }

    [HttpPost("work-schedules/{scheduleId}/generate-slots")]
    public async Task<IActionResult> GenerateSlots(long scheduleId)
    {
        await _scheduleService.GenerateSlotsAsync(scheduleId);
        return Ok(ApiResponse.Ok("Sinh slot thành công."));
    }
}
