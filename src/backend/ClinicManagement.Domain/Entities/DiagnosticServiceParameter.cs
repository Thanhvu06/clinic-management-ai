namespace ClinicManagement.Domain.Entities;

public class DiagnosticServiceParameter
{
    public long Id { get; set; }
    public long DiagnosticServiceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal? RefLow { get; set; }
    public decimal? RefHigh { get; set; }
    public decimal? RefLowFemale { get; set; }
    public decimal? RefHighFemale { get; set; }
    public decimal? CriticalLow { get; set; }
    public decimal? CriticalHigh { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DiagnosticService DiagnosticService { get; set; } = null!;
}
