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
    public async Task<IActionResult> GetMyAppointments([FromQuery] System.DateOnly? date, [FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _doctorAppointmentService.GetMyAppointmentsAsync(date, status, search, page, pageSize);
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

    [HttpGet("{id}/patient-context")]
    public async Task<IActionResult> GetPatientClinicalContext(long id)
    {
        var result = await _doctorAppointmentService.GetPatientClinicalContextAsync(id);
        return Ok(ApiResponse<PatientClinicalContextDto>.Ok(result));
    }

    [HttpPost("{id}/check-in")]
    public async Task<IActionResult> CheckInAppointment(long id)
    {
        await _doctorAppointmentService.CheckInAppointmentAsync(id);
        return Ok(ApiResponse.Ok("Đã tiếp nhận bệnh nhân vào phòng khám."));
    }

    [HttpPost("{id}/start-consultation")]
    public async Task<IActionResult> StartConsultation(long id)
    {
        await _doctorAppointmentService.StartConsultationAsync(id);
        return Ok(ApiResponse.Ok("Đã bắt đầu phiên khám lâm sàng."));
    }

    [HttpPost("{id}/complete")]
    public async Task<IActionResult> CompleteAppointment(long id, [FromBody] CompleteConsultationRequest request)
    {
        await _doctorAppointmentService.CompleteAppointmentAsync(id, request);
        return Ok(ApiResponse.Ok("Đã hoàn tất phiên khám lâm sàng."));
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

    [HttpGet("{id}/encounter")]
    public async Task<IActionResult> GetEncounter(long id)
    {
        var result = await _doctorAppointmentService.GetEncounterAsync(id);
        return Ok(ApiResponse<ClinicalEncounterDto?>.Ok(result));
    }

    [HttpPut("{id}/encounter")]
    public async Task<IActionResult> SaveEncounter(long id, [FromBody] SaveEncounterRequest request)
    {
        var result = await _doctorAppointmentService.SaveEncounterAsync(id, request);
        return Ok(ApiResponse<ClinicalEncounterDto>.Ok(result, "Đã lưu diễn tiến khám."));
    }

    [HttpGet("{id}/vitals")]
    public async Task<IActionResult> GetVitalSigns(long id)
    {
        var result = await _doctorAppointmentService.GetVitalSignsAsync(id);
        return Ok(ApiResponse<VitalSignsDto?>.Ok(result));
    }

    [HttpPut("{id}/vitals")]
    public async Task<IActionResult> SaveVitalSigns(long id, [FromBody] SaveVitalSignsRequest request)
    {
        var result = await _doctorAppointmentService.SaveVitalSignsAsync(id, request);
        return Ok(ApiResponse<VitalSignsDto>.Ok(result, "Đã lưu dấu hiệu sinh tồn."));
    }

    [HttpGet("{id}/prescription")]
    public async Task<IActionResult> GetPrescriptionDraft(long id)
    {
        var result = await _doctorAppointmentService.GetPrescriptionDraftAsync(id);
        return Ok(ApiResponse<PrescriptionDraftDto?>.Ok(result));
    }

    [HttpPut("{id}/prescription")]
    public async Task<IActionResult> SavePrescriptionDraft(long id, [FromBody] SavePrescriptionDraftRequest request)
    {
        var result = await _doctorAppointmentService.SavePrescriptionDraftAsync(id, request);
        return Ok(ApiResponse<PrescriptionDraftDto>.Ok(result, "Đã lưu nháp đơn thuốc."));
    }

    [HttpPost("{id}/prescription")]
    public async Task<IActionResult> SavePrescriptionDraftPost(long id, [FromBody] SavePrescriptionDraftRequest request)
    {
        var result = await _doctorAppointmentService.SavePrescriptionDraftAsync(id, request);
        return Ok(ApiResponse<PrescriptionDraftDto>.Ok(result, "Đã lưu nháp đơn thuốc."));
    }
}
