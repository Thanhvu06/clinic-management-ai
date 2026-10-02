namespace ClinicManagement.Application.AI;

/// <summary>
/// A safe, provider-agnostic view of the effective AI configuration. It must
/// never expose credential values, authorization headers, or connection data.
/// </summary>
public interface IAiProviderConfigurationInspector
{
    AiProviderConfigurationSnapshot GetSnapshot();
}

public sealed class AiProviderConfigurationSnapshot
{
    public bool IsEnabled { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string ProviderBaseUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public int MaxAttempts { get; init; }
    public int RetryBaseDelayMilliseconds { get; init; }
    public int CircuitFailureThreshold { get; init; }
    public int CircuitCooldownSeconds { get; init; }
    public bool KeyConfigured { get; init; }
    public string KeyConfigurationSource { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> ConfigurationSources { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset RetrievedAtUtc { get; init; }
}
