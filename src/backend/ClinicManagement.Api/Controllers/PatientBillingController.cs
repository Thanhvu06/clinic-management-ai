using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/patient/invoices")]
[Authorize(Roles = RoleNames.Patient)]
public class PatientBillingController : ControllerBase
{
    private readonly IBillingService _billingService;
    private readonly ICurrentUserService _currentUserService;
    private readonly AppDbContext _dbContext;

    public PatientBillingController(
        IBillingService billingService,
        ICurrentUserService currentUserService,
        AppDbContext dbContext)
    {
        _billingService = billingService;
        _currentUserService = currentUserService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<InvoiceDto>>>> GetMyInvoices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] InvoiceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var patient = await GetCurrentPatientAsync(cancellationToken);
        var result = await _billingService.GetPatientInvoicesAsync(patient.Id, page, pageSize, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<InvoiceDto>>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<InvoiceDetailDto>>> GetMyInvoiceDetail(
        long id,
        CancellationToken cancellationToken = default)
    {
        var patient = await GetCurrentPatientAsync(cancellationToken);
        var result = await _billingService.GetPatientInvoiceDetailAsync(id, patient.Id, cancellationToken);
        return Ok(ApiResponse<InvoiceDetailDto>.Ok(result));
    }

    private async Task<Domain.Entities.Patient> GetCurrentPatientAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var patient = await _dbContext.Patients
            .FirstOrDefaultAsync(p => p.UserId == userId.Value, cancellationToken);

        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        return patient;
    }
}
