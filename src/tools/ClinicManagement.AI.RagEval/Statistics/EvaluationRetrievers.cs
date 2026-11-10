using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.AI.RagEval;

public sealed class DenseEvaluationRetriever(IReadOnlyList<KnowledgeChunk> chunks, float[][] vectors,
    OllamaKnowledgeClient ollama, OllamaOptions options) : IClinicKnowledgeRetriever
{
    public async Task<KnowledgeRetrieval> RetrieveAsync(string question, CancellationToken ct = default)
    {
        var masked = KnowledgePrivacyMask.Mask(question);
        if (string.IsNullOrWhiteSpace(masked)) return new([]);
        var query = await ollama.EmbedAsync([masked], ct);
        if (query.Value is not { Length: 1 } || vectors.Length != chunks.Count || vectors.Length == 0 ||
            query.Value[0].Length != vectors[0].Length || query.Value[0].Any(v => !float.IsFinite(v)) || query.Value[0].All(v => v == 0))
            throw new InvalidOperationException("Dense query embedding failed.");
        var ranked = chunks.Select((chunk, index) => new KnowledgeHit(chunk, Bm25KnowledgeIndex.Cosine(query.Value[0], vectors[index])))
            .Where(hit => hit.Score >= Math.Clamp(options.MinCosine, 0, 1))
            .OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Chunk.Id, StringComparer.Ordinal)
            .Take(Math.Clamp(options.TopK, 1, 10)).ToArray();
        return new(ranked, query.WasCalled);
    }
}

public sealed class EvaluationRetrievers : IDisposable
{
    private readonly HttpClient _http;
    private readonly ClinicKnowledgeRetriever _bm25;
    private readonly ClinicKnowledgeRetriever _hybrid;
    public IReadOnlyDictionary<string, IClinicKnowledgeRetriever> Modes { get; }
    public int Documents { get; }
    public int Chunks { get; }
    public OllamaOptions Options { get; }

    private EvaluationRetrievers(HttpClient http, ClinicKnowledgeRetriever bm25, ClinicKnowledgeRetriever hybrid,
        DenseEvaluationRetriever dense, MarkdownKnowledgeCatalog catalog, OllamaOptions options)
    {
        _http = http; _bm25 = bm25; _hybrid = hybrid;
        Modes = new Dictionary<string, IClinicKnowledgeRetriever> { ["bm25"] = bm25, ["dense"] = dense, ["hybrid"] = hybrid };
        Documents = catalog.Chunks.Select(c => c.Source.DocId).Distinct().Count();
        Chunks = catalog.Chunks.Count;
        Options = options;
    }

    public static async Task<EvaluationRetrievers> CreateAsync(HttpMessageHandler? handler = null,
        string? cacheRoot = null, CancellationToken ct = default)
    {
        var http = new HttpClient(handler ?? new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
            { Timeout = Timeout.InfiniteTimeSpan };
        var catalog = new MarkdownKnowledgeCatalog();
        var enabled = new OllamaOptions { IsEnabled = true };
        var ollama = new OllamaKnowledgeClient(http, Microsoft.Extensions.Options.Options.Create(enabled));
        var environment = new EvaluationEnvironment { ContentRootPath = cacheRoot ?? AppContext.BaseDirectory };
        var hybrid = new ClinicKnowledgeRetriever(catalog, ollama, Microsoft.Extensions.Options.Options.Create(enabled), environment,
            NullLogger<ClinicKnowledgeRetriever>.Instance);
        var disabled = Microsoft.Extensions.Options.Options.Create(new OllamaOptions());
        var bm25 = new ClinicKnowledgeRetriever(catalog, new OllamaKnowledgeClient(http, disabled), disabled, environment,
            NullLogger<ClinicKnowledgeRetriever>.Instance);
        try
        {
            await hybrid.BuildEmbeddingsAsync(ct);
            if (!hybrid.EmbeddingsReady) throw new InvalidOperationException("Evaluation requires a complete bge-m3 index.");
            // The runtime owns index construction; read its public-document cache without changing its private state or behavior.
            var content = catalog.Chunks.Select(c => c.Source.Title + " — " + c.Source.Heading + "\n" + c.Text);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("rag-v1|" + enabled.EmbeddingModel + "|" + string.Join('\n', content))));
            var path = Path.Combine(environment.ContentRootPath, "App_Data", "rag", hash + ".json");
            var vectors = JsonSerializer.Deserialize<float[][]>(await File.ReadAllTextAsync(path, ct));
            if (vectors is null || vectors.Length != catalog.Chunks.Count || vectors.Length == 0 ||
                vectors.Any(v => v is null || v.Length != vectors[0].Length || v.Length == 0 || v.Any(x => !float.IsFinite(x)) || v.All(x => x == 0)))
                throw new InvalidDataException("Invalid shared embedding cache.");
            return new(http, bm25, hybrid, new DenseEvaluationRetriever(catalog.Chunks, vectors, ollama, enabled), catalog, enabled);
        }
        catch { bm25.Dispose(); hybrid.Dispose(); http.Dispose(); throw; }
    }

    public void Dispose() { _bm25.Dispose(); _hybrid.Dispose(); _http.Dispose(); }
}
