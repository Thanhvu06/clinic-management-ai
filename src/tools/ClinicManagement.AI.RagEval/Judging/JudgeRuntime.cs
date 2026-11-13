using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public interface ILlmJudgeClient
{
    Task<string> EvaluateAsync(string prompt, CancellationToken ct = default);
}

public interface IJudgeSources
{
    Task<string> GetAsync(string question, string? expectedDocument, CancellationToken ct = default);
}

public sealed class LocalJudgeClient(string model, HttpMessageHandler? handler = null, int context = 8192) : ILlmJudgeClient, IDisposable
{
    public const int OutputTokenLimit = 4096;
    private readonly HttpClient _http = new(handler ?? new SocketsHttpHandler { UseProxy = false })
        { BaseAddress = new Uri("http://127.0.0.1:11434"), Timeout = TimeSpan.FromMinutes(15) };

    public async Task<string> EvaluateAsync(string prompt, CancellationToken ct = default)
    {
        using var json = await ChatAsync(prompt, context, OutputTokenLimit, truncate: true, ct);
        if (json.RootElement.TryGetProperty("done_reason", out var reason) && reason.GetString() == "length")
            throw new InvalidDataException("Judge response was truncated.");
        return json.RootElement.GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidDataException("Empty judge response.");
    }

    internal async Task<int> MeasurePromptAsync(string prompt, CancellationToken ct)
    {
        using var json = await ChatAsync(prompt, 8192, 1, truncate: false, ct);
        return json.RootElement.TryGetProperty("prompt_eval_count", out var count) && count.TryGetInt32(out var tokens) && tokens > 0
            ? tokens : throw new InvalidDataException("Ollama did not report prompt tokens.");
    }

    private async Task<JsonDocument> ChatAsync(string prompt, int numCtx, int numPredict, bool truncate, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("/api/chat", new
        {
            model, stream = false, format = JudgePrompt.Schema, truncate,
            options = new { temperature = 0, num_ctx = numCtx, num_predict = numPredict },
            messages = new[] { new { role = "user", content = prompt } }
        }, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public void Dispose() => _http.Dispose();
}

public sealed class CorpusJudgeSources : IJudgeSources, IDisposable
{
    private readonly EvaluationRetrievers _retrievers;
    private readonly IReadOnlyDictionary<string, string> _documents;

    private CorpusJudgeSources(EvaluationRetrievers retrievers, IReadOnlyDictionary<string, string> documents)
    { _retrievers = retrievers; _documents = documents; }

    public static async Task<CorpusJudgeSources> CreateAsync(HttpMessageHandler? handler = null, string? cacheRoot = null, CancellationToken ct = default)
    {
        var documents = Directory.GetFiles(MarkdownKnowledgeCatalog.DefaultDirectory, "*.md")
            .Select(File.ReadAllText).ToDictionary(text => MarkdownKnowledgeCatalog.Parse(text)[0].Source.DocId, text => text, StringComparer.Ordinal);
        return new(await EvaluationRetrievers.CreateAsync(handler, cacheRoot, ct), documents);
    }

    public async Task<string> GetAsync(string question, string? expectedDocument, CancellationToken ct = default) =>
        (await GetMaterialAsync(question, expectedDocument, ct)).Text;

    internal async Task<JudgeMaterial> GetMaterialAsync(string question, string? expectedDocument, CancellationToken ct)
    {
        var full = string.Empty;
        if (!string.IsNullOrWhiteSpace(expectedDocument) && !_documents.TryGetValue(expectedDocument, out full))
            throw new InvalidDataException("Unknown expected document.");
        var retrieval = await _retrievers.Modes["hybrid"].RetrieveAsync(question, ct);
        if (retrieval.Hits.Count > 0 && !retrieval.HybridUsed) throw new InvalidOperationException("Hybrid embedding failed.");
        var text = "TÀI LIỆU ĐÚNG (TOÀN VĂN):\n" + full + "\nTOP-4 ĐOẠN HYBRID:\n" +
            string.Join("\n\n", retrieval.Hits.Select(hit => $"[{hit.Chunk.Id}] {hit.Chunk.Source.Title} / {hit.Chunk.Source.Heading}\n{hit.Chunk.Text}"));
        return new(text, full.Length > 0 || retrieval.Hits.Count > 0, retrieval.Hits.Count > 0);
    }

    public void Dispose() => _retrievers.Dispose();
}

internal sealed record JudgeMaterial(string Text, bool? HasDocuments, bool? RetrievalHasDocuments);
