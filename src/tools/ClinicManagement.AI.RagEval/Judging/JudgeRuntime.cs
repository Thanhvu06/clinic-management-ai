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

public sealed class LocalJudgeClient(string model, HttpMessageHandler? handler = null) : ILlmJudgeClient, IDisposable
{
    private readonly HttpClient _http = new(handler ?? new SocketsHttpHandler { UseProxy = false })
        { BaseAddress = new Uri("http://127.0.0.1:11434"), Timeout = TimeSpan.FromMinutes(15) };

    public async Task<string> EvaluateAsync(string prompt, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/chat", new
        {
            model, stream = false, format = JudgePrompt.Schema,
            options = new { temperature = 0, num_ctx = 8192, num_predict = 4096 },
            messages = new[] { new { role = "user", content = prompt } }
        }, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (json.RootElement.TryGetProperty("done_reason", out var reason) && reason.GetString() == "length")
            throw new InvalidDataException("Judge response was truncated.");
        return json.RootElement.GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidDataException("Empty judge response.");
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

    public async Task<string> GetAsync(string question, string? expectedDocument, CancellationToken ct = default)
    {
        var full = string.Empty;
        if (!string.IsNullOrWhiteSpace(expectedDocument) && !_documents.TryGetValue(expectedDocument, out full))
            throw new InvalidDataException("Unknown expected document.");
        var retrieval = await _retrievers.Modes["hybrid"].RetrieveAsync(question, ct);
        if (retrieval.Hits.Count > 0 && !retrieval.HybridUsed) throw new InvalidOperationException("Hybrid embedding failed.");
        return "TÀI LIỆU ĐÚNG (TOÀN VĂN):\n" + full + "\nTOP-4 ĐOẠN HYBRID:\n" +
            string.Join("\n\n", retrieval.Hits.Select(hit => $"[{hit.Chunk.Id}] {hit.Chunk.Source.Title} / {hit.Chunk.Source.Heading}\n{hit.Chunk.Text}"));
    }

    public void Dispose() => _retrievers.Dispose();
}
