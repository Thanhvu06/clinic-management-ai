using ClinicManagement.Application.AI.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public static class KnowledgeRegistration
{
    public static IServiceCollection AddClinicKnowledge(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OllamaOptions>().Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(options => options.TimeoutSeconds is >= 1 and <= 120 && options.TopK is >= 1 and <= 10 &&
                options.MinScore is > 0 and <= 1 && Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                uri.IsLoopback && uri.Scheme == "http" && string.IsNullOrEmpty(uri.UserInfo), "Invalid local RAG configuration.")
            .ValidateOnStart();
        services.AddSingleton<MarkdownKnowledgeCatalog>();
        services.AddHttpClient("ClinicKnowledge", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) });
        services.AddSingleton(sp => new OllamaKnowledgeClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("ClinicKnowledge"),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>()));
        services.AddSingleton<ClinicKnowledgeRetriever>();
        services.AddSingleton<IClinicKnowledgeRetriever>(sp => sp.GetRequiredService<ClinicKnowledgeRetriever>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ClinicKnowledgeRetriever>());
        services.AddSingleton<IClinicKnowledgeAnswerer, ClinicKnowledgeAnswerer>();
        return services;
    }
}
