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
    public const string Disabled = "Disabled";
    public const string SafetyBlocked = "SafetyBlocked";

    public const string ExecutionProviderAssisted = "ProviderAssisted";
    public const string ExecutionDeterministicFallback = "DeterministicFallback";
    public const string ExecutionManualHandoff = "ManualHandoff";

    public const string FailureNone = "None";
    public const string FailureRateLimited = "RateLimited";
    public const string FailureTimeout = "Timeout";
    public const string FailureNetworkError = "NetworkError";
    public const string FailureServerError = "ServerError";
    public const string FailureAuthenticationFailed = "AuthenticationFailed";
    public const string FailureModelUnavailable = "ModelUnavailable";
    public const string FailureInvalidResponse = "InvalidResponse";
    public const string FailureSafetyBlocked = "SafetyBlocked";
    public const string FailureCircuitOpen = "CircuitOpen";
    public const string FailureClientCancelled = "ClientCancelled";
    public const string FailureConfigurationDisabled = "ConfigurationDisabled";
    public const string FailureAttemptBudgetExceeded = "AttemptBudgetExceeded";
    public const string FailureUnknown = "UnknownProviderFailure";

    public static string FromProviderResult(string? providerStatus, bool called)
    {
        if (!called) return NotCalled;
        return providerStatus?.Trim() switch
        {
            "Success" or "Healthy" => Online,
            "Disabled" => Disabled,
            "AuthFailure" or "InvalidModelOrEndpoint" or "ProviderCircuitOpen" => Unavailable,
            "Cancelled" or "ClientCancelled" => NotCalled,
            _ => Degraded
        };
    }

    public static string FailureCodeFromProviderStatus(string? providerStatus) => providerStatus?.Trim() switch
    {
        null or "" or "Success" or "Healthy" => FailureNone,
        "RateLimited" => FailureRateLimited,
        "Timeout" => FailureTimeout,
        "NetworkError" => FailureNetworkError,
        "ProviderServerError" or "ServerError" => FailureServerError,
        "AuthFailure" or "AuthenticationFailed" => FailureAuthenticationFailed,
        "InvalidModelOrEndpoint" or "ModelUnavailable" => FailureModelUnavailable,
        "InvalidResponse" => FailureInvalidResponse,
        "SafetyBlocked" => FailureSafetyBlocked,
        "ProviderCircuitOpen" or "CircuitOpen" => FailureCircuitOpen,
        "Cancelled" or "ClientCancelled" => FailureClientCancelled,
        "Disabled" or "ConfigurationDisabled" => FailureConfigurationDisabled,
        "AttemptBudgetExceeded" => FailureAttemptBudgetExceeded,
        _ => FailureUnknown
    };

    /// <summary>
    /// Only provider reliability failures count toward the shared circuit. A
    /// caller cancellation or a configuration/authentication error must not
    /// make healthy traffic unavailable to other users.
    /// </summary>
    public static bool IsCircuitFailure(string? providerStatus) => providerStatus?.Trim() switch
    {
        "Timeout" or "NetworkError" or "RateLimited" or "ProviderServerError" or "ServerError" or
        FailureTimeout or FailureNetworkError or FailureRateLimited or FailureServerError => true,
        _ => false
    };

    public static bool IsClientCancellation(string? providerStatus) =>
        string.Equals(providerStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(providerStatus, "ClientCancelled", StringComparison.OrdinalIgnoreCase);
}
