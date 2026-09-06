using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Billing.Interfaces;

public interface IBillingService
{
    // Receptionist operations
    Task<PagedResult<InvoiceDto>> GetReceptionInvoicesAsync(InvoiceFilterParams filters, CancellationToken cancellationToken = default);
    Task<InvoiceDetailDto> GetInvoiceDetailAsync(long invoiceId, CancellationToken cancellationToken = default);
    Task<InvoiceDetailDto> CreateInvoiceFromAppointmentAsync(long appointmentId, Guid createdByUserId, CancellationToken cancellationToken = default);
    Task<InvoiceDetailDto> CreateInvoiceFromHealthPackageAsync(long registrationId, Guid createdByUserId, CancellationToken cancellationToken = default);
    Task<PaymentDto> ProcessPaymentAsync(long invoiceId, ProcessPaymentRequest request, Guid receivedByUserId, CancellationToken cancellationToken = default);
    Task<InvoiceDetailDto> CancelInvoiceAsync(long invoiceId, string reason, Guid cancelledByUserId, CancellationToken cancellationToken = default);
    Task<BillingKpiDto> GetTodayKpiAsync(CancellationToken cancellationToken = default);

    // Patient operations
    Task<PagedResult<InvoiceDto>> GetPatientInvoicesAsync(long patientId, int page, int pageSize, InvoiceStatus? status, CancellationToken cancellationToken = default);
    Task<InvoiceDetailDto> GetPatientInvoiceDetailAsync(long invoiceId, long patientId, CancellationToken cancellationToken = default);

    // Admin operations
    Task<RevenueReportDto> GetRevenueReportAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);
    Task<List<SpecialtyFeeDto>> GetSpecialtyFeesAsync(CancellationToken cancellationToken = default);
    Task<SpecialtyFeeDto> UpdateSpecialtyFeeAsync(long specialtyId, decimal fee, Guid updatedByUserId, CancellationToken cancellationToken = default);
}
