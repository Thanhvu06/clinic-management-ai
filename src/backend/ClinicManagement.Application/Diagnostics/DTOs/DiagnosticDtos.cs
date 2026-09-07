using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.Diagnostics.DTOs;

public class DiagnosticServiceDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? PreparationInstructions { get; set; }
    public bool IsActive { get; set; }
}

public class DiagnosticResultDto
{
    public long Id { get; set; }
    public long DiagnosticOrderItemId { get; set; }
    public string ResultText { get; set; } = string.Empty;
    public string? Conclusion { get; set; }
    public string? ReferenceRange { get; set; }
    public string? Unit { get; set; }
    public DateTime ResultedAtUtc { get; set; }
    public Guid ResultedByUserId { get; set; }
    public string ResultedByUserName { get; set; } = string.Empty;
    public string? RowVersion { get; set; }
}

public class DiagnosticOrderItemDto
{
    public long Id { get; set; }
    public long DiagnosticOrderId { get; set; }
    public long DiagnosticServiceId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceCategory { get; set; } = string.Empty;
    public string? PreparationInstructions { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RowVersion { get; set; }
    public DiagnosticResultDto? Result { get; set; }
}

public class DiagnosticOrderDto
{
    public long Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public long AppointmentId { get; set; }
    public string AppointmentCode { get; set; } = string.Empty;
    public DateOnly AppointmentDate { get; set; }

    public long PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public DateOnly? PatientDob { get; set; }
    public int? PatientAge { get; set; }

    public long OrderingDoctorId { get; set; }
    public string OrderingDoctorName { get; set; } = string.Empty;
    public string OrderingDoctorSpecialty { get; set; } = string.Empty;

    public string ClinicalIndication { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;

    public DateTime OrderedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public string? StartedByUserName { get; set; }
    public string? CompletedByUserName { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByDoctorName { get; set; }

    public string? RowVersion { get; set; }
    public List<DiagnosticOrderItemDto> Items { get; set; } = new();
}

public class CreateDiagnosticOrderRequest
{
    [Required(ErrorMessage = "Chỉ định lâm sàng không được để trống.")]
    [MaxLength(1000)]
    public string ClinicalIndication { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Note { get; set; }

    [Required(ErrorMessage = "Cần chọn ít nhất một dịch vụ cận lâm sàng.")]
    [MinLength(1, ErrorMessage = "Cần chọn ít nhất một dịch vụ cận lâm sàng.")]
    public List<long> ServiceIds { get; set; } = new();
}

public class RecordDiagnosticResultRequest
{
    [Required(ErrorMessage = "Kết quả xét nghiệm không được để trống.")]
    [MaxLength(4000)]
    public string ResultText { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Conclusion { get; set; }

    [MaxLength(200)]
    public string? ReferenceRange { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }

    public string? RowVersion { get; set; }
}

public class TransitionDiagnosticOrderRequest
{
    public string? RowVersion { get; set; }
}

public class CancelDiagnosticOrderRequest
{
    [MaxLength(500)]
    public string? Reason { get; set; }
    public string? RowVersion { get; set; }
}

public class TechnicianDiagnosticStatsDto
{
    public int OrderedCount { get; set; }
    public int InProgressCount { get; set; }
    public int CompletedTodayCount { get; set; }
}