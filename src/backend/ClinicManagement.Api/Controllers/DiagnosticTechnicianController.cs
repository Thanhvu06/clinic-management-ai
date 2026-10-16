using System;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/diagnostics")]
[Authorize(Roles = RoleNames.DiagnosticTechnician)]
public class DiagnosticTechnicianController : ControllerBase
{
    [HttpGet("orders/verification-queue")]
    public async Task<IActionResult> GetVerificationQueue([FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        return Ok(ApiResponse<PagedResult<DiagnosticOrderDto>>.Ok(await _workflowService.GetVerificationQueueAsync(page, pageSize)));
    }

    [HttpPost("orders/{orderId}/verify")]
    public async Task<IActionResult> VerifyOrder(long orderId, [FromBody] VerifyDiagnosticOrderRequest request)
    {
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(await _workflowService.VerifyOrderAsync(orderId, request)));
    }

    [HttpPost("orders/{orderId}/return")]
    public async Task<IActionResult> ReturnOrder(long orderId, [FromBody] ReturnDiagnosticOrderRequest request)
    {
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(await _workflowService.ReturnOrderAsync(orderId, request)));
    }

    [HttpPost("orders/{orderId}/items/{itemId}/specimen/collect")]
    public async Task<IActionResult> CollectSpecimen(long orderId, long itemId, [FromBody] CollectSpecimenRequest request)
    {
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(await _workflowService.CollectSpecimenAsync(orderId, itemId, request)));
    }

    [HttpPost("orders/{orderId}/items/{itemId}/specimen/receive")]
    public async Task<IActionResult> ReceiveSpecimen(long orderId, long itemId, [FromBody] ReceiveSpecimenRequest request)
    {
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(await _workflowService.ReceiveSpecimenAsync(orderId, itemId, request)));
    }

    [HttpPost("orders/{orderId}/items/{itemId}/specimen/reject")]
    public async Task<IActionResult> RejectSpecimen(long orderId, long itemId, [FromBody] RejectSpecimenRequest request)
    {
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(await _workflowService.RejectSpecimenAsync(orderId, itemId, request)));
    }

    private readonly IDiagnosticWorkflowService _workflowService;

    [HttpPost("orders/{orderId}/items/{itemId}/attachments")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(long orderId, long itemId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        await using var input = file.OpenReadStream();
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(await _workflowService.UploadAttachmentAsync(orderId, itemId, input, file.Length, file.FileName, cancellationToken)));
    }

    [HttpDelete("orders/{orderId}/items/{itemId}/attachments/{attachmentId}")]
    public async Task<IActionResult> DeleteAttachment(long orderId, long itemId, long attachmentId, CancellationToken cancellationToken)
    {
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(await _workflowService.DeleteAttachmentAsync(orderId, itemId, attachmentId, cancellationToken)));
    }

    public DiagnosticTechnicianController(IDiagnosticWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders(
        [FromQuery] DiagnosticOrderStatus? status,
        [FromQuery] DateOnly? date,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _workflowService.GetTechnicianOrdersAsync(status, date, search, page, pageSize);
        return Ok(ApiResponse<PagedResult<DiagnosticOrderDto>>.Ok(result));
    }

    [HttpGet("orders/stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = await _workflowService.GetTechnicianStatsAsync();
        return Ok(ApiResponse<TechnicianDiagnosticStatsDto>.Ok(stats));
    }

    [HttpGet("orders/{orderId}")]
    public async Task<IActionResult> GetOrderById(long orderId)
    {
        var order = await _workflowService.GetTechnicianOrderByIdAsync(orderId);
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(order));
    }

    [HttpPost("orders/{orderId}/start")]
    public async Task<IActionResult> StartOrder(long orderId, [FromBody] TransitionDiagnosticOrderRequest? request = null)
    {
        var order = await _workflowService.StartOrderAsync(orderId, request);
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(order, "Tiếp nhận thực hiện phiếu chỉ định thành công."));
    }

    [HttpPut("orders/{orderId}/items/{itemId}/result")]
    public async Task<IActionResult> RecordItemResult(long orderId, long itemId, [FromBody] RecordDiagnosticResultRequest request)
    {
        var order = await _workflowService.RecordItemResultAsync(orderId, itemId, request);
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(order, "Lưu kết quả dịch vụ cận lâm sàng thành công."));
    }

    [HttpPost("orders/{orderId}/complete")]
    public async Task<IActionResult> CompleteOrder(long orderId, [FromBody] TransitionDiagnosticOrderRequest? request = null)
    {
        var order = await _workflowService.CompleteOrderAsync(orderId, request);
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(order, "Hoàn tất phiếu chỉ định cận lâm sàng thành công."));
    }
}
