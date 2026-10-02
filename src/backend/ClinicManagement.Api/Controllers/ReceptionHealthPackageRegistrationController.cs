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
    public async Task<ActionResult<ApiResponse<PagedResult<HealthPackageRegistrationDto>>>> GetAllRegistrations(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var results = await _registrationService.GetAllRegistrationsForReceptionAsync(status, search, page, pageSize, cancellationToken);
        return Ok(ApiResponse<PagedResult<HealthPackageRegistrationDto>>.Ok(results));
    }

    [HttpPost("{id}/confirm")]
    public async Task<ActionResult<ApiResponse<HealthPackageRegistrationDto>>> ConfirmRegistration(
        long id,
        [FromBody] ConfirmPackageRegistrationRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _registrationService.ConfirmRegistrationAsync(id, request, cancellationToken);
        return Ok(ApiResponse<HealthPackageRegistrationDto>.Ok(result, "Xác nhận đăng ký gói khám thành công."));
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse<HealthPackageRegistrationDto>>> CancelRegistration(
        long id,
        [FromBody] CancelPackageRegistrationRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _registrationService.CancelRegistrationByReceptionAsync(id, request, cancellationToken);
        return Ok(ApiResponse<HealthPackageRegistrationDto>.Ok(result, "Hủy đăng ký gói khám thành công."));
    }
}
