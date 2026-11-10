using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Authentication;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.AI.RagEval;

public sealed class BlindAnswerRuntime : IDisposable
{
    private readonly HttpClient _http;
    private readonly ClinicKnowledgeRetriever _llmRetriever;
    private readonly ClinicKnowledgeRetriever _extractiveRetriever;
    private readonly AppDbContext _db;
    private readonly AiSpecialtyService _llm;
    private readonly AiSpecialtyService _extractive;
    private readonly string _cacheRoot;

    private BlindAnswerRuntime(HttpClient http, ClinicKnowledgeRetriever llmRetriever,
        ClinicKnowledgeRetriever extractiveRetriever, AppDbContext db,
        AiSpecialtyService llm, AiSpecialtyService extractive, string cacheRoot)
    {
        _http = http;
        _llmRetriever = llmRetriever;
        _extractiveRetriever = extractiveRetriever;
        _db = db;
        _llm = llm;
        _extractive = extractive;
        _cacheRoot = cacheRoot;
    }

    public static async Task<BlindAnswerRuntime> CreateAsync(HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        var http = new HttpClient(handler ?? new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
            { Timeout = Timeout.InfiniteTimeSpan };
        var enabled = Options.Create(new OllamaOptions { IsEnabled = true });
        var disabled = Options.Create(new OllamaOptions { IsEnabled = false });
        var llmClient = new OllamaKnowledgeClient(http, enabled);
        var extractiveClient = new OllamaKnowledgeClient(http, disabled);
        var catalog = new MarkdownKnowledgeCatalog();
        var cacheRoot = Path.Combine(Path.GetTempPath(), "rag-blind-cache-" + Guid.NewGuid().ToString("N"));
        var environment = new EvaluationEnvironment { ContentRootPath = cacheRoot };
        var llmRetriever = new ClinicKnowledgeRetriever(catalog, llmClient, enabled, environment,
            NullLogger<ClinicKnowledgeRetriever>.Instance);
        var extractiveRetriever = new ClinicKnowledgeRetriever(catalog, extractiveClient, disabled, environment,
            NullLogger<ClinicKnowledgeRetriever>.Instance);
        // Keep evaluation sessions and audit entries isolated from patient data and persistent logs.
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            await db.Database.EnsureCreatedAsync(ct);
            await llmRetriever.BuildEmbeddingsAsync(ct);
            if (!llmRetriever.EmbeddingsReady || (await llmClient.WarmChatModelAsync(ct)).Value is null)
                throw new InvalidOperationException("blind-answers requires local Ollama with qwen2.5:7b and bge-m3.");
            var clock = new DateTimeProvider();
            var provider = new GeminiAiProvider(http, Options.Create(new AiProviderOptions { IsEnabled = false }),
                NullLogger<GeminiAiProvider>.Instance);
            var classifier = new VietnameseIntentClassifier();
            var safety = new AiSafetyGuard();
            AiSpecialtyService Service(ClinicKnowledgeAnswerer answerer) => new(db, provider,
                new ClinicAiContextService(db, clock), clock, NullLogger<AiSpecialtyService>.Instance,
                new AppointmentAvailabilityPolicy(db, clock), new CurrentUserService(new HttpContextAccessor()),
                intentClassifier: classifier, safetyGuard: safety,
                conversationPipeline: new AiConversationPipeline(classifier, safety), knowledgeAnswerer: answerer);
            return new(http, llmRetriever, extractiveRetriever, db,
                Service(new ClinicKnowledgeAnswerer(llmRetriever, llmClient)),
                Service(new ClinicKnowledgeAnswerer(extractiveRetriever, extractiveClient)), cacheRoot);
        }
        catch
        {
            db.Dispose();
            llmRetriever.Dispose();
            extractiveRetriever.Dispose();
            http.Dispose();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
            throw;
        }
    }

    public Task<AiChatResponseDto> AnswerAsync(string question, bool useLlm, CancellationToken ct = default) =>
        (useLlm ? _llm : _extractive).ChatAsync(new AiChatRequestDto { Message = question }, ct);

    public void Dispose()
    {
        _db.Dispose();
        _llmRetriever.Dispose();
        _extractiveRetriever.Dispose();
        _http.Dispose();
        if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true);
    }
}
