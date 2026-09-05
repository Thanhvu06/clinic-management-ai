using System;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.HealthPackages.DTOs;

public class CreatePackageRegistrationRequest
{
    [Required(ErrorMessage = "Vui lòng chọn gói khám.")]
    public long HealthPackageId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày mong muốn khám.")]
    public DateOnly PreferredDate { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại liên hệ.")]
    [RegularExpression(@"^(0|\+84)[3|5|7|8|9][0-9]{8}$", ErrorMessage = "Số điện thoại không hợp lệ (định dạng Việt Nam 10 chữ số).")]
    public string ContactPhone { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? Note { get; set; }
}

public class HealthPackageRegistrationDto
{
    public long Id { get; set; }
    public string RegistrationCode { get; set; } = string.Empty;
    public long HealthPackageId { get; set; }
    public string HealthPackageCode { get; set; } = string.Empty;
    public string HealthPackageName { get; set; } = string.Empty;
    public decimal HealthPackagePrice { get; set; }
    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public DateOnly PreferredDate { get; set; }
    public string ContactPhone { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
