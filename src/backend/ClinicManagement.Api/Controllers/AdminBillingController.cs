using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/admin/billing")]
[Authorize(Roles = RoleNames.Admin)]
public class AdminBillingController : ControllerBase
{
    private readonly IBillingService _billingService;
    private readonly ICurrentUserService _currentUserService;

    public AdminBillingController(IBillingService billingService, ICurrentUserService currentUserService)
    {
        _billingService = billingService;
        _currentUserService = currentUserService;
    }

    [HttpGet("revenue")]
    public async Task<ActionResult<ApiResponse<RevenueReportDto>>> GetRevenueReport(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var result = await _billingService.GetRevenueReportAsync(fromDate, toDate, cancellationToken);
        return Ok(ApiResponse<RevenueReportDto>.Ok(result));
    }

    [HttpGet("specialties")]
    public async Task<ActionResult<ApiResponse<List<SpecialtyFeeDto>>>> GetSpecialtyFees(CancellationToken cancellationToken)
    {
        var result = await _billingService.GetSpecialtyFeesAsync(cancellationToken);
        return Ok(ApiResponse<List<SpecialtyFeeDto>>.Ok(result));
    }

    [HttpPatch("specialties/{id}/fee")]
    public async Task<ActionResult<ApiResponse<SpecialtyFeeDto>>> UpdateSpecialtyFee(
        long id,
        [FromBody] UpdateSpecialtyFeeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var result = await _billingService.UpdateSpecialtyFeeAsync(id, request.ConsultationFee, userId.Value, cancellationToken);
        return Ok(ApiResponse<SpecialtyFeeDto>.Ok(result, "Cập nhật phí khám chuyên khoa thành công."));
    }
}
