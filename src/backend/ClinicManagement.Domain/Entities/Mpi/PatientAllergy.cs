using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class PatientAllergy
{
    public long Id { get; set; }
    public long PatientId { get; set; }
    public AllergenType AllergenType { get; set; } = AllergenType.Drug;
    public string AllergenName { get; set; } = string.Empty;
    public AllergySeverity Severity { get; set; } = AllergySeverity.Moderate;
    public string? ReactionDescription { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    public Patient Patient { get; set; } = null!;
}
