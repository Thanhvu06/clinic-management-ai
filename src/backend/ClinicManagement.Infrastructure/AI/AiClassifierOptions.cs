namespace ClinicManagement.Infrastructure.AI;

public class AiClassifierOptions
{
    public const string SectionName = "AiClassifier";

    public bool Enabled { get; set; } = false;
    public string? ModelPath { get; set; }
    public float MinimumConfidence { get; set; } = 0.5f;
    public bool RequireClinicallyValidated { get; set; } = true;
}
