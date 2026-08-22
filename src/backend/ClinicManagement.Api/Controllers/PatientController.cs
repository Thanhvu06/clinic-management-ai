using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Patients.DTOs;
using ClinicManagement.Application.Patients.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = RoleNames.Patient)]
public class PatientController : ControllerBase
{
    private readonly IPatientService _patientService;

    public PatientController(IPatientService patientService)
    {
        _patientService = patientService;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var profile = await _patientService.GetMyProfileAsync();
        return Ok(ApiResponse<PatientProfileDto>.Ok(profile));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdatePatientProfileRequest request)
    {
        await _patientService.UpdateMyProfileAsync(request);
        return Ok(ApiResponse.Ok("Cập nhật thông tin thành công"));
    }
}
