namespace ClinicManagement.Application.AI;

public static class AiAssistantModes
{
    public const string Ready = "Ready";
    public const string Clarifying = "Clarifying";
    public const string Processing = "Processing";
    public const string Degraded = "Degraded";
    public const string Unavailable = "Unavailable";
    public const string SafetyBlocked = "SafetyBlocked";
}

public static class AiPlannerModes
{
    public const string Deterministic = "Deterministic";
    public const string LocalClassifier = "LocalClassifier";
    public const string Gemini = "Gemini";
    public const string Fallback = "Fallback";
    public const string Safety = "Safety";
}
