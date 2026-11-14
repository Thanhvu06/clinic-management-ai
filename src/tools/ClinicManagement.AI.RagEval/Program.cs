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
if (mode is "dev-groups" or "dev-overlap" or "dev-split" or "dev-plant")
{
    Environment.ExitCode = await DevCalibrationCommands.RunAsync(mode, args.Skip(1).ToArray(), Console.Out, Console.Error);
    return;
}
if (mode == "judge-diagnose")
{
    try
    {
        await JudgeDiagnose.RunAsync(args.Skip(1).ToArray(), Console.Out);
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Judge diagnosis failed. Check the checkpoint, matching master, and new judge-diagnose.json output.");
        Environment.ExitCode = 1;
    }
    return;
}
if (mode is "llm-judge" or "disagreements")
{
    try
    {
        if (mode == "disagreements") await Disagreements.RunAsync(args.Skip(1).ToArray(), Console.Out);
        else
        {
            var judgeOptions = JudgeOptions.Parse(args.Skip(1).ToArray());
            using var sources = await CorpusJudgeSources.CreateAsync();
            using var judge = new LocalJudgeClient(judgeOptions.Model, context: judgeOptions.Context);
            if (judgeOptions.MeasurePrompt)
                await JudgeRun.MeasurePromptsAsync(judgeOptions, judge, sources, Console.Out);
            else
                await JudgeRun.RunAsync(judgeOptions, judge, sources, Console.Out);
        }
    }
    catch (JudgeCheckpointException error)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 1;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Grading tool failed. Check workbook structure, options, new output files and local Ollama models.");
        Environment.ExitCode = 1;
    }
    return;
}
if (mode is "y-chinh-template" or "grading-sheet-v2")
{
    try { await Rq1WorkbookCommands.RunAsync(mode, args.Skip(1).ToArray(), Console.Out); }
    catch (KeyIdeasAlreadyExistsException error)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 1;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("RQ1 workbook export failed. Check workbook paths, headers and output access.");
        Environment.ExitCode = 1;
    }
    return;
}
if (mode is "dense" or "compare-retrieval" or "rq1-stats")
{
    try
    {
        var statisticsInput = StatisticsInput.Parse(args.Skip(1).ToArray(), mode);
        StatisticsReport.EnsureNew(statisticsInput.Output);
        if (mode == "rq1-stats")
            await Rq1Statistics.WriteAsync(Rq1Statistics.Read(statisticsInput.Xlsx!, statisticsInput.Primary, statisticsInput.ReviewedXlsx),
                statisticsInput.Output, Console.Out);
        else
        {
            var selectedQuestions = await statisticsInput.LoadQuestionsAsync();
            using var retrievers = await EvaluationRetrievers.CreateAsync();
            var comparison = await RetrievalComparison.EvaluateAsync(selectedQuestions, retrievers, mode == "dense" ? ["dense"] : null);
            await RetrievalComparison.WriteAsync(comparison, statisticsInput.Output, Console.Out);
        }
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Statistics evaluation failed. Check input structure, a new JSON output path, and local bge-m3 if running retrieval.");
        Environment.ExitCode = 1;
    }
    return;
}
if (mode == "merge-grades")
{
    try { await MergeGrades.RunAsync(MergeGradesOptions.Parse(args.Skip(1).ToArray()), Console.Out); }
    catch (Exception)
    {
        Console.Error.WriteLine("Grade merge failed. Check workbook structure, options and a new output path.");
        Environment.ExitCode = 1;
    }
    return;
}
if (mode == "blind-answers")
{
    try
    {
        var blindOptions = BlindAnswerOptions.Parse(args.Skip(1).ToArray());
        using var runtime = await BlindAnswerRuntime.CreateAsync(retrieval: blindOptions.Retrieval);
        await BlindAnswerRun.RunAsync(blindOptions, runtime, Console.Out);
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Blind export failed. Check CSV/options, --out-dir outside the repository, and local Ollama models.");
        Environment.ExitCode = 1;
    }
    return;
}
if (mode is not ("bm25" or "hybrid" or "end-to-end" or "end-to-end-hybrid" or "sweep"))
    throw new ArgumentException("Usage: RagEval [bm25|hybrid|end-to-end|end-to-end-hybrid|sweep] [output.json] [--questions baseline|v2|combined|path.json] [--csv path.csv]; dense|compare-retrieval [--questions baseline|v2|combined|path.json|--csv path.csv|--xlsx path.xlsx] [--output new.json]; rq1-stats --xlsx merged.xlsx [--primary grader1|grader2|mean] [--reviewed-xlsx reviewed.xlsx] [--output new.json]; y-chinh-template --xlsx questions.xlsx; grading-sheet-v2 --master master.xlsx --ideas y-chinh.xlsx [--sources sources.xlsx]; blind-answers (--csv path.csv|--xlsx path.xlsx) --out-dir external-directory [--seed integer] [--retrieval hybrid|bm25]; merge-grades --master master.xlsx --grader2 grader2.xlsx --out new.xlsx [--grader1 grader1.xlsx]");
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
            questionFiles = input.IsCsv ? Array.Empty<string>() : input.QuestionFiles.Select(Path.GetFileName).ToArray(),
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
    var byCategory = new Dictionary<string, CategoryMetrics>();
    foreach (var question in questions)
    {
        var watch = Stopwatch.StartNew();
        var classification = useEndToEnd ? classifier.Classify(question.Text, new IntentClassificationContext()) : new IntentClassificationResult();
        var safety = useEndToEnd ? new AiSafetyGuard().Inspect(question.Text) : new ClinicManagement.Application.AI.Tools.AiSafetyGuardResult();
        var candidate = !useEndToEnd || !safety.IsEmergency && !safety.IsPromptInjection &&
            !AiMedicalScopeGuard.IsPrescriptionRequest(question.Text) &&
            KnowledgeQuestionPolicy.IsCandidate(question.Text, classification.Intent, false, classification);
        if (candidate) { candidates++; if (question.ExpectedDocId is not null) inScopeCandidates++; }
        else if (!input.IsCsv && useEndToEnd && question.ExpectedDocId is not null)
            gateRejections.Add(new { question.Text, classification.Intent, classification.IsClear,
                classification.ExtractedDoctorName, classification.ExtractedDate, classification.ExtractedTimePreference,
                safety.IsEmergency });
        var result = candidate ? await retriever.RetrieveAsync(question.Text) : new KnowledgeRetrieval([]);
        if (result.Hits.Count > 0) answers++;
        samples.Add(watch.Elapsed.TotalMilliseconds);
        if (result.EmbeddingWasCalled) embeddingCalls++;
        if (result.HybridUsed) hybridQueries++;
        var ids = result.Hits.Select(h => h.Chunk.Source.DocId).Distinct().ToArray();
        if (question.Category is { } category)
        {
            if (!byCategory.TryGetValue(category, out var metrics)) byCategory[category] = metrics = new();
            metrics.Add(question, ids);
        }
        if (question.ExpectedDocId is null)
        {
            outside++;
            if (ids.Length == 0) rejected++;
            else if (!input.IsCsv) falsePositives.Add(new { question.Text, sources = result.Hits.Select(h => h.Chunk.Source).ToArray() });
        }
        else
        {
            inScope++;
            var rank = Array.IndexOf(ids, question.ExpectedDocId);
            if (rank == 0) hit1++;
            if (rank is >= 0 and < 3) hit3++;
            if (rank >= 0) reciprocal += 1.0 / (rank + 1);
            if (!input.IsCsv && candidate && rank is not (>= 0 and < 3))
                retrievalMisses.Add(new { question.Text, question.ExpectedDocId, returnedDocIds = ids });
        }
    }
    if (useHybrid && (hybridQueries == 0 || hybridQueries != embeddingCalls))
        throw new InvalidOperationException("Query embeddings did not complete; refusing to report a hybrid result.");
    var sorted = samples.Order().ToArray();
    var h1 = (double)hit1 / inScope;
    var h3 = (double)hit3 / inScope;
    var mrr = reciprocal / inScope;
    var rej = (double)rejected / outside;
    var avgMs = samples.Average();
    var p95Ms = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
    object report = input.IsCsv
        ? new
        {
            mode = currentMode,
            measuredAtUtc = DateTimeOffset.UtcNow,
            n = questions.Length,
            inScope,
            outside,
            hitAt1 = h1,
            hitAt3 = h3,
            mrr,
            correctRejection = rej,
            byCategory,
            averageMs = avgMs,
            p95Ms
        }
        : new
        {
            mode = currentMode, measuredAtUtc = DateTimeOffset.UtcNow, chatModel = options.Value.ChatModel, embeddingModel = options.Value.EmbeddingModel,
            questionFiles = input.QuestionFiles.Select(Path.GetFileName).ToArray(),
            minScore = options.Value.MinScore, minQueryCoverage = options.Value.MinQueryCoverage, minTermIdf = options.Value.MinTermIdf,
            minCosine = options.Value.MinCosine, topK = options.Value.TopK, documents = catalog.Chunks.Select(c => c.Source.DocId).Distinct().Count(),
            chunks = catalog.Chunks.Count, inScope, outside, candidates, candidateRate = (double)candidates / questions.Length,
            inScopeCandidateRate = (double)inScopeCandidates / inScope, answerRate = (double)answers / questions.Length,
            hitDenominator = $"all {inScope} in-scope questions, including gate rejections as misses", hitAt1 = h1, hitAt3 = h3,
            mrr, correctRejection = rej, byCategory,
            averageMs = avgMs, p95Ms, embeddingCalls, hybridQueries,
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
