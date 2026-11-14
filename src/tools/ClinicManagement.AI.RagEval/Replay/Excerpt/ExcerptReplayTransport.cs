using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed class ExcerptReplayTransport : DelegatingHandler
{
    private const string DocumentSeparator = "\nTài liệu công khai:\n";
    private readonly Dictionary<string, KnowledgeChunk[]> _blocks;
    private readonly OllamaOptions _options = new();
    private readonly bool _verifyDigest;
    public ExcerptPromptPassage[] PromptPassages { get; private set; } = [];

    public ExcerptReplayTransport(IReadOnlyList<KnowledgeChunk> chunks, HttpMessageHandler innerHandler, bool verifyDigest = true)
        : base(innerHandler)
    {
        _verifyDigest = verifyDigest;
        _blocks = chunks.GroupBy(chunk =>
                $"[{chunk.Source.DocId}] {chunk.Source.Title} — {chunk.Source.Heading}\n{chunk.Text}", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }

    internal void BeginTurn() => PromptPassages = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method == HttpMethod.Post && IsEndpoint(request, "api/chat"))
        {
            using var json = await ReadBodyAsync(request, ct);
            var messages = json.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() == 0)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { done = true }) };
            var userMessage = messages.EnumerateArray().Single(message => message.GetProperty("role").GetString() == "user")
                .GetProperty("content").GetString() ?? throw new ExcerptReplayTransportException();
            var separator = userMessage.LastIndexOf(DocumentSeparator, StringComparison.Ordinal);
            if (separator < 0) throw new ExcerptReplayTransportException();
            var blocks = userMessage[(separator + DocumentSeparator.Length)..].Split("\n\n", StringSplitOptions.None);
            PromptPassages = blocks.Select(block =>
            {
                if (!_blocks.TryGetValue(block, out var candidates) || candidates.Length != 1)
                    throw new ExcerptReplayTransportException();
                var chunk = candidates[0];
                return new ExcerptPromptPassage(chunk.Id, chunk.Text);
            }).ToArray();
            throw new ExcerptPromptCapturedException();
        }

        if (request.Method == HttpMethod.Get && IsEndpoint(request, "api/tags"))
        {
            var response = await SendCheckedAsync(request, ct);
            try
            {
                if (_verifyDigest)
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    var model = _options.EmbeddingModel;
                    var entries = json.RootElement.GetProperty("models").EnumerateArray()
                        .Where(entry => entry.GetProperty("name").GetString() is { } name &&
                            (name == model || name == model + ":latest")).ToArray();
                    if (entries.Length != 1 || entries[0].GetProperty("digest").GetString() != ExcerptReplayFile.PinnedDigest)
                        throw new ExcerptReplayTransportException();
                }
                return response;
            }
            catch { response.Dispose(); throw; }
        }

        if (request.Method == HttpMethod.Post && IsEndpoint(request, "api/embed"))
        {
            using var body = await ReadBodyAsync(request, ct);
            if (body.RootElement.GetProperty("model").GetString() != _options.EmbeddingModel)
                throw new ExcerptReplayTransportException();
            var response = await SendCheckedAsync(request, ct);
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var vectors = json.RootElement.GetProperty("embeddings").Deserialize<float[][]>();
                if (vectors is null || vectors.Length != body.RootElement.GetProperty("input").GetArrayLength() ||
                    vectors.Length == 0 || vectors[0] is not { Length: 1024 } ||
                    vectors.Any(vector => vector is not { Length: 1024 } || !vector.All(float.IsFinite) || !vector.Any(value => value != 0)))
                    throw new ExcerptReplayTransportException();
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        throw new ExcerptReplayTransportException();
    }

    private bool IsEndpoint(HttpRequestMessage request, string path) => request.RequestUri == _options.LocalEndpoint(path);

    private async Task<HttpResponseMessage> SendCheckedAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try { response = await base.SendAsync(request, ct); }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ExcerptReplayTransportException();
        }
        if (response.IsSuccessStatusCode) return response;
        response.Dispose();
        throw new ExcerptReplayTransportException();
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpRequestMessage request, CancellationToken ct) =>
        JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
}

internal sealed class ExcerptPromptCapturedException : Exception;
internal sealed class ExcerptReplayTransportException : Exception;
