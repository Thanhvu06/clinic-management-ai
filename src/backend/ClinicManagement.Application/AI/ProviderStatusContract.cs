namespace ClinicManagement.Application.AI;

/// <summary>
/// Truthful provider state shown to clients. NotCalled means this turn was
/// handled locally and is not evidence that Gemini is online.
/// </summary>
public static class AiProviderStatusContract
{
    public const string NotCalled = "NotCalled";
    public const string Online = "Online";
    public const string Degraded = "Degraded";
    public const string Unavailable = "Unavailable";
    public const string SafetyBlocked = "SafetyBlocked";

    public static string FromProviderResult(string? providerStatus, bool called)
    {
        if (!called) return NotCalled;
        return providerStatus?.Trim() switch
        {
            "Success" or "Healthy" => Online,
            "Disabled" => Unavailable,
            _ => Degraded
        };
    }
}
