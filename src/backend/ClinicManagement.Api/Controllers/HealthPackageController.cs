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
[Route("api/v1/health-packages")]
public class HealthPackageController : ControllerBase
{
    private readonly IHealthPackageService _healthPackageService;

    public HealthPackageController(IHealthPackageService healthPackageService)
    {
        _healthPackageService = healthPackageService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<HealthPackageDto>>>> GetActivePackages(CancellationToken cancellationToken)
    {
        var packages = await _healthPackageService.GetActivePackagesAsync(cancellationToken);
        return Ok(ApiResponse<List<HealthPackageDto>>.Ok(packages));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<HealthPackageDto>>> GetPackageById(long id, CancellationToken cancellationToken)
    {
        var package = await _healthPackageService.GetPackageByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<HealthPackageDto>.Ok(package));
    }

    [HttpGet("/api/v1/admin/health-packages")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<List<HealthPackageDto>>>> GetAllPackagesForAdmin(CancellationToken cancellationToken)
    {
        var packages = await _healthPackageService.GetAllPackagesAsync(cancellationToken);
        return Ok(ApiResponse<List<HealthPackageDto>>.Ok(packages));
    }

    [HttpPost("/api/v1/admin/health-packages")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<HealthPackageDto>>> CreatePackage([FromBody] CreateHealthPackageRequest request, CancellationToken cancellationToken)
    {
        var package = await _healthPackageService.CreatePackageAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPackageById), new { id = package.Id }, ApiResponse<HealthPackageDto>.Ok(package, "Tạo gói khám thành công."));
    }

    [HttpPut("/api/v1/admin/health-packages/{id}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse<HealthPackageDto>>> UpdatePackage(long id, [FromBody] UpdateHealthPackageRequest request, CancellationToken cancellationToken)
    {
        var package = await _healthPackageService.UpdatePackageAsync(id, request, cancellationToken);
        return Ok(ApiResponse<HealthPackageDto>.Ok(package, "Cập nhật gói khám thành công."));
    }

    [HttpDelete("/api/v1/admin/health-packages/{id}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<ApiResponse>> DeletePackage(long id, CancellationToken cancellationToken)
    {
        await _healthPackageService.DeletePackageAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok("Xóa gói khám thành công."));
    }
}
