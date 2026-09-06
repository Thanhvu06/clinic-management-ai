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
[Route("api/v1/reception/billing")]
[Authorize(Roles = RoleNames.Receptionist)]
public class ReceptionBillingController : ControllerBase
{
    private readonly IBillingService _billingService;
    private readonly ICurrentUserService _currentUserService;

    public ReceptionBillingController(IBillingService billingService, ICurrentUserService currentUserService)
    {
        _billingService = billingService;
        _currentUserService = currentUserService;
    }

    [HttpGet("invoices")]
    public async Task<ActionResult<ApiResponse<PagedResult<InvoiceDto>>>> GetInvoices([FromQuery] InvoiceFilterParams filters, CancellationToken cancellationToken)
    {
        var result = await _billingService.GetReceptionInvoicesAsync(filters, cancellationToken);
        return Ok(ApiResponse<PagedResult<InvoiceDto>>.Ok(result));
    }

    [HttpGet("invoices/{id}")]
    public async Task<ActionResult<ApiResponse<InvoiceDetailDto>>> GetInvoiceById(long id, CancellationToken cancellationToken)
    {
        var result = await _billingService.GetInvoiceDetailAsync(id, cancellationToken);
        return Ok(ApiResponse<InvoiceDetailDto>.Ok(result));
    }

    [HttpPost("invoices/appointment")]
    public async Task<ActionResult<ApiResponse<InvoiceDetailDto>>> CreateInvoiceFromAppointment([FromBody] CreateAppointmentInvoiceRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var result = await _billingService.CreateInvoiceFromAppointmentAsync(request.AppointmentId, userId.Value, cancellationToken);
        return StatusCode(201, ApiResponse<InvoiceDetailDto>.Ok(result, "Lập hóa đơn khám bệnh thành công."));
    }

    [HttpPost("invoices/health-package")]
    public async Task<ActionResult<ApiResponse<InvoiceDetailDto>>> CreateInvoiceFromHealthPackage([FromBody] CreatePackageInvoiceRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var result = await _billingService.CreateInvoiceFromHealthPackageAsync(request.HealthPackageRegistrationId, userId.Value, cancellationToken);
        return StatusCode(201, ApiResponse<InvoiceDetailDto>.Ok(result, "Lập hóa đơn gói khám thành công."));
    }

    [HttpPost("invoices/{id}/pay")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> ProcessPayment(long id, [FromBody] ProcessPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var result = await _billingService.ProcessPaymentAsync(id, request, userId.Value, cancellationToken);
        return Ok(ApiResponse<PaymentDto>.Ok(result, "Thu tiền và cập nhật hóa đơn thành công."));
    }

    [HttpPatch("invoices/{id}/cancel")]
    public async Task<ActionResult<ApiResponse<InvoiceDetailDto>>> CancelInvoice(long id, [FromBody] CancelInvoiceRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var result = await _billingService.CancelInvoiceAsync(id, request.Reason, userId.Value, cancellationToken);
        return Ok(ApiResponse<InvoiceDetailDto>.Ok(result, "Hủy hóa đơn thành công."));
    }

    [HttpGet("kpi")]
    public async Task<ActionResult<ApiResponse<BillingKpiDto>>> GetTodayKpi(CancellationToken cancellationToken)
    {
        var result = await _billingService.GetTodayKpiAsync(cancellationToken);
        return Ok(ApiResponse<BillingKpiDto>.Ok(result));
    }
}
