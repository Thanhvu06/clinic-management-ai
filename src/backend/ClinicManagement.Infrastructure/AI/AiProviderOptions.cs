namespace ClinicManagement.Infrastructure.AI;

public class AiProviderOptions
{
    public const string SectionName = "AiProvider";
    
    public bool IsEnabled { get; set; } = false;
    public string ProviderUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "gemini-3.6-flash";
    public int TimeoutSeconds { get; set; } = 10;
}
