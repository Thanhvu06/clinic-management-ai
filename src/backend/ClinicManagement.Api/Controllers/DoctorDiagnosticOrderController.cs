using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/doctor")]
[Authorize(Roles = RoleNames.Doctor)]
public class DoctorDiagnosticOrderController : ControllerBase
{
    private readonly IDiagnosticWorkflowService _workflowService;

    public DoctorDiagnosticOrderController(IDiagnosticWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpPost("appointments/{appointmentId}/diagnostic-orders")]
    public async Task<IActionResult> CreateOrder(long appointmentId, [FromBody] CreateDiagnosticOrderRequest request)
    {
        var order = await _workflowService.CreateOrderForDoctorAsync(appointmentId, request);
        return Created($"/api/v1/doctor/diagnostic-orders/{order.Id}", ApiResponse<DiagnosticOrderDto>.Ok(order, "Tạo phiếu chỉ định cận lâm sàng thành công."));
    }

    [HttpGet("appointments/{appointmentId}/diagnostic-orders")]
    public async Task<IActionResult> GetOrdersByAppointment(long appointmentId)
    {
        var orders = await _workflowService.GetOrdersByAppointmentForDoctorAsync(appointmentId);
        return Ok(ApiResponse<List<DiagnosticOrderDto>>.Ok(orders));
    }

    [HttpGet("diagnostic-orders/{orderId}")]
    public async Task<IActionResult> GetOrderById(long orderId)
    {
        var order = await _workflowService.GetOrderByIdForDoctorAsync(orderId);
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(order));
    }

    [HttpPost("diagnostic-orders/{orderId}/review")]
    public async Task<IActionResult> ReviewOrder(long orderId, [FromBody] TransitionDiagnosticOrderRequest? request)
    {
        var order = await _workflowService.ReviewOrderAsync(orderId, request);
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(order, "Đã xác nhận xem kết quả cận lâm sàng."));
    }

    [HttpPost("diagnostic-orders/{orderId}/cancel")]
    public async Task<IActionResult> CancelOrder(long orderId, [FromBody] CancelDiagnosticOrderRequest? request)
    {
        var order = await _workflowService.CancelOrderAsync(orderId, request);
        return Ok(ApiResponse<DiagnosticOrderDto>.Ok(order, "Hủy phiếu chỉ định thành công."));
    }
}
