using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private sealed record Side(int? Score, int RunsCompleted, bool CitationError = false, JudgeGradeDiagnostics? Diagnostics = null,
        bool Uncertain = false);
    private sealed record CheckpointRow(int Row, Side A, Side B);
    private sealed record MasterRow(string Category, string ModeA, string ModeB, string Retrieval);

    private sealed class ScoreCounts(bool v3)
    {
        public int Scored { get; private set; }
        public int Blank { get; private set; }
        [JsonIgnore]
        public int Flagged { get; private set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? CitationUnverified => v3 ? null : Flagged;
        [JsonPropertyName("format_invalid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? FormatInvalid => v3 ? Flagged : null;
        public int Uncertain { get; private set; }
        public int[] Scores { get; } = new int[3];
        public void Add(Side grade)
        {
            if (grade.Score is int score)
            {
                Scored++;
                Scores[score]++;
            }
            else Blank++;
            if (grade.Diagnostics?.HasFormatFlag == true) Flagged++;
            if (grade.Uncertain) Uncertain++;
        }
    }

    private sealed class CategoryCounts(bool v3)
    {
        public ScoreCounts A { get; } = new(v3);
        public ScoreCounts B { get; } = new(v3);
        public int FullyGraded { get; private set; }
        public int UncertainQuestions { get; private set; }
        public void Add(CheckpointRow row)
        {
            if (row.A.Score.HasValue && row.B.Score.HasValue) FullyGraded++;
            if (row.A.Uncertain || row.B.Uncertain) UncertainQuestions++;
        }
    }

    private sealed class FailureCounts(bool v3)
    {
        private long _retryTotal;
        public int Blank { get; private set; }
        public int CitationErrorFlagged { get; private set; }
        public int RetrySamples { get; private set; }
        public double? AverageRetries => RetrySamples == 0 ? null : (double)_retryTotal / RetrySamples;

        [JsonIgnore]
        public int Flagged { get; private set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? CitationUnverified => v3 ? null : Flagged;
        [JsonPropertyName("format_invalid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? FormatInvalid => v3 ? Flagged : null;
        public void Add(Side side)
        {
            if (side.Score is null) Blank++;
            if (side.Diagnostics?.HasFormatFlag == true) Flagged++;
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
        var v3 = version == 3;
        var flagName = v3 ? "format_invalid" : "citation_unverified";
        var byCategory = QuestionCategories.All.ToDictionary(name => name, _ => new CategoryCounts(v3));
        var byRetrieval = NewScores(v3, "Có tài liệu", "Trả rỗng", Unknown);
        var byMode = NewScores(v3, "LLM", "Trích đoạn", Unknown);
        var failures = new[] { "Trích dẫn không khớp nguyên văn tài liệu", "Không có tài liệu để trích",
            "Thiếu trường trích dẫn", "Lỗi JSON-schema", "Khác", "Mã không tồn tại", "Thiếu mã T", "Hết thời gian", "Thiếu mã C", Unknown };
        if (v3) failures[(int)JudgeFailureKind.NoDocuments] = "Vi phạm nguồn rỗng";
        var byFailure = failures.ToDictionary(name => name, _ => new FailureCounts(v3));
        if (v3) byFailure.Remove("Khác");
        var totalFailures = new FailureCounts(v3);
        foreach (var (row, grade) in grades)
        {
            var metadata = master[row];
            var category = byCategory[metadata.Category];
            category.Add(grade);
            Add(grade.A, category.A, byMode[metadata.ModeA], byRetrieval[Retrieval(grade.A, metadata)]);
            Add(grade.B, category.B, byMode[metadata.ModeB], byRetrieval[Retrieval(grade.B, metadata)]);
        }

        await StatisticsReport.SaveAsync(new { questions = grades.Count, byCategory, byRetrieval, byMode, byFailure, totalFailures }, output, ct);
        await console.WriteLineAsync("Nhóm | A có điểm | A trống | B có điểm | B trống");
        foreach (var (name, count) in byCategory)
            await console.WriteLineAsync($"{name} | {count.A.Scored} | {count.A.Blank} | {count.B.Scored} | {count.B.Blank}");
        if (v3)
        {
            await console.WriteLineAsync("Nhóm | Đủ A/B | Lệch 2 lần | A có cờ | B có cờ | A 0/1/2 | B 0/1/2");
            foreach (var (name, count) in byCategory)
                await console.WriteLineAsync($"{name} | {count.FullyGraded} | {count.UncertainQuestions} | {count.A.Flagged} | {count.B.Flagged} | {string.Join("/", count.A.Scores)} | {string.Join("/", count.B.Scores)}");
        }
        await PrintScores("Retrieval", byRetrieval);
        await PrintScores("Chế độ", byMode);
        if (v3)
        {
            await console.WriteLineAsync("Chế độ | Số ô lệch 2 lần | Điểm 0/1/2");
            foreach (var (name, count) in byMode)
                await console.WriteLineAsync($"{name} | {count.Uncertain} | {string.Join("/", count.Scores)}");
        }
        await console.WriteLineAsync($"Lỗi cuối | Ô trống | Ô {flagName} | Ô có cờ lỗi trích dẫn | Ô có số retry | Retry trung bình");
        foreach (var (name, count) in byFailure.Append(new("Tổng", totalFailures)))
            await console.WriteLineAsync($"{name} | {count.Blank} | {count.Flagged} | {count.CitationErrorFlagged} | {count.RetrySamples} | {StatisticsReport.Number(count.AverageRetries)}");

        string Retrieval(Side grade, MasterRow metadata) => grade.Diagnostics?.RetrievalHasDocuments switch
        {
            true => "Có tài liệu",
            false => "Trả rỗng",
            _ => version >= 2 ? Unknown : metadata.Retrieval
        };

        void Add(Side grade, ScoreCounts category, ScoreCounts mode, ScoreCounts retrieval)
        {
            category.Add(grade);
            mode.Add(grade);
            retrieval.Add(grade);
            if (grade.Score.HasValue && grade.Diagnostics?.HasFormatFlag != true && grade.Diagnostics?.FinalFailure is null) return;
            var name = grade.Diagnostics?.FinalFailure is JudgeFailureKind failure ? failures[(int)failure] : Unknown;
            if (version == 3 && grade.Diagnostics?.FinalFailure == JudgeFailureKind.Other)
            {
                name = $"Khác ({ExceptionLabel(grade.Diagnostics.ExceptionType)})";
                if (!byFailure.ContainsKey(name)) byFailure.Add(name, new FailureCounts(v3));
            }
            byFailure[name].Add(grade);
            totalFailures.Add(grade);
        }

        async Task PrintScores(string heading, Dictionary<string, ScoreCounts> counts)
        {
            await console.WriteLineAsync($"{heading} | Có điểm | Trống | {flagName}");
            foreach (var (name, count) in counts)
                await console.WriteLineAsync($"{name} | {count.Scored} | {count.Blank} | {count.Flagged}");
        }
    }

    private static string ExceptionLabel(string? type) => type switch
    {
        nameof(HttpRequestException) => nameof(HttpRequestException),
        nameof(InvalidOperationException) => nameof(InvalidOperationException),
        nameof(KeyNotFoundException) => nameof(KeyNotFoundException),
        _ => "Không rõ exception"
    };

    private static Dictionary<string, ScoreCounts> NewScores(bool v3, params string[] names) =>
        names.ToDictionary(name => name, _ => new ScoreCounts(v3));

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
        var reportName = Path.GetFileName(checkpoint) switch
        {
            "judge-checkpoint-v2.jsonl" => "judge-diagnose-v2.json",
            "judge-checkpoint-v3.jsonl" => "judge-diagnose-v3.json",
            _ => "judge-diagnose.json"
        };
        var output = values.TryGetValue("--output", out var name) ? Path.GetFullPath(name) :
            Path.Combine(Path.GetDirectoryName(checkpoint)!, reportName);
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
        if (header is not { Version: >= 1 and <= 3, Runs: > 0 } || header.Id == Guid.Empty)
            throw new InvalidDataException("Invalid checkpoint header.");
        var rows = new Dictionary<int, CheckpointRow>();
        var position = headerEnd + 1;
        while (position < bytes.Length)
        {
            var end = bytes[position..].IndexOf((byte)'\n');
            if (end < 0) break;
            var row = JsonSerializer.Deserialize<CheckpointRow>(bytes.Slice(position, end), JsonOptions);
            if (row is null || row.Row < 4 || !Valid(row.A, header) || !Valid(row.B, header) ||
                rows.TryGetValue(row.Row, out var prior) && prior.A.Score.HasValue && prior.B.Score.HasValue)
                throw new InvalidDataException("Invalid checkpoint row.");
            rows[row.Row] = row;
            position += end + 1;
        }
        return (rows, header.Version);
    }

    private static bool Valid(Side? side, JudgeCheckpointHeader header) => side is not null &&
        side.RunsCompleted >= 0 && side.RunsCompleted <= header.Runs &&
        ((side.RunsCompleted == header.Runs || header.Version == 3 && side.Score.HasValue && side.RunsCompleted > 0)
            ? side.Score is >= 0 and <= 2 : side.Score is null) &&
        (side.Diagnostics is not { } diagnostics ||
            diagnostics.RetryCount >= 0 && diagnostics.RetryCount <= 2L * header.Runs &&
            (header.Version == 3 || !diagnostics.HasFormatFlag || side.Score.HasValue) &&
            (diagnostics.FinalFailure is not JudgeFailureKind failure ||
                (header.Version == 3 || side.Score is null || diagnostics.HasFormatFlag) && Enum.IsDefined(failure)));

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
