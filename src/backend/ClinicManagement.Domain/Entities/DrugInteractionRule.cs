using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class DrugInteractionRule
{
    public long Id { get; set; }
    public string IngredientKeyA { get; set; } = string.Empty;
    public string IngredientKeyB { get; set; } = string.Empty;
    public DrugInteractionSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
