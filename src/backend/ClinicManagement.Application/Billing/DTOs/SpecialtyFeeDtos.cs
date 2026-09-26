using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Billing.DTOs;

public class SpecialtyFeeDto
{
    public long Id { get; set; }
    public string SpecialtyCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ConsultationFee { get; set; }
    public bool IsActive { get; set; }
}

public class UpdateSpecialtyFeeRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mức phí khám.")]
    [Range(0, 100000000, ErrorMessage = "Mức phí khám không được âm.")]
    public decimal ConsultationFee { get; set; }
}
