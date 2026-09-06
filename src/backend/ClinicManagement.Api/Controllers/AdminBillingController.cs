using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Common.Constants;
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

    public AdminBillingController(IBillingService billingService)
    {
        _billingService = billingService;
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
        var result = await _billingService.UpdateSpecialtyFeeAsync(id, request.ConsultationFee, cancellationToken);
        return Ok(ApiResponse<SpecialtyFeeDto>.Ok(result, "Cập nhật phí khám chuyên khoa thành công."));
    }
}
