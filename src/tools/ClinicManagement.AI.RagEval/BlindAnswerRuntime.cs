using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
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
    private readonly ClinicKnowledgeRetriever _retriever;
    private readonly BlindAnswerRetrieval _sharedRetrieval;
    private readonly AppDbContext _db;
    private readonly AiSpecialtyService _llm;
    private readonly AiSpecialtyService _extractive;
    private readonly string _cacheRoot;
    public string Retrieval { get; }
    public string ChatModel { get; } = new OllamaOptions().ChatModel;

    private BlindAnswerRuntime(HttpClient http, ClinicKnowledgeRetriever retriever,
        BlindAnswerRetrieval sharedRetrieval, AppDbContext db,
        AiSpecialtyService llm, AiSpecialtyService extractive, string cacheRoot, string retrieval)
    {
        _http = http;
        _retriever = retriever;
        _sharedRetrieval = sharedRetrieval;
        _db = db;
        _llm = llm;
        _extractive = extractive;
        _cacheRoot = cacheRoot;
        Retrieval = retrieval;
    }

    public static async Task<BlindAnswerRuntime> CreateAsync(HttpMessageHandler? handler = null, CancellationToken ct = default,
        string retrieval = "hybrid", IClinicKnowledgeRetriever? retriever = null)
    {
        if (retrieval is not ("hybrid" or "bm25"))
            throw new ArgumentException("--retrieval must be hybrid or bm25.");
        var http = new HttpClient(handler ?? new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
            { Timeout = Timeout.InfiniteTimeSpan };
        var enabled = Options.Create(new OllamaOptions { IsEnabled = true });
        var disabled = Options.Create(new OllamaOptions { IsEnabled = false });
        var llmClient = new OllamaKnowledgeClient(http, enabled);
        var extractiveClient = new OllamaKnowledgeClient(http, disabled);
        var retrievalOptions = Options.Create(new OllamaOptions { IsEnabled = retrieval == "hybrid" });
        var retrievalClient = new OllamaKnowledgeClient(http, retrievalOptions);
        var catalog = new MarkdownKnowledgeCatalog();
        var cacheRoot = Path.Combine(Path.GetTempPath(), "rag-blind-cache-" + Guid.NewGuid().ToString("N"));
        var environment = new EvaluationEnvironment { ContentRootPath = cacheRoot };
        var knowledgeRetriever = new ClinicKnowledgeRetriever(catalog, retrievalClient, retrievalOptions, environment,
            NullLogger<ClinicKnowledgeRetriever>.Instance);
        var sharedRetrieval = new BlindAnswerRetrieval(retriever ?? knowledgeRetriever);
        // Keep evaluation sessions and audit entries isolated from patient data and persistent logs.
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            await db.Database.EnsureCreatedAsync(ct);
            if (retriever is null && retrieval == "hybrid")
            {
                await knowledgeRetriever.BuildEmbeddingsAsync(ct);
                if (!knowledgeRetriever.EmbeddingsReady)
                    throw new InvalidOperationException("Hybrid retrieval requires local Ollama with bge-m3.");
            }
            if ((await llmClient.WarmChatModelAsync(ct)).Value is null)
                throw new InvalidOperationException("blind-answers requires local Ollama with qwen2.5:7b.");
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
            return new(http, knowledgeRetriever, sharedRetrieval, db,
                Service(new ClinicKnowledgeAnswerer(sharedRetrieval, llmClient)),
                Service(new ClinicKnowledgeAnswerer(sharedRetrieval, extractiveClient)), cacheRoot, retrieval);
        }
        catch
        {
            db.Dispose();
            knowledgeRetriever.Dispose();
            http.Dispose();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
            throw;
        }
    }

    public Task<AiChatResponseDto> AnswerAsync(string question, bool useLlm, CancellationToken ct = default)
    {
        _sharedRetrieval.BeginAnswer(question, useLlm);
        return (useLlm ? _llm : _extractive).ChatAsync(new AiChatRequestDto { Message = question }, ct);
    }

    public void Dispose()
    {
        _db.Dispose();
        _retriever.Dispose();
        _http.Dispose();
        if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true);
    }
}
