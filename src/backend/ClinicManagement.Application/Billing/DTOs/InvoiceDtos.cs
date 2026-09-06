using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Billing.DTOs;

public class InvoiceDto
{
    public long Id { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public InvoiceSourceType SourceType { get; set; }
    public string SourceTypeName { get; set; } = string.Empty;
    public long? AppointmentId { get; set; }
    public string? AppointmentCode { get; set; }
    public long? HealthPackageRegistrationId { get; set; }
    public string? RegistrationCode { get; set; }
    public InvoiceStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public string? CreatedByUserName { get; set; }
    public string? PaidByUserName { get; set; }
}

public class InvoiceItemDto
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }
}

public class InvoiceDetailDto : InvoiceDto
{
    public List<InvoiceItemDto> Items { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();
}

public class CreateAppointmentInvoiceRequest
{
    [Required(ErrorMessage = "Vui lòng cung cấp AppointmentId.")]
    public long AppointmentId { get; set; }
}

public class CreatePackageInvoiceRequest
{
    [Required(ErrorMessage = "Vui lòng cung cấp HealthPackageRegistrationId.")]
    public long HealthPackageRegistrationId { get; set; }
}

public class CancelInvoiceRequest
{
    [Required(ErrorMessage = "Vui lòng nhập lý do hủy hóa đơn.")]
    [MaxLength(500, ErrorMessage = "Lý do hủy không được vượt quá 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

public class InvoiceFilterParams
{
    public string? Search { get; set; }
    public InvoiceStatus? Status { get; set; }
    public InvoiceSourceType? SourceType { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
