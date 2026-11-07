namespace ClinicManagement.Infrastructure.AI.Knowledge;

public sealed class OllamaOptions
{
    public const string SectionName = "Rag";
    public bool IsEnabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen2.5:7b";
    public string EmbeddingModel { get; set; } = "bge-m3";
    public int TimeoutSeconds { get; set; } = 45;
    public double MinScore { get; set; } = 0.35;
    public int TopK { get; set; } = 4;

    public Uri LocalEndpoint(string path)
    {
        var uri = new Uri(BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/'));
        if (!uri.IsLoopback || uri.Scheme != "http" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Rag:BaseUrl must be a local HTTP loopback address.");
        // Ollama on Windows commonly binds IPv4 only; avoid the localhost IPv6 connection delay.
        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? new UriBuilder(uri) { Host = "127.0.0.1" }.Uri : uri;
    }
}
