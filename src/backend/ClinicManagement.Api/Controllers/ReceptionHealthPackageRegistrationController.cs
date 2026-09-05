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
[Route("api/v1/reception/health-package-registrations")]
[Authorize(Roles = RoleNames.Receptionist + "," + RoleNames.Admin)]
public class ReceptionHealthPackageRegistrationController : ControllerBase
{
    private readonly IHealthPackageRegistrationService _registrationService;

    public ReceptionHealthPackageRegistrationController(IHealthPackageRegistrationService registrationService)
    {
        _registrationService = registrationService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<HealthPackageRegistrationDto>>>> GetAllRegistrations([FromQuery] string? status, CancellationToken cancellationToken)
    {
        var results = await _registrationService.GetAllRegistrationsForReceptionAsync(status, cancellationToken);
        return Ok(ApiResponse<List<HealthPackageRegistrationDto>>.Ok(results));
    }

    [HttpPost("{id}/confirm")]
    public async Task<ActionResult<ApiResponse<HealthPackageRegistrationDto>>> ConfirmRegistration(long id, CancellationToken cancellationToken)
    {
        var result = await _registrationService.ConfirmRegistrationAsync(id, cancellationToken);
        return Ok(ApiResponse<HealthPackageRegistrationDto>.Ok(result, "Xác nhận đăng ký gói khám thành công."));
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse<HealthPackageRegistrationDto>>> CancelRegistration(long id, CancellationToken cancellationToken)
    {
        var result = await _registrationService.CancelRegistrationByReceptionAsync(id, cancellationToken);
        return Ok(ApiResponse<HealthPackageRegistrationDto>.Ok(result, "Hủy đăng ký gói khám thành công."));
    }
}
