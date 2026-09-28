using ClinicManagement.Application.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>
/// Resolves effective provider metadata without exposing secret values. The
/// last configuration provider that contains a key wins, matching .NET's
/// configuration precedence. Missing keys are reported as option defaults.
/// </summary>
public sealed class AiProviderConfigurationInspector : IAiProviderConfigurationInspector
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<AiProviderOptions> _options;
    private readonly TimeProvider _timeProvider;

    public AiProviderConfigurationInspector(
        IConfiguration configuration,
        IOptions<AiProviderOptions> options,
        TimeProvider? timeProvider = null)
    {
        _configuration = configuration;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AiProviderConfigurationSnapshot GetSnapshot()
    {
        var options = _options.Value;
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(AiProviderOptions.IsEnabled)] = SourceFor("IsEnabled"),
            [nameof(AiProviderOptions.ProviderName)] = SourceFor("ProviderName"),
            [nameof(AiProviderOptions.ModelName)] = SourceFor("ModelName"),
            [nameof(AiProviderOptions.ProviderUrl)] = SourceFor("ProviderUrl"),
            [nameof(AiProviderOptions.TimeoutSeconds)] = SourceFor("TimeoutSeconds"),
            [nameof(AiProviderOptions.MaxAttempts)] = SourceFor("MaxAttempts"),
            [nameof(AiProviderOptions.RetryBaseDelayMilliseconds)] = SourceFor("RetryBaseDelayMilliseconds"),
            [nameof(AiProviderOptions.CircuitFailureThreshold)] = SourceFor("CircuitFailureThreshold"),
            [nameof(AiProviderOptions.CircuitCooldownSeconds)] = SourceFor("CircuitCooldownSeconds"),
            ["ApiKey"] = SourceFor("ApiKey")
        };

        return new AiProviderConfigurationSnapshot
        {
            IsEnabled = options.IsEnabled,
            ProviderName = Limit(options.ProviderName, 64),
            ModelName = Limit(options.ModelName, 128),
            ProviderBaseUrl = SafeBaseUrl(options.ProviderUrl),
            TimeoutSeconds = Math.Max(1, options.TimeoutSeconds),
            MaxAttempts = Math.Clamp(options.MaxAttempts, 1, 3),
            RetryBaseDelayMilliseconds = Math.Clamp(options.RetryBaseDelayMilliseconds, 0, 5000),
            CircuitFailureThreshold = Math.Max(1, options.CircuitFailureThreshold),
            CircuitCooldownSeconds = Math.Max(1, options.CircuitCooldownSeconds),
            KeyConfigured = !string.IsNullOrWhiteSpace(options.ApiKey),
            KeyConfigurationSource = sources["ApiKey"],
            ConfigurationSources = sources,
            RetrievedAtUtc = _timeProvider.GetUtcNow()
        };
    }

    private string SourceFor(string propertyName)
    {
        var key = $"{AiProviderOptions.SectionName}:{propertyName}";
        if (_configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                if (provider.TryGet(key, out _))
                    return ProviderSourceName(provider);
            }
        }

        return "AiProviderOptions default";
    }

    private static string ProviderSourceName(IConfigurationProvider provider)
    {
        if (provider is FileConfigurationProvider fileProvider && !string.IsNullOrWhiteSpace(fileProvider.Source.Path))
            return $"{provider.GetType().Name}({Path.GetFileName(fileProvider.Source.Path)})";

        return provider.GetType().Name;
    }

    private static string SafeBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host))
            return "invalid";

        return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}";
    }

    private static string Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
