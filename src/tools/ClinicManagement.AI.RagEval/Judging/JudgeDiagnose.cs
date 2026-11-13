using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public static class JudgeDiagnose
{
    private const string Unknown = "Không rõ";
    private const string RetrievalHeader = "Retrieval có tài liệu";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true
    };

    private sealed record Side(int? Score, int RunsCompleted, bool CitationError = false, JudgeGradeDiagnostics? Diagnostics = null);
    private sealed record CheckpointRow(int Row, Side A, Side B);
    private sealed record MasterRow(string Category, string ModeA, string ModeB, string Retrieval);

    private sealed class ScoreCounts
    {
        public int Scored { get; private set; }
        public int Blank { get; private set; }
        public int CitationUnverified { get; private set; }
        public void Add(Side grade)
        {
            if (grade.Score.HasValue) Scored++;
            else Blank++;
            if (grade.Diagnostics?.CitationUnverified == true) CitationUnverified++;
        }
    }

    private sealed record CategoryCounts(ScoreCounts A, ScoreCounts B);

    private sealed class FailureCounts
    {
        private long _retryTotal;
        public int Blank { get; private set; }
        public int CitationErrorFlagged { get; private set; }
        public int RetrySamples { get; private set; }
        public double? AverageRetries => RetrySamples == 0 ? null : (double)_retryTotal / RetrySamples;

        public int CitationUnverified { get; private set; }
        public void Add(Side side)
        {
            if (side.Score is null) Blank++;
            if (side.Diagnostics?.CitationUnverified == true) CitationUnverified++;
            if (side.CitationError) CitationErrorFlagged++;
            if (side.Diagnostics?.RetryCount is not int count) return;
            RetrySamples++;
            _retryTotal += count;
        }
    }

    public static async Task RunAsync(string[] args, TextWriter console, CancellationToken ct = default)
    {
        var (checkpointPath, masterPath, output) = Parse(args);
        StatisticsReport.EnsureNew(output);
        var (grades, version) = ReadCheckpoint(checkpointPath);
        var master = ReadMaster(masterPath, grades.Keys);
        var byCategory = QuestionCategories.All.ToDictionary(name => name, _ => new CategoryCounts(new(), new()));
        var byRetrieval = NewScores("Có tài liệu", "Trả rỗng", Unknown);
        var byMode = NewScores("LLM", "Trích đoạn", Unknown);
        var failures = new[] { "Trích dẫn không khớp nguyên văn tài liệu", "Không có tài liệu để trích",
            "Thiếu trường trích dẫn", "Lỗi JSON-schema", "Khác", Unknown };
        var byFailure = failures.ToDictionary(name => name, _ => new FailureCounts());
        var totalFailures = new FailureCounts();
        foreach (var (row, grade) in grades)
        {
            var metadata = master[row];
            var category = byCategory[metadata.Category];
            Add(grade.A, category.A, byMode[metadata.ModeA], byRetrieval[Retrieval(grade.A, metadata)]);
            Add(grade.B, category.B, byMode[metadata.ModeB], byRetrieval[Retrieval(grade.B, metadata)]);
        }

        await StatisticsReport.SaveAsync(new { questions = grades.Count, byCategory, byRetrieval, byMode, byFailure, totalFailures }, output, ct);
        await console.WriteLineAsync("Nhóm | A có điểm | A trống | B có điểm | B trống");
        foreach (var (name, count) in byCategory)
            await console.WriteLineAsync($"{name} | {count.A.Scored} | {count.A.Blank} | {count.B.Scored} | {count.B.Blank}");
        await PrintScores("Retrieval", byRetrieval);
        await PrintScores("Chế độ", byMode);
        await console.WriteLineAsync("Lỗi cuối | Ô trống | Ô citation_unverified | Ô có cờ lỗi trích dẫn | Ô có số retry | Retry trung bình");
        foreach (var (name, count) in byFailure.Append(new("Tổng", totalFailures)))
            await console.WriteLineAsync($"{name} | {count.Blank} | {count.CitationUnverified} | {count.CitationErrorFlagged} | {count.RetrySamples} | {StatisticsReport.Number(count.AverageRetries)}");

        string Retrieval(Side grade, MasterRow metadata) => grade.Diagnostics?.RetrievalHasDocuments switch
        {
            true => "Có tài liệu",
            false => "Trả rỗng",
            _ => version == 2 ? Unknown : metadata.Retrieval
        };

        void Add(Side grade, ScoreCounts category, ScoreCounts mode, ScoreCounts retrieval)
        {
            category.Add(grade);
            mode.Add(grade);
            retrieval.Add(grade);
            if (grade.Score.HasValue && grade.Diagnostics?.CitationUnverified != true) return;
            var name = grade.Diagnostics?.FinalFailure is JudgeFailureKind failure ? failures[(int)failure] : Unknown;
            byFailure[name].Add(grade);
            totalFailures.Add(grade);
        }

        async Task PrintScores(string heading, Dictionary<string, ScoreCounts> counts)
        {
            await console.WriteLineAsync($"{heading} | Có điểm | Trống | Citation_unverified");
            foreach (var (name, count) in counts)
                await console.WriteLineAsync($"{name} | {count.Scored} | {count.Blank} | {count.CitationUnverified}");
        }
    }

    private static Dictionary<string, ScoreCounts> NewScores(params string[] names) =>
        names.ToDictionary(name => name, _ => new ScoreCounts());

    private static (string Checkpoint, string Master, string Output) Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--checkpoint" or "--master" or "--output") || i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Invalid judge-diagnose options.");
        }
        if (!values.TryGetValue("--checkpoint", out var checkpoint) || !values.TryGetValue("--master", out var master) ||
            !checkpoint.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || !master.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use --checkpoint and --master.");
        checkpoint = Path.GetFullPath(checkpoint);
        var output = values.TryGetValue("--output", out var name) ? Path.GetFullPath(name) :
            Path.Combine(Path.GetDirectoryName(checkpoint)!, Path.GetFileName(checkpoint) == "judge-checkpoint-v2.jsonl" ? "judge-diagnose-v2.json" : "judge-diagnose.json");
        if (!output.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a JSON output.");
        return (checkpoint, Path.GetFullPath(master), output);
    }

    private static (Dictionary<int, CheckpointRow> Rows, int Version) ReadCheckpoint(string path)
    {
        // A newline commits the row. The projection leaves judge notes and other content fields unread by the report.
        using var stream = File.OpenRead(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
        var headerEnd = bytes.IndexOf((byte)'\n');
        if (headerEnd < 0) throw new InvalidDataException("Missing checkpoint header.");
        var header = JsonSerializer.Deserialize<JudgeCheckpointHeader>(bytes[..headerEnd], JsonOptions);
        if (header is not { Version: 1 or 2, Runs: > 0 } || header.Id == Guid.Empty)
            throw new InvalidDataException("Invalid checkpoint header.");
        var rows = new Dictionary<int, CheckpointRow>();
        var position = headerEnd + 1;
        while (position < bytes.Length)
        {
            var end = bytes[position..].IndexOf((byte)'\n');
            if (end < 0) break;
            var row = JsonSerializer.Deserialize<CheckpointRow>(bytes.Slice(position, end), JsonOptions);
            if (row is null || row.Row < 4 || !Valid(row.A, header.Runs) || !Valid(row.B, header.Runs) ||
                rows.TryGetValue(row.Row, out var prior) && prior.A.Score.HasValue && prior.B.Score.HasValue)
                throw new InvalidDataException("Invalid checkpoint row.");
            rows[row.Row] = row;
            position += end + 1;
        }
        return (rows, header.Version);
    }

    private static bool Valid(Side? side, int runs) => side is not null &&
        side.RunsCompleted >= 0 && side.RunsCompleted <= runs &&
        (side.RunsCompleted == runs ? side.Score is >= 0 and <= 2 : side.Score is null) &&
        (side.Diagnostics is not { } diagnostics ||
            diagnostics.RetryCount >= 0 && diagnostics.RetryCount <= 2L * runs &&
            (diagnostics.CitationUnverified != true || side.Score.HasValue) &&
            (diagnostics.FinalFailure is not JudgeFailureKind failure ||
                (side.Score is null || diagnostics.CitationUnverified == true) && Enum.IsDefined(failure)));

    private static Dictionary<int, MasterRow> ReadMaster(string path, IEnumerable<int> gradeRows)
    {
        using var book = new XLWorkbook(path);
        var grades = book.Worksheet("Phiếu chấm");
        var questions = book.Worksheet("Câu hỏi");
        var key = book.Worksheet("Khóa");
        var questionRows = BlindAnswerWorkbook.SttRows(questions);
        var keyRows = BlindAnswerWorkbook.SttRows(key);
        var gradingRows = BlindAnswerWorkbook.SttRows(grades).ToDictionary(pair => pair.Value, pair => pair.Key);
        var retrievalColumn = questions.Row(2).CellsUsed()
            .Where(cell => cell.GetString().Trim() == RetrievalHeader).Select(cell => cell.Address.ColumnNumber).SingleOrDefault();
        var metadata = new Dictionary<int, MasterRow>();
        foreach (var row in gradeRows)
        {
            if (!gradingRows.TryGetValue(row, out var stt) || !questionRows.TryGetValue(stt, out var questionRow) ||
                !keyRows.TryGetValue(stt, out var keyRow))
                throw new InvalidDataException("Checkpoint and master question rows do not match.");
            var category = QuestionCategories.Canonical(questions.Cell(questionRow, 4).GetString());
            var (a, b) = key.Cell(keyRow, 2).GetString().Trim() switch
            {
                "LLM" => ("LLM", "Trích đoạn"),
                "Trích đoạn" => ("Trích đoạn", "LLM"),
                _ => (Unknown, Unknown)
            };
            var retrieval = retrievalColumn == 0 ? Unknown : ParseRetrieval(questions.Cell(questionRow, retrievalColumn));
            metadata.Add(row, new(category, a, b, retrieval));
        }
        return metadata;
    }

    private static string ParseRetrieval(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Boolean) return cell.GetBoolean() ? "Có tài liệu" : "Trả rỗng";
        return cell.GetString().Trim().ToLower(CultureInfo.InvariantCulture) switch
        {
            "có tài liệu" or "có" or "true" or "1" => "Có tài liệu",
            "trả rỗng" or "không" or "false" or "0" => "Trả rỗng",
            _ => Unknown
        };
    }
}
