using System.Diagnostics;
using System.Text.Json;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var mode = args.FirstOrDefault() ?? "bm25";
if (mode is not ("bm25" or "hybrid" or "end-to-end" or "end-to-end-hybrid"))
    throw new ArgumentException("Usage: RagEval [bm25|hybrid|end-to-end|end-to-end-hybrid] [output.json]");
var endToEnd = mode.StartsWith("end-to-end", StringComparison.Ordinal);
var hybrid = mode is "hybrid" or "end-to-end-hybrid";
var options = Options.Create(new OllamaOptions { IsEnabled = hybrid });
var classifier = new VietnameseIntentClassifier();
var environment = new EvaluationEnvironment();
using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
    { Timeout = Timeout.InfiniteTimeSpan };
var ollama = new OllamaKnowledgeClient(http, options);
var catalog = new MarkdownKnowledgeCatalog();
using var retriever = new ClinicKnowledgeRetriever(catalog, ollama, options, environment, NullLogger<ClinicKnowledgeRetriever>.Instance);
if (hybrid)
{
    await retriever.BuildEmbeddingsAsync();
    if (!retriever.EmbeddingsReady) throw new InvalidOperationException("Hybrid evaluation requires Ollama and a complete embedding index: " + ollama.LastFailureCode);
}
var questions = JsonSerializer.Deserialize<Question[]>(await File.ReadAllTextAsync(
    Path.Combine(AppContext.BaseDirectory, "AI", "Knowledge", "Eval", "rag-dev-questions.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
// Warm-up is excluded from measured per-query latency; indexing time is also excluded.
await retriever.RetrieveAsync("Chuẩn bị trước khi khám cần gì?");
var samples = new List<double>();
var inScope = 0; var outside = 0; var hit1 = 0; var hit3 = 0; var rejected = 0; double reciprocal = 0;
var embeddingCalls = 0;
var hybridQueries = 0;
var candidates = 0;
var inScopeCandidates = 0;
var answers = 0;
var gateRejections = new List<object>();
foreach (var question in questions)
{
    var watch = Stopwatch.StartNew();
    var classification = endToEnd ? classifier.Classify(question.Text, new IntentClassificationContext()) : new IntentClassificationResult();
    var safety = endToEnd ? new AiSafetyGuard().Inspect(question.Text) : new ClinicManagement.Application.AI.Tools.AiSafetyGuardResult();
    var candidate = !endToEnd || !safety.IsEmergency && !safety.IsPromptInjection &&
        !AiMedicalScopeGuard.IsPrescriptionRequest(question.Text) &&
        KnowledgeQuestionPolicy.IsCandidate(question.Text, classification.Intent, false, classification);
    if (candidate) { candidates++; if (question.ExpectedDocId is not null) inScopeCandidates++; }
    else if (endToEnd && question.ExpectedDocId is not null)
        gateRejections.Add(new { question.Text, classification.Intent, classification.IsClear,
            classification.ExtractedDoctorName, classification.ExtractedDate, classification.ExtractedTimePreference,
            safety.IsEmergency });
    var result = candidate ? await retriever.RetrieveAsync(question.Text) : new KnowledgeRetrieval([]);
    if (result.Hits.Count > 0) answers++;
    samples.Add(watch.Elapsed.TotalMilliseconds);
    if (result.EmbeddingWasCalled) embeddingCalls++;
    if (result.HybridUsed) hybridQueries++;
    var ids = result.Hits.Select(h => h.Chunk.Source.DocId).Distinct().ToArray();
    if (question.ExpectedDocId is null) { outside++; if (ids.Length == 0) rejected++; }
    else
    {
        inScope++;
        var rank = Array.IndexOf(ids, question.ExpectedDocId);
        if (rank == 0) hit1++;
        if (rank is >= 0 and < 3) hit3++;
        if (rank >= 0) reciprocal += 1.0 / (rank + 1);
    }
}
if (hybrid && (hybridQueries == 0 || hybridQueries != embeddingCalls))
    throw new InvalidOperationException("Query embeddings did not complete; refusing to report a hybrid result.");
var sorted = samples.Order().ToArray();
var report = new
{
    mode, measuredAtUtc = DateTimeOffset.UtcNow, chatModel = options.Value.ChatModel, embeddingModel = options.Value.EmbeddingModel,
    minScore = options.Value.MinScore, topK = options.Value.TopK, documents = catalog.Chunks.Select(c => c.Source.DocId).Distinct().Count(),
    chunks = catalog.Chunks.Count, inScope, outside, candidates, candidateRate = (double)candidates / questions.Length,
    inScopeCandidateRate = (double)inScopeCandidates / inScope, answerRate = (double)answers / questions.Length,
    hitDenominator = "all 156 in-scope questions, including gate rejections as misses", hitAt1 = (double)hit1 / inScope, hitAt3 = (double)hit3 / inScope,
    mrr = reciprocal / inScope, correctRejection = (double)rejected / outside,
    averageMs = samples.Average(), p95Ms = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1], embeddingCalls, hybridQueries,
    gateRejections,
    latencyIncludes = endToEnd ? "classification, safety, candidate gate and retrieval; all questions included; indexing and warm-up excluded"
        : "retrieval only; indexing and warm-up excluded; rejected queries included"
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (args.Length > 1)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    await File.WriteAllTextAsync(args[1], json + Environment.NewLine);
}

internal sealed record Question([property: System.Text.Json.Serialization.JsonPropertyName("question")] string Text, string? ExpectedDocId);
internal sealed class EvaluationEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "ClinicManagement.AI.RagEval";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
