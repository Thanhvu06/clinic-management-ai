using System.Diagnostics;
using System.Text.Json;
using ClinicManagement.AI.RagEval;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var mode = args.FirstOrDefault() ?? "bm25";
if (mode is not ("bm25" or "hybrid" or "end-to-end" or "end-to-end-hybrid" or "sweep"))
    throw new ArgumentException("Usage: RagEval [bm25|hybrid|end-to-end|end-to-end-hybrid|sweep] [output.json] [--questions baseline|v2|combined|path.json]...");
var input = EvaluationInput.Parse(args.Skip(1).ToArray());
var questions = await input.LoadAsync();
var classifier = new VietnameseIntentClassifier();
var environment = new EvaluationEnvironment();
using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
    { Timeout = Timeout.InfiniteTimeSpan };
var catalog = new MarkdownKnowledgeCatalog();
if (mode == "sweep")
{
    var sweepResults = new List<object>();
    var configs = new (double MinScore, double MinIdf, double MinCosine)[]
    {
        (0.35, 1.15, 0.55),
        (0.35, 1.25, 0.55),
        (0.40, 1.25, 0.50),
        (0.40, 1.25, 0.40),
        (0.45, 1.15, 0.35),
        (0.45, 1.25, 0.55),
        (0.45, 1.25, 0.45),
        (0.45, 1.25, 0.35),
        (0.45, 1.25, 0.25),
        (0.45, 1.25, 0.20),
        (0.45, 1.35, 0.20),
        (0.45, 1.50, 0.20),
        (0.50, 1.25, 0.20),
        (0.50, 1.50, 0.55)
    };
    foreach (var (minScore, minIdf, minCosine) in configs)
    {
        var pureBm25 = await RunOnce("bm25", false, false, minScore, 0.5, minIdf, minCosine);
        var pureHybrid = await RunOnce("hybrid", true, false, minScore, 0.5, minIdf, minCosine);
        var e2eBm25 = await RunOnce("end-to-end", false, true, minScore, 0.5, minIdf, minCosine);
        var e2eHybrid = await RunOnce("end-to-end-hybrid", true, true, minScore, 0.5, minIdf, minCosine);
        sweepResults.Add(new
        {
            questionFiles = input.QuestionFiles.Select(Path.GetFileName).ToArray(),
            inScope = questions.Count(q => q.ExpectedDocId is not null), outside = questions.Count(q => q.ExpectedDocId is null),
            minScore, minQueryCoverage = 0.5, minTermIdf = minIdf, minCosine,
            bm25HitAt3 = Math.Round(pureBm25.HitAt3 * 100, 2),
            bm25Rejection = Math.Round(pureBm25.CorrectRejection * 100, 2),
            hybridHitAt3 = Math.Round(pureHybrid.HitAt3 * 100, 2),
            hybridRejection = Math.Round(pureHybrid.CorrectRejection * 100, 2),
            e2eBm25HitAt1 = Math.Round(e2eBm25.HitAt1 * 100, 2),
            e2eBm25HitAt3 = Math.Round(e2eBm25.HitAt3 * 100, 2),
            e2eBm25Rejection = Math.Round(e2eBm25.CorrectRejection * 100, 2),
            e2eHybridHitAt1 = Math.Round(e2eHybrid.HitAt1 * 100, 2),
            e2eHybridHitAt3 = Math.Round(e2eHybrid.HitAt3 * 100, 2),
            e2eHybridRejection = Math.Round(e2eHybrid.CorrectRejection * 100, 2)
        });
    }
    var sweepJson = JsonSerializer.Serialize(sweepResults, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(sweepJson);
    if (input.OutputPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(input.OutputPath))!);
        await File.WriteAllTextAsync(input.OutputPath, sweepJson + Environment.NewLine);
    }
    return;
}
var endToEnd = mode.StartsWith("end-to-end", StringComparison.Ordinal);
var hybrid = mode is "hybrid" or "end-to-end-hybrid";
var defaultOptions = new OllamaOptions { IsEnabled = hybrid };
var single = await RunOnce(mode, hybrid, endToEnd, defaultOptions.MinScore, defaultOptions.MinQueryCoverage, defaultOptions.MinTermIdf, defaultOptions.MinCosine);
var json = JsonSerializer.Serialize(single.Report, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (input.OutputPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(input.OutputPath))!);
    await File.WriteAllTextAsync(input.OutputPath, json + Environment.NewLine);
}

async Task<(double HitAt1, double HitAt3, double CorrectRejection, object Report)> RunOnce(
    string currentMode, bool useHybrid, bool useEndToEnd, double minScore, double minCoverage, double minIdf, double minCosine)
{
    var options = Options.Create(new OllamaOptions
    {
        IsEnabled = useHybrid, MinScore = minScore, MinQueryCoverage = minCoverage, MinTermIdf = minIdf, MinCosine = minCosine
    });
    var ollama = new OllamaKnowledgeClient(http, options);
    using var retriever = new ClinicKnowledgeRetriever(catalog, ollama, options, environment, NullLogger<ClinicKnowledgeRetriever>.Instance);
    if (useHybrid)
    {
        await retriever.BuildEmbeddingsAsync();
        if (!retriever.EmbeddingsReady) throw new InvalidOperationException("Hybrid evaluation requires Ollama and a complete embedding index: " + ollama.LastFailureCode);
    }
    // Warm-up is excluded from measured per-query latency; indexing time is also excluded.
    await retriever.RetrieveAsync("Chuẩn bị trước khi khám cần gì?");
    var samples = new List<double>();
    var inScope = 0; var outside = 0; var hit1 = 0; var hit3 = 0; var rejected = 0; double reciprocal = 0;
    var embeddingCalls = 0; var hybridQueries = 0; var candidates = 0; var inScopeCandidates = 0; var answers = 0;
    var gateRejections = new List<object>();
    var retrievalMisses = new List<object>();
    var falsePositives = new List<object>();
    foreach (var question in questions)
    {
        var watch = Stopwatch.StartNew();
        var classification = useEndToEnd ? classifier.Classify(question.Text, new IntentClassificationContext()) : new IntentClassificationResult();
        var safety = useEndToEnd ? new AiSafetyGuard().Inspect(question.Text) : new ClinicManagement.Application.AI.Tools.AiSafetyGuardResult();
        var candidate = !useEndToEnd || !safety.IsEmergency && !safety.IsPromptInjection &&
            !AiMedicalScopeGuard.IsPrescriptionRequest(question.Text) &&
            KnowledgeQuestionPolicy.IsCandidate(question.Text, classification.Intent, false, classification);
        if (candidate) { candidates++; if (question.ExpectedDocId is not null) inScopeCandidates++; }
        else if (useEndToEnd && question.ExpectedDocId is not null)
            gateRejections.Add(new { question.Text, classification.Intent, classification.IsClear,
                classification.ExtractedDoctorName, classification.ExtractedDate, classification.ExtractedTimePreference,
                safety.IsEmergency });
        var result = candidate ? await retriever.RetrieveAsync(question.Text) : new KnowledgeRetrieval([]);
        if (result.Hits.Count > 0) answers++;
        samples.Add(watch.Elapsed.TotalMilliseconds);
        if (result.EmbeddingWasCalled) embeddingCalls++;
        if (result.HybridUsed) hybridQueries++;
        var ids = result.Hits.Select(h => h.Chunk.Source.DocId).Distinct().ToArray();
        if (question.ExpectedDocId is null)
        {
            outside++;
            if (ids.Length == 0) rejected++;
            else falsePositives.Add(new { question.Text, sources = result.Hits.Select(h => h.Chunk.Source).ToArray() });
        }
        else
        {
            inScope++;
            var rank = Array.IndexOf(ids, question.ExpectedDocId);
            if (rank == 0) hit1++;
            if (rank is >= 0 and < 3) hit3++;
            if (rank >= 0) reciprocal += 1.0 / (rank + 1);
            if (candidate && rank is not (>= 0 and < 3))
                retrievalMisses.Add(new { question.Text, question.ExpectedDocId, returnedDocIds = ids });
        }
    }
    if (useHybrid && (hybridQueries == 0 || hybridQueries != embeddingCalls))
        throw new InvalidOperationException("Query embeddings did not complete; refusing to report a hybrid result.");
    var sorted = samples.Order().ToArray();
    var h1 = (double)hit1 / inScope;
    var h3 = (double)hit3 / inScope;
    var rej = (double)rejected / outside;
    var report = new
    {
        mode = currentMode, measuredAtUtc = DateTimeOffset.UtcNow, chatModel = options.Value.ChatModel, embeddingModel = options.Value.EmbeddingModel,
        questionFiles = input.QuestionFiles.Select(Path.GetFileName).ToArray(),
        minScore = options.Value.MinScore, minQueryCoverage = options.Value.MinQueryCoverage, minTermIdf = options.Value.MinTermIdf,
        minCosine = options.Value.MinCosine, topK = options.Value.TopK, documents = catalog.Chunks.Select(c => c.Source.DocId).Distinct().Count(),
        chunks = catalog.Chunks.Count, inScope, outside, candidates, candidateRate = (double)candidates / questions.Length,
        inScopeCandidateRate = (double)inScopeCandidates / inScope, answerRate = (double)answers / questions.Length,
        hitDenominator = $"all {inScope} in-scope questions, including gate rejections as misses", hitAt1 = h1, hitAt3 = h3,
        mrr = reciprocal / inScope, correctRejection = rej,
        averageMs = samples.Average(), p95Ms = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1], embeddingCalls, hybridQueries,
        gateRejections, retrievalMisses, falsePositives,
        latencyIncludes = useEndToEnd ? "classification, safety, candidate gate and retrieval; all questions included; indexing and warm-up excluded"
            : "retrieval only; indexing and warm-up excluded; rejected queries included"
    };
    return (h1, h3, rej, report);
}

internal sealed class EvaluationEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "ClinicManagement.AI.RagEval";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
