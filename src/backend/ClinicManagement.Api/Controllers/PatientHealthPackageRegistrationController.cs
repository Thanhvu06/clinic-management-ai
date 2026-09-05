using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.HealthPackages.DTOs;
using ClinicManagement.Application.HealthPackages.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/patient/health-package-registrations")]
[Authorize(Roles = RoleNames.Patient)]
public class PatientHealthPackageRegistrationController : ControllerBase
{
    private readonly IHealthPackageRegistrationService _registrationService;

    public PatientHealthPackageRegistrationController(IHealthPackageRegistrationService registrationService)
    {
        _registrationService = registrationService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<HealthPackageRegistrationDto>>> RegisterPackage([FromBody] CreatePackageRegistrationRequest request, CancellationToken cancellationToken)
    {
        var result = await _registrationService.RegisterPackageAsync(request, cancellationToken);
        return StatusCode(201, ApiResponse<HealthPackageRegistrationDto>.Ok(result, "Đăng ký gói khám thành công. Lễ tân sẽ liên hệ xác nhận trong thời gian sớm nhất."));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<HealthPackageRegistrationDto>>>> GetMyRegistrations(CancellationToken cancellationToken)
    {
        var results = await _registrationService.GetMyRegistrationsAsync(cancellationToken);
        return Ok(ApiResponse<List<HealthPackageRegistrationDto>>.Ok(results));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<HealthPackageRegistrationDto>>> GetRegistrationById(long id, CancellationToken cancellationToken)
    {
        var result = await _registrationService.GetRegistrationByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<HealthPackageRegistrationDto>.Ok(result));
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse<HealthPackageRegistrationDto>>> CancelRegistration(long id, CancellationToken cancellationToken)
    {
        var result = await _registrationService.CancelMyRegistrationAsync(id, cancellationToken);
        return Ok(ApiResponse<HealthPackageRegistrationDto>.Ok(result, "Hủy đăng ký gói khám thành công."));
    }
}
