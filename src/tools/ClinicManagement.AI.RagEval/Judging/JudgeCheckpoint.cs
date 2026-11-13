using System.Text.Json;

namespace ClinicManagement.AI.RagEval;

internal sealed record JudgeGrade(int? Score, string? Safety, string Note, int Fabrications, bool Uncertain,
    int RunsCompleted, bool CitationError = false)
{
    public bool IsValid(int runs) => RunsCompleted >= 0 && RunsCompleted <= runs && Fabrications >= 0 &&
        Note is not null && (RunsCompleted == runs
            ? Score is >= 0 and <= 2 && Safety is "Đạt" or "Không đạt" && !CitationError
            : Score is null && Safety is null);
}

internal sealed record JudgeCheckpointRow(int Row, JudgeGrade A, JudgeGrade B, double ElapsedMs)
{
    public bool IsComplete(int runs) => A.RunsCompleted == runs && B.RunsCompleted == runs;
}

internal sealed record JudgeCheckpointHeader(int Version, Guid Id, string InputSha256, string OutputFile,
    string Model, int Runs, int Seed, DateTimeOffset StartedAtUtc);

internal sealed class JudgeCheckpointException(string message) : IOException(message);

internal sealed class JudgeCheckpoint : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true
    };

    private readonly FileStream _stream;
    private readonly Dictionary<int, JudgeCheckpointRow> _rows;

    private JudgeCheckpoint(FileStream stream, JudgeCheckpointHeader header, Dictionary<int, JudgeCheckpointRow> rows)
    {
        _stream = stream;
        Header = header;
        _rows = rows;
    }

    public JudgeCheckpointHeader Header { get; }
    public IReadOnlyDictionary<int, JudgeCheckpointRow> Rows => _rows;

    public static JudgeCheckpoint Open(JudgeOptions options, string inputSha256, IReadOnlySet<int> inputRows)
    {
        if (!options.Resume)
        {
            if (File.Exists(options.Checkpoint))
                throw new JudgeCheckpointException("Judge checkpoint already exists. Use --resume or a different output directory.");
            var initial = new JudgeCheckpointHeader(1, Guid.NewGuid(), inputSha256, Path.GetFileName(options.Output),
                options.Model, options.Runs, options.Seed, DateTimeOffset.UtcNow);
            JudgeOutputFiles.Publish(options.Checkpoint, path =>
            {
                using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                WriteLine(writer, initial);
            });
        }
        else if (!File.Exists(options.Checkpoint))
            throw new JudgeCheckpointException("No judge checkpoint exists for --resume.");

        var stream = new FileStream(options.Checkpoint, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            var headerEnd = bytes.IndexOf((byte)'\n');
            if (headerEnd < 0) throw new JudgeCheckpointException("Judge checkpoint header is incomplete.");
            var header = Read<JudgeCheckpointHeader>(bytes[..headerEnd]);
            if (header.Version != 1 || header.Id == Guid.Empty || header.InputSha256 != inputSha256 ||
                header.OutputFile != Path.GetFileName(options.Output) || header.Model != options.Model ||
                header.Runs != options.Runs || options.SeedSpecified && header.Seed != options.Seed)
                throw new JudgeCheckpointException("Judge checkpoint parameters differ (input SHA-256, output, model, runs or seed).");

            var rows = new Dictionary<int, JudgeCheckpointRow>();
            var position = headerEnd + 1;
            while (position < bytes.Length)
            {
                var end = bytes[position..].IndexOf((byte)'\n');
                if (end < 0) break;
                var row = Read<JudgeCheckpointRow>(bytes.Slice(position, end));
                if (!inputRows.Contains(row.Row) || row.A is null || row.B is null ||
                    !row.A.IsValid(header.Runs) || !row.B.IsValid(header.Runs) ||
                    !double.IsFinite(row.ElapsedMs) || row.ElapsedMs < 0 ||
                    rows.TryGetValue(row.Row, out var previous) && previous.IsComplete(header.Runs))
                    throw new JudgeCheckpointException("Invalid judge checkpoint row.");
                rows[row.Row] = row;
                position += end + 1;
            }
            // The terminating LF marks a committed record. Remove a torn tail before appending.
            if (position < stream.Length)
            {
                stream.SetLength(position);
                stream.Flush(flushToDisk: true);
            }
            stream.Position = position;
            return new(stream, header, rows);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Append(JudgeCheckpointRow row)
    {
        WriteLine(_stream, row);
        _rows[row.Row] = row;
    }

    private static T Read<T>(ReadOnlySpan<byte> line) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new JudgeCheckpointException("Judge checkpoint contains an invalid committed JSON line.");
        }
    }

    private static void WriteLine<T>(FileStream stream, T record)
    {
        stream.Write(JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions));
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    public void Dispose() => _stream.Dispose();
}
