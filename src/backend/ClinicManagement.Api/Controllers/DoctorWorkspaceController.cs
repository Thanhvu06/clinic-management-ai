using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/doctor")]
[Authorize(Roles = RoleNames.Doctor)]
public class DoctorWorkspaceController : ControllerBase
{
    private readonly IDoctorAppointmentService _doctorAppointmentService;

    public DoctorWorkspaceController(IDoctorAppointmentService doctorAppointmentService)
    {
        _doctorAppointmentService = doctorAppointmentService;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] DateOnly? date)
    {
        var result = await _doctorAppointmentService.GetDoctorDashboardAsync(date);
        return Ok(ApiResponse<DoctorDashboardDto>.Ok(result));
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule([FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate)
    {
        var result = await _doctorAppointmentService.GetDoctorScheduleAsync(fromDate, toDate);
        return Ok(ApiResponse<List<DoctorScheduleDayDto>>.Ok(result));
    }
}
