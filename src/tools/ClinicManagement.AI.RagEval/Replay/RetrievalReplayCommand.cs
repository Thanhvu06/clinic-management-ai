using System.Text.Json;
using ClosedXML.Excel;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.AI.RagEval;

internal static class RetrievalReplayCommand
{
    internal static async Task<int> RunAsync(string[] args, TextWriter console, CancellationToken ct = default)
    {
        if (args.FirstOrDefault() != "--hidden-eval")
            throw new ArgumentException("Replay requires --hidden-eval.");
        var values = Rq1WorkbookCommands.ParseOptions(args[1..],
            ["--xlsx", "--master", "--out", "--source-commit", "--embedding-digest"]);
        var input = ParsePath("--xlsx", ".xlsx");
        var masterPath = ParsePath("--master", ".xlsx");
        var output = ParsePath("--out", ".json");
        output = Path.Combine(ExternalOutputDirectory.Validate(Path.GetDirectoryName(output)!), Path.GetFileName(output));
        StatisticsReport.EnsureNew(output);
        var commit = values["--source-commit"];
        var expectedDigest = values["--embedding-digest"];
        if (commit.Length != 40 || !commit.All(Uri.IsHexDigit) ||
            expectedDigest.Length != 64 || !expectedDigest.All(Uri.IsHexDigit))
            throw new ArgumentException("Specify full commit SHA and model digest.");

        var questions = BlindAnswerWorkbook.LoadQuestions(input);
        if (questions.Count != 150) throw new InvalidDataException("Replay requires 150 questions.");
        using var master = new XLWorkbook(masterPath);
        var options = Options.Create(new OllamaOptions { IsEnabled = true });
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
            { Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds) };
        var client = new OllamaKnowledgeClient(http, options);
        await VerifyDigestAsync();
        var cache = Directory.CreateTempSubdirectory("rag-replay-");
        try
        {
            using var retriever = new ClinicKnowledgeRetriever(new MarkdownKnowledgeCatalog(), client, options,
                new EvaluationEnvironment { ContentRootPath = cache.FullName }, NullLogger<ClinicKnowledgeRetriever>.Instance);
            await retriever.BuildEmbeddingsAsync(ct);
            if (!retriever.EmbeddingsReady) throw new InvalidOperationException("Replay requires a complete bge-m3 index.");
            var entries = await RetrievalReplay.CollectAsync(questions, retriever, ct);
            await VerifyDigestAsync();
            var verification = ReplayVerification.Verify(master, entries);
            var result = new RetrievalReplayResult(commit, expectedDigest.ToLowerInvariant(), entries);
            await StatisticsReport.SaveAsync(result, output, ct);
            await console.WriteLineAsync($"Khớp: {verification.Matched}; lệch: {verification.Mismatched}; không xác định: {verification.Indeterminate}");
            foreach (var (type, count) in verification.ByType.OrderBy(item => item.Key))
                await console.WriteLineAsync($"{type}: {count}");
            return verification.IsComplete ? 0 : 2;
        }
        finally
        {
            cache.Delete(recursive: true);
        }

        string ParsePath(string flag, string extension)
        {
            var path = Path.GetFullPath(values[flag]);
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid replay file extension.");
            return path;
        }

        async Task VerifyDigestAsync()
        {
            using var response = await http.GetAsync(options.Value.LocalEndpoint("api/tags"), ct);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var model = options.Value.EmbeddingModel;
            var installed = json.RootElement.GetProperty("models").EnumerateArray()
                .SingleOrDefault(entry => entry.GetProperty("name").GetString() == model + ":latest" ||
                    entry.GetProperty("name").GetString() == model);
            if (installed.ValueKind != JsonValueKind.Object ||
                !string.Equals(installed.GetProperty("digest").GetString(), expectedDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Installed embedding digest does not match the pinned value.");
        }
    }
}
