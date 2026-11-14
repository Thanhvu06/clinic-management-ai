using System.Net;
using System.Reflection;
using ClosedXML.Excel;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public static class ExcerptReplayCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter console, CancellationToken ct = default)
    {
        var stamp = typeof(ExcerptReplayCommand).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "ExcerptReplaySourceCommit")?.Value;
        if (stamp != ExcerptReplayFile.HistoricalCommit)
            throw new InvalidOperationException("Build excerpt-replay in the historical worktree first.");
        if (args.FirstOrDefault() != "--hidden-eval" || args.Length != 7)
            throw new ArgumentException("Expected --hidden-eval followed by --xlsx, --master and --out.");

        var flags = new HashSet<string>(["--xlsx", "--master", "--out"], StringComparer.Ordinal);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            var flag = args[index];
            var extension = flag == "--out" ? ".json" : ".xlsx";
            if (!flags.Remove(flag) || !args[index + 1].EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid excerpt-replay options.");
            paths.Add(flag, Path.GetFullPath(args[index + 1]));
        }
        var output = paths["--out"];
        output = Path.Combine(ExternalOutputDirectory.Validate(Path.GetDirectoryName(output)!), Path.GetFileName(output));
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("The excerpt replay output already exists.");
        var questions = BlindAnswerWorkbook.LoadQuestions(paths["--xlsx"]);
        if (questions.Count != ExcerptReplayFile.RequiredRows) throw new InvalidDataException("Excerpt replay requires 150 questions.");
        using var master = new XLWorkbook(paths["--master"]);
        using var transport = new ExcerptReplayTransport(new MarkdownKnowledgeCatalog().Chunks,
            new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2), AllowAutoRedirect = false });
        using var runtime = await BlindAnswerRuntime.CreateAsync(transport, ct);
        var rows = new Dictionary<string, ExcerptReplayRow>(StringComparer.Ordinal);
        foreach (var question in questions)
            rows.Add(question.Stt!, await ExcerptReplayTurn.RunAsync(runtime, transport, question.Text, ct));

        using var http = new HttpClient(transport, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(5) };
        using var tags = await http.GetAsync(new OllamaOptions().LocalEndpoint("api/tags"), ct);
        if (tags.StatusCode != HttpStatusCode.OK) throw new ExcerptReplayTransportException();

        var verification = ExcerptReplayVerification.Verify(master, rows);
        await new ExcerptReplayFile(stamp, ExcerptReplayFile.PinnedDigest, rows).SaveNewAsync(output, ct);
        await console.WriteLineAsync($"Khớp: {verification.Matched}; lệch: {verification.Mismatched}");
        foreach (var count in verification.Counts)
            await console.WriteLineAsync($"{count.Route}\t{count.Matched}\t{count.Mismatched}");
        foreach (var difference in verification.Mismatches)
            await console.WriteLineAsync($"STT={difference.Stt}\t{difference.StoredLength}\t{difference.ReplayedLength}\t{difference.FirstDifferentCharacter}");
        return verification.IsComplete ? 0 : 2;
    }
}
