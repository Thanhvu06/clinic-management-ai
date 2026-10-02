using System.Text.Json;
using ClinicManagement.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class AiProviderConfigurationInspectorTests
{
    [Fact]
    public void Missing_keys_report_option_defaults_without_inventing_an_override()
    {
        var configuration = new ConfigurationBuilder().Build();
        var inspector = new AiProviderConfigurationInspector(
            configuration,
            Options.Create(new AiProviderOptions()));

        var snapshot = inspector.GetSnapshot();

        Assert.False(snapshot.IsEnabled);
        Assert.Equal("Gemini", snapshot.ProviderName);
        Assert.Equal("gemini-3.6-flash", snapshot.ModelName);
        Assert.Equal(10, snapshot.TimeoutSeconds);
        Assert.Equal(3, snapshot.MaxAttempts);
        Assert.Equal(250, snapshot.RetryBaseDelayMilliseconds);
        Assert.Equal(3, snapshot.CircuitFailureThreshold);
        Assert.Equal(30, snapshot.CircuitCooldownSeconds);
        Assert.False(snapshot.KeyConfigured);
        Assert.Equal("AiProviderOptions default", snapshot.ConfigurationSources["ModelName"]);
        Assert.Equal("AiProviderOptions default", snapshot.KeyConfigurationSource);
    }

    [Fact]
    public void Winning_provider_is_reported_and_secret_and_url_path_are_redacted()
    {
        const string secret = "synthetic-provider-secret-that-must-not-escape";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProvider:IsEnabled"] = "true",
                ["AiProvider:ProviderName"] = "Gemini",
                ["AiProvider:ModelName"] = "synthetic-model",
                ["AiProvider:ProviderUrl"] = "https://provider.example.test/private/path?key=should-not-be-returned",
                ["AiProvider:TimeoutSeconds"] = "7",
                ["AiProvider:MaxAttempts"] = "2",
                ["AiProvider:RetryBaseDelayMilliseconds"] = "100",
                ["AiProvider:CircuitFailureThreshold"] = "4",
                ["AiProvider:CircuitCooldownSeconds"] = "45",
                ["AiProvider:ApiKey"] = secret
            })
            .Build();
        var inspector = new AiProviderConfigurationInspector(
            configuration,
            Options.Create(new AiProviderOptions
            {
                IsEnabled = true,
                ProviderName = "Gemini",
                ModelName = "synthetic-model",
                ProviderUrl = "https://provider.example.test/private/path?key=should-not-be-returned",
                TimeoutSeconds = 7,
                MaxAttempts = 2,
                RetryBaseDelayMilliseconds = 100,
                CircuitFailureThreshold = 4,
                CircuitCooldownSeconds = 45,
                ApiKey = secret
            }));

        var snapshot = inspector.GetSnapshot();
        var serialized = JsonSerializer.Serialize(snapshot);

        Assert.True(snapshot.KeyConfigured);
        Assert.Equal("https://provider.example.test", snapshot.ProviderBaseUrl);
        Assert.Contains("MemoryConfigurationProvider", snapshot.ConfigurationSources["ModelName"]);
        Assert.Contains("MemoryConfigurationProvider", snapshot.KeyConfigurationSource);
        Assert.DoesNotContain(secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("private/path", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-be-returned", serialized, StringComparison.Ordinal);
    }
}
