using System.Text;
using System.Text.Json;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.Options;

namespace ClinicManagement.AI.RagEval;

public static class DevCalibrationCommands
{
    public static async Task<int> RunAsync(string mode, string[] args, TextWriter output, TextWriter error)
    {
        var usage = mode switch
        {
            "dev-overlap" => "dev-overlap --hidden-eval workbook.xlsx --retained new.json",
            "dev-split" => "dev-split --retained overlap.json --out-dir external-directory",
            "dev-plant" => "dev-plant --out-dir external-directory (dev-kiem.csv and dev-kiem/dev-nguon.json required)",
            _ => "dev-groups"
        };
        if (args is ["--help"])
        {
            await output.WriteLineAsync(usage);
            return 0;
        }

        try
        {
            switch (mode)
            {
                case "dev-groups":
                    Parse(args);
                    var dev = await DevQuestions.LoadRq2Async();
                    foreach (var group in DevQuestions.Groups)
                        await output.WriteLineAsync($"{group}: {dev.Count(q => q.Category == group)}");
                    break;
                case "dev-overlap":
                    var overlap = Parse(args, "--hidden-eval", "--retained");
                    await OverlapAsync(overlap["--hidden-eval"], overlap["--retained"], output);
                    break;
                case "dev-split":
                    var split = Parse(args, "--retained", "--out-dir");
                    await SplitAsync(split["--retained"], split["--out-dir"], output);
                    break;
                case "dev-plant":
                    var plant = Parse(args, "--out-dir");
                    await DevPlantRun.RunAsync(ExternalOutputDirectory.Validate(plant["--out-dir"]), output);
                    break;
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (mode == "dev-overlap")
                await error.WriteLineAsync($"dev-overlap failed. Use {usage} and check local bge-m3.");
            else if (mode == "dev-plant")
                await error.WriteLineAsync($"dev-plant failed ({ex.GetType().Name}). Use --out-dir with dev-kiem.csv, dev-kiem/dev-nguon.json, a new workbook path and local qwen2.5:7b.");
            else
                await error.WriteLineAsync($"{mode} failed ({ex.GetType().Name}). Use {usage} and check the RQ2 dev inputs and output paths.");
            return 1;
        }
    }

    private static async Task OverlapAsync(string hiddenPath, string retainedPath, TextWriter output)
    {
        if (!hiddenPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            !retainedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid overlap paths.");
        var retained = ExternalFile(retainedPath);
        StatisticsReport.EnsureNew(retained);
        var dev = await DevQuestions.LoadRq2Async();
        var hidden = DevOverlap.ReadHiddenTexts(hiddenPath);
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) });
        var model = new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions
            { IsEnabled = true, EmbeddingModel = "bge-m3", TimeoutSeconds = 120 }));
        async Task<float[][]> EmbedAsync(IEnumerable<string> texts)
        {
            var vectors = new List<float[]>();
            foreach (var batch in texts.Chunk(16))
            {
                var result = await model.EmbedAsync(batch, CancellationToken.None);
                if (result.Value?.Length != batch.Length) throw new InvalidDataException("Embedding batch failed.");
                vectors.AddRange(result.Value);
            }
            return vectors.ToArray();
        }

        var devVectors = await EmbedAsync(dev.Select(q => q.Text));
        var hiddenVectors = await EmbedAsync(hidden);
        var result = DevOverlap.Compare(dev, hidden, devVectors, hiddenVectors);
        await StatisticsReport.SaveAsync(result.Retained.Select(q => q.Stt).ToArray(), retained);
        await DevOverlap.WriteSummaryAsync(result, output);
    }

    private static async Task SplitAsync(string retainedPath, string outputDirectory, TextWriter output)
    {
        var directory = ExternalOutputDirectory.Validate(outputDirectory);
        var check = Path.Combine(directory, "dev-kiem.csv");
        var main = Path.Combine(directory, "dev-chinh.csv");
        StatisticsReport.EnsureNew(check);
        StatisticsReport.EnsureNew(main);
        var dev = await DevQuestions.LoadRq2Async();
        var retained = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(retainedPath))
            ?? throw new InvalidDataException("Missing retained dev codes.");
        var codes = retained.ToHashSet(StringComparer.Ordinal);
        var filtered = dev.Where(q => codes.Contains(q.Stt!)).ToArray();
        if (retained.Length != filtered.Length)
            throw new InvalidDataException("Invalid retained dev codes.");
        var split = DevSplit.Create(filtered, DevQuestions.Seed);
        Directory.CreateDirectory(directory);
        WriteNew(check, DevQuestions.Csv(split.Check));
        WriteNew(main, DevQuestions.Csv(split.Main));
        await output.WriteLineAsync($"dev-kiem: {split.Check.Length}; dev-chinh: {split.Main.Length}; seed: {DevQuestions.Seed}");
        foreach (var group in split.Groups)
            await output.WriteLineAsync($"{group.Group}: mục tiêu {group.Target}, dev-kiem {group.Check}, dev-chinh {group.Main}, thiếu {group.Shortage}");
    }

    private static string ExternalFile(string path)
    {
        var file = Path.GetFullPath(path);
        return Path.Combine(ExternalOutputDirectory.Validate(Path.GetDirectoryName(file)!), Path.GetFileName(file));
    }

    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        writer.Write(content);
    }

    private static Dictionary<string, string> Parse(string[] args, params string[] required)
    {
        if (args.Length != required.Length * 2)
            throw new ArgumentException("Missing calibration options.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!required.Contains(args[i]) || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unsupported or duplicate calibration option.");
        }
        return options;
    }
}
