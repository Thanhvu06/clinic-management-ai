using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class DiagnosticService
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DiagnosticCategory Category { get; set; }
    public string? PreparationInstructions { get; set; }
    public decimal? Price { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SpecimenType { get; set; }
    public string? ContainerType { get; set; }
    public ICollection<DiagnosticServiceParameter> Parameters { get; set; } = new List<DiagnosticServiceParameter>();
}
