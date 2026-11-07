using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public sealed class ClinicKnowledgeRetriever(MarkdownKnowledgeCatalog catalog, OllamaKnowledgeClient ollama,
    IOptions<OllamaOptions> options, IHostEnvironment environment, ILogger<ClinicKnowledgeRetriever> logger)
    : BackgroundService, IClinicKnowledgeRetriever
{
    private readonly Bm25KnowledgeIndex _bm25 = new(catalog.Chunks);
    private float[][]? _embeddings;
    public bool EmbeddingsReady => Volatile.Read(ref _embeddings) is not null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        if (!options.Value.IsEnabled) return;
        while (!stoppingToken.IsCancellationRequested && !EmbeddingsReady)
        {
            try { await BuildEmbeddingsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { logger.LogWarning("RAG embedding cache unavailable; lexical retrieval remains available."); }
            if (!EmbeddingsReady) await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        if (EmbeddingsReady && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                var warmup = await ollama.WarmChatModelAsync(stoppingToken);
                if (warmup.Value is null) logger.LogWarning("RAG chat model warm-up failed; extractive answers remain available.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    public async Task BuildEmbeddingsAsync(CancellationToken ct = default)
    {
        if (!options.Value.IsEnabled || catalog.Chunks.Count == 0) return;
        var content = catalog.Chunks.Select(c => c.Source.Title + " — " + c.Source.Heading + "\n" + c.Text).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "rag-v1|" + options.Value.EmbeddingModel + "|" + string.Join('\n', content))));
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "rag");
        var path = Path.Combine(directory, hash + ".json");
        try
        {
            if (File.Exists(path))
            {
                var cached = JsonSerializer.Deserialize<float[][]>(await File.ReadAllTextAsync(path, ct));
                if (ValidVectors(cached, content.Length)) { Volatile.Write(ref _embeddings, cached); return; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { logger.LogWarning("RAG cache could not be read; rebuilding public document embeddings."); }
        if (!await ollama.IsAvailableAsync(ct)) return;
        var vectors = new List<float[]>();
        foreach (var batch in content.Chunk(2))
        {
            var result = await ollama.EmbedAsync(batch, ct);
            if (!ValidVectors(result.Value, batch.Length)) return;
            vectors.AddRange(result.Value!);
        }
        if (!ValidVectors(vectors.ToArray(), content.Length)) return;
        Volatile.Write(ref _embeddings, vectors.ToArray());
        try
        {
            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(vectors), ct);
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { logger.LogWarning("RAG cache could not be saved; using the in-memory index."); }
    }

    public async Task<KnowledgeRetrieval> RetrieveAsync(string question, CancellationToken ct = default)
    {
        var maskedQuestion = KnowledgePrivacyMask.Mask(question);
        if (string.IsNullOrWhiteSpace(maskedQuestion)) return new([]);
        var lexical = _bm25.Search(maskedQuestion);
        var min = Math.Clamp(options.Value.MinScore, 0, 1);
        var accepted = lexical.Where(h => h.Score >= min).ToArray();
        if (accepted.Length == 0) return new([]);
        var vectors = Volatile.Read(ref _embeddings);
        var called = false;
        var hybridUsed = false;
        IReadOnlyList<KnowledgeHit> ranked = accepted;
        if (options.Value.IsEnabled && vectors is not null)
        {
            var query = await ollama.EmbedAsync([maskedQuestion], ct);
            called = query.WasCalled;
            if (ValidVectors(query.Value, 1) && query.Value![0].Length == vectors[0].Length)
            {
                var semantic = catalog.Chunks.Select((chunk, i) => new KnowledgeHit(chunk, Bm25KnowledgeIndex.Cosine(query.Value[0], vectors[i])))
                    .Where(h => h.Score >= 0.55 && accepted.Any(a => a.Chunk.Id == h.Chunk.Id))
                    .OrderByDescending(h => h.Score).ToArray();
                ranked = Bm25KnowledgeIndex.Fuse(accepted, semantic);
                hybridUsed = true;
            }
        }
        return new(ranked.Take(Math.Clamp(options.Value.TopK, 1, 10)).ToArray(), called, hybridUsed);
    }

    private static bool ValidVectors(float[][]? vectors, int count) => vectors is { Length: > 0 } && vectors.Length == count &&
        vectors[0] is { Length: > 0 } && vectors.All(vector => vector is not null && vector.Length == vectors[0].Length &&
            vector.All(float.IsFinite) && vector.Any(v => v != 0));
}
