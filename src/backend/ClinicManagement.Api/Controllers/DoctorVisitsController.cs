using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/doctor/visits")]
[Authorize(Roles = RoleNames.Doctor)]
public class DoctorVisitsController : ControllerBase
{
    private readonly IDoctorAppointmentService _doctorAppointmentService;

    public DoctorVisitsController(IDoctorAppointmentService doctorAppointmentService)
    {
        _doctorAppointmentService = doctorAppointmentService;
    }

    [HttpGet("{id}/patient-context")]
    [HttpGet("{id}/clinical-context")]
    public async Task<IActionResult> GetPatientClinicalContext(long id)
    {
        var result = await _doctorAppointmentService.GetVisitClinicalContextAsync(id);
        return Ok(ApiResponse<PatientClinicalContextDto>.Ok(result));
    }

    [HttpPost("{id}/start-consultation")]
    public async Task<IActionResult> StartConsultation(long id)
    {
        await _doctorAppointmentService.StartVisitConsultationAsync(id);
        return Ok(ApiResponse.Ok("Đã bắt đầu phiên khám lâm sàng từ lượt khám."));
    }

    [HttpPost("{id}/complete")]
    public async Task<IActionResult> CompleteVisit(long id, [FromBody] CompleteConsultationRequest request)
    {
        await _doctorAppointmentService.CompleteVisitConsultationAsync(id, request);
        return Ok(ApiResponse.Ok("Đã hoàn tất phiên khám lâm sàng cho lượt khám."));
    }

    [HttpGet("{id}/encounter")]
    public async Task<IActionResult> GetEncounter(long id)
    {
        var result = await _doctorAppointmentService.GetVisitEncounterAsync(id);
        return Ok(ApiResponse<ClinicalEncounterDto?>.Ok(result));
    }

    [HttpPut("{id}/encounter")]
    public async Task<IActionResult> SaveEncounter(long id, [FromBody] SaveEncounterRequest request)
    {
        var result = await _doctorAppointmentService.SaveVisitEncounterAsync(id, request);
        return Ok(ApiResponse<ClinicalEncounterDto>.Ok(result, "Đã lưu diễn tiến khám."));
    }

    [HttpGet("{id}/vitals")]
    public async Task<IActionResult> GetVitalSigns(long id)
    {
        var result = await _doctorAppointmentService.GetVisitVitalSignsAsync(id);
        return Ok(ApiResponse<VitalSignsDto?>.Ok(result));
    }

    [HttpPut("{id}/vitals")]
    public async Task<IActionResult> SaveVitalSigns(long id, [FromBody] SaveVitalSignsRequest request)
    {
        var result = await _doctorAppointmentService.SaveVisitVitalSignsAsync(id, request);
        return Ok(ApiResponse<VitalSignsDto>.Ok(result, "Đã lưu dấu hiệu sinh tồn."));
    }

    [HttpGet("{id}/prescription-draft")]
    public async Task<IActionResult> GetPrescriptionDraft(long id)
    {
        var result = await _doctorAppointmentService.GetVisitPrescriptionDraftAsync(id);
        return Ok(ApiResponse<PrescriptionDraftDto?>.Ok(result));
    }

    [HttpPut("{id}/prescription-draft")]
    public async Task<IActionResult> SavePrescriptionDraft(long id, [FromBody] SavePrescriptionDraftRequest request)
    {
        var result = await _doctorAppointmentService.SaveVisitPrescriptionDraftAsync(id, request);
        return Ok(ApiResponse<PrescriptionDraftDto>.Ok(result, "Đã lưu đơn thuốc nháp."));
    }
}
