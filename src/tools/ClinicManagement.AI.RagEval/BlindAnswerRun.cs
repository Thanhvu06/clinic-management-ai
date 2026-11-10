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
        if (File.Exists(metadataPath))
            throw new IOException("Run metadata already exists; choose a new --out-dir.");
        var measuredAtUtc = DateTimeOffset.UtcNow;
        var result = await BlindAnswerExport.RunAsync(options, runtime.AnswerAsync, console, ct);
        var metadata = JsonSerializer.Serialize(new
        {
            seed = result.Seed,
            retrieval = runtime.Retrieval,
            count = result.Count,
            measuredAtUtc,
            model = runtime.ChatModel
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, metadata + Environment.NewLine, ct);
        await console.WriteLineAsync(metadataPath);
        return result;
    }
}
