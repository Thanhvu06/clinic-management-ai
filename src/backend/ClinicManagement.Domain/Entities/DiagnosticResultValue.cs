using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class DiagnosticResultValue
{
    public long Id { get; set; }
    public long DiagnosticResultId { get; set; }
    public long DiagnosticServiceParameterId { get; set; }
    public string ParameterCode { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal? RefLow { get; set; }
    public decimal? RefHigh { get; set; }
    public decimal? CriticalLow { get; set; }
    public decimal? CriticalHigh { get; set; }
    public decimal NumericValue { get; set; }
    public DiagnosticResultFlag Flag { get; set; }
    public decimal? PreviousNumericValue { get; set; }
    public DateTime? PreviousResultedAtUtc { get; set; }
    public string? PreviousOrderCode { get; set; }
    public decimal? DeltaChange { get; set; }
    public decimal? DeltaPercent { get; set; }
    public bool IsDeltaFailure { get; set; }
    public int SortOrder { get; set; }
    public DiagnosticResult DiagnosticResult { get; set; } = null!;
    public DiagnosticServiceParameter DiagnosticServiceParameter { get; set; } = null!;
}
