using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/patient-visits")]
[Authorize]
public class PatientVisitsController : ControllerBase
{
    private readonly IPatientVisitService _patientVisitService;

    public PatientVisitsController(IPatientVisitService patientVisitService)
    {
        _patientVisitService = patientVisitService;
    }

    [HttpPost("intake")]
    [Authorize(Roles = RoleNames.Receptionist + "," + RoleNames.Admin)]
    public async Task<IActionResult> ReceptionIntake(
        [FromBody] ReceptionIntakeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.IdempotencyKey ??= idempotencyKey;
        }

        var ticket = await _patientVisitService.ReceptionIntakeAsync(request, cancellationToken);
        return Ok(ApiResponse<CheckInTicketDto>.Ok(ticket, "Tiếp nhận lượt khám thành công."));
    }

    [HttpPost("walk-in")]
    [Authorize(Roles = RoleNames.Receptionist + "," + RoleNames.Admin)]
    public async Task<IActionResult> CreateWalkInVisit([FromBody] WalkInRegistrationRequest request, CancellationToken cancellationToken)
    {
        var ticket = await _patientVisitService.CreateWalkInVisitAsync(request, cancellationToken);
        return Ok(ApiResponse<CheckInTicketDto>.Ok(ticket, "Tiếp nhận bệnh nhân vãng lai thành công."));
    }

    [HttpPost("check-in-appointment")]
    [Authorize(Roles = RoleNames.Receptionist + "," + RoleNames.Admin)]
    public async Task<IActionResult> CheckInAppointment([FromBody] AppointmentCheckInRequest request, CancellationToken cancellationToken)
    {
        var ticket = await _patientVisitService.CheckInAppointmentAsync(request, cancellationToken);
        return Ok(ApiResponse<CheckInTicketDto>.Ok(ticket, "Check-in cuộc hẹn thành công."));
    }

    [HttpGet("{id}")]
    [Authorize(Roles = RoleNames.Doctor + "," + RoleNames.Receptionist + "," + RoleNames.Admin + "," + RoleNames.Pharmacist + "," + RoleNames.DiagnosticTechnician)]
    public async Task<IActionResult> GetVisitById(long id, CancellationToken cancellationToken)
    {
        var detail = await _patientVisitService.GetVisitByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<PatientVisitDetailDto>.Ok(detail));
    }

    [HttpGet("{id}/ticket")]
    [Authorize(Roles = RoleNames.Receptionist + "," + RoleNames.Admin)]
    public async Task<IActionResult> GetCheckInTicket(long id, CancellationToken cancellationToken)
    {
        var ticket = await _patientVisitService.GetCheckInTicketAsync(id, cancellationToken);
        return Ok(ApiResponse<CheckInTicketDto>.Ok(ticket));
    }

    [HttpGet("department-queue")]
    [Authorize(Roles = RoleNames.Doctor + "," + RoleNames.Receptionist + "," + RoleNames.Admin)]
    public async Task<IActionResult> GetDepartmentQueue([FromQuery] long departmentId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var queue = await _patientVisitService.GetDepartmentQueueAsync(departmentId, date, cancellationToken);
        return Ok(ApiResponse<List<DepartmentQueueItemDto>>.Ok(queue));
    }

    [HttpPost("{id}/assign-doctor")]
    [Authorize(Roles = RoleNames.Doctor + "," + RoleNames.Receptionist + "," + RoleNames.Admin)]
    public async Task<IActionResult> AssignDoctor(long id, [FromBody] AssignDoctorRequest request, CancellationToken cancellationToken)
    {
        var updated = await _patientVisitService.AssignDoctorAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PatientVisitDetailDto>.Ok(updated, "Phân bổ bác sĩ thành công."));
    }

    [HttpPut("{id}/status")]
    [Authorize(Roles = RoleNames.Doctor + "," + RoleNames.Receptionist + "," + RoleNames.Admin)]
    public async Task<IActionResult> UpdateVisitStatus(long id, [FromQuery] VisitStatus status, [FromQuery] string? reason, CancellationToken cancellationToken)
    {
        var updated = await _patientVisitService.UpdateVisitStatusAsync(id, status, reason, cancellationToken);
        return Ok(ApiResponse<PatientVisitDetailDto>.Ok(updated, "Cập nhật trạng thái lượt khám thành công."));
    }
}
