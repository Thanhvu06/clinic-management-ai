using System.Text.Json;

namespace ClinicManagement.AI.RagEval;

public static class BlindAnswerRun
{
    public static async Task<BlindExportResult> RunAsync(BlindAnswerOptions options, BlindAnswerRuntime runtime,
        TextWriter console, CancellationToken ct = default)
    {
        if (options.Retrieval != runtime.Retrieval)
            throw new ArgumentException("Export retrieval must match the runtime retrieval mode.");
        var directory = ExternalOutputDirectory.Validate(options.OutputDirectory);
        var metadataPath = Path.Combine(directory, "chay-thu.json");
        var names = new List<string> { "phieu-cham.csv", "khoa.csv", "chay-thu.json" };
        if (options.DevProvenance) names.Add(DevBlindProvenance.FileName);
        if (options.XlsxPath is not null)
            names.AddRange([BlindAnswerWorkbook.FullFile, BlindAnswerWorkbook.Grader1File, BlindAnswerWorkbook.Grader2File]);
        if (names.Any(name => File.Exists(Path.Combine(directory, name)) || Directory.Exists(Path.Combine(directory, name))))
            throw new IOException("Output already exists; choose a new --out-dir.");
        using var workbook = options.XlsxPath is null ? null : new BlindAnswerWorkbook(options.XlsxPath);
        var answers = new List<BlindAnswerRow>();
        var provenance = options.DevProvenance ? new DevBlindProvenance() : null;
        var devQuestions = provenance is null ? null : await DevBlindProvenance.LoadDevCsvAsync(options.CsvPath);
        var measuredAtUtc = DateTimeOffset.UtcNow;
        var result = await BlindAnswerExport.RunAsync(options, runtime.AnswerAsync, console, ct,
            onAnswer: workbook is null ? null : row => answers.Add(runtime.WithPassages(row)), loadedQuestions: workbook?.Questions ?? devQuestions,
            onResponses: provenance is null ? null : provenance.Add);
        if (workbook is not null)
        {
            workbook.Write(directory, answers);
            foreach (var name in names.Where(name => name.EndsWith(".xlsx", StringComparison.Ordinal)))
                await console.WriteLineAsync(Path.Combine(directory, name));
        }
        var metadata = JsonSerializer.Serialize(new
        {
            seed = result.Seed,
            retrieval = runtime.Retrieval,
            count = result.Count,
            measuredAtUtc,
            model = runtime.ChatModel
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, metadata + Environment.NewLine, ct);
        if (provenance is not null)
            await StatisticsReport.SaveAsync(provenance.Answers, Path.Combine(directory, DevBlindProvenance.FileName), ct);
        await console.WriteLineAsync(metadataPath);
        return result;
    }
}
