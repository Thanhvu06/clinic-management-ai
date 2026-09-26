using System;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Billing.DTOs;

public class PaymentDto
{
    public long Id { get; set; }
    public string PaymentCode { get; set; } = string.Empty;
    public long InvoiceId { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string MethodName { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public string? Note { get; set; }
    public Guid ReceivedByUserId { get; set; }
    public string? ReceivedByUserName { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public PaymentStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
}

public class ProcessPaymentRequest
{
    [Required(ErrorMessage = "Vui lòng nhập số tiền thanh toán.")]
    [Range(0.01, 1000000000, ErrorMessage = "Số tiền thanh toán phải lớn hơn 0.")]
    public decimal Amount { get; set; }

    [EnumDataType(typeof(PaymentMethod), ErrorMessage = "Phương thức thanh toán không hợp lệ.")]
    public PaymentMethod Method { get; set; }

    [MaxLength(100, ErrorMessage = "Mã tham chiếu không được vượt quá 100 ký tự.")]
    public string? ReferenceCode { get; set; }

    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string? Note { get; set; }
}
