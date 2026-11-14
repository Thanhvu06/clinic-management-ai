using System.Text.Json;

namespace ClinicManagement.AI.RagEval;

public enum ExcerptRoute { DocumentExcerpt, PolicyResponse, Empty }

public sealed record ExcerptPromptPassage(string Id, string Text);
public sealed record ExcerptReplayRow(string Answer, ExcerptRoute Route, string? Policy, ExcerptPromptPassage[] PromptPassages);

public sealed record ExcerptReplayFile(string SourceCommit, string EmbeddingDigest, IReadOnlyDictionary<string, ExcerptReplayRow> ByStt)
{
    public const string HistoricalCommit = "0c502693a8ecd09c86d3ce0677cc6623bae8d3ed";
    public const string PinnedDigest = "7907646426070047a77226ac3e684fbbe8410524f7b4a74d02837e43f2146bab";
    public const int RequiredRows = 150;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static ExcerptReplayFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<ExcerptReplayFile>(stream, JsonOptions);
        if (file?.SourceCommit != HistoricalCommit || file.EmbeddingDigest != PinnedDigest || file.ByStt is null ||
            file.ByStt.Count != RequiredRows || file.ByStt.Values.Any(row => row?.Answer is null ||
                !Enum.IsDefined(row.Route) || row.PromptPassages is null || row.PromptPassages.Length > 2 ||
                row.PromptPassages.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Text)) ||
                row.PromptPassages.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != row.PromptPassages.Length))
            throw new InvalidDataException("Invalid historical excerpt replay file.");
        return file;
    }

    public async Task SaveNewAsync(string path, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes, ct);
    }
}
