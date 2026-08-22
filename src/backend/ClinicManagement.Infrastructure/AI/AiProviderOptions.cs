namespace ClinicManagement.Infrastructure.AI;

public class AiProviderOptions
{
    public const string SectionName = "AiProvider";
    
    public bool IsEnabled { get; set; } = false;
    public string ProviderUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}
