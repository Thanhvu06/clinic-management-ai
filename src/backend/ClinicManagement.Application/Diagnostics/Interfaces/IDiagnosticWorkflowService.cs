using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Diagnostics.Interfaces;

public interface IDiagnosticWorkflowService
{
    // Catalog & Pricing
    Task<List<DiagnosticServiceDto>> GetDiagnosticServicesAsync(DiagnosticCategory? category, string? search);
    Task<List<DiagnosticServiceDto>> GetAllDiagnosticServicesPricingAsync(System.Threading.CancellationToken cancellationToken = default);
    Task<DiagnosticServiceDto> UpdateServicePriceAsync(long serviceId, decimal price, System.Threading.CancellationToken cancellationToken = default);

    // Doctor operations
    Task<DiagnosticOrderDto> CreateOrderForDoctorAsync(long appointmentId, CreateDiagnosticOrderRequest request);
    Task<DiagnosticOrderDto> CreateOrderForVisitDoctorAsync(long visitId, CreateDiagnosticOrderRequest request);
    Task<List<DiagnosticOrderDto>> GetOrdersByAppointmentForDoctorAsync(long appointmentId);
    Task<List<DiagnosticOrderDto>> GetOrdersByVisitForDoctorAsync(long visitId);
    Task<DiagnosticOrderDto> GetOrderByIdForDoctorAsync(long orderId);
    Task<DiagnosticOrderDto> ReviewOrderAsync(long orderId, TransitionDiagnosticOrderRequest? request);
    Task<DiagnosticOrderDto> CancelOrderAsync(long orderId, CancelDiagnosticOrderRequest? request);

    // Technician operations
    Task<PagedResult<DiagnosticOrderDto>> GetTechnicianOrdersAsync(DiagnosticOrderStatus? status, DateOnly? date, string? search, int page, int pageSize);
    Task<DiagnosticOrderDto> GetTechnicianOrderByIdAsync(long orderId);
    Task<TechnicianDiagnosticStatsDto> GetTechnicianStatsAsync();
    Task<DiagnosticOrderDto> StartOrderAsync(long orderId, TransitionDiagnosticOrderRequest? request);
    Task<DiagnosticOrderDto> RecordItemResultAsync(long orderId, long itemId, RecordDiagnosticResultRequest request);
    Task<DiagnosticOrderDto> CompleteOrderAsync(long orderId, TransitionDiagnosticOrderRequest? request);

    // Patient operations
    Task<PagedResult<DiagnosticOrderDto>> GetPatientOrdersAsync(int page, int pageSize);
    Task<DiagnosticOrderDto> GetPatientOrderByIdAsync(long orderId);
}