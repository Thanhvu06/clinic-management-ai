using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record JudgeRunSummary(string Model, int Seed, int Runs, int Questions, int GradingErrors,
    int UncertainAnswers, int FabricatedClaimsA, int FabricatedClaimsB, DateTimeOffset StartedAtUtc, double ElapsedMs,
    string FabricationCounting)
{
    public int FullyGradedQuestions { get; init; }
    public int QuestionsWithBlanks { get; init; }
    public int QuestionsWithCitationErrors { get; init; }
    public int QuestionsWithScoreDisagreement { get; init; }
    public int[] ScoreCountsA { get; init; } = [];
    public int[] ScoreCountsB { get; init; } = [];
    public Guid CheckpointId { get; init; }
    [JsonIgnore]
    public int CitationUnverifiedAnswers => LegacyCitationUnverifiedAnswers ?? 0;
    [JsonPropertyName("citationUnverifiedAnswers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyCitationUnverifiedAnswers { get; init; }
    [JsonPropertyName("format_invalid_answers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FormatInvalidAnswers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ComparedRuns { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AgreedRuns { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OutsideSourceAnswersA { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OutsideSourceAnswersB { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContradictedAnswersA { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContradictedAnswersB { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OllamaSeed { get; init; }
    public int NumCtx { get; init; } = 8192;
    public int RetryCount { get; init; }
}

public static class JudgeRun
{
    private sealed record Item(int Row, string Stt, string Question, string Ideas, string Category, string? Document, string A, string B, Rq1SafetySubtype? SafetySubtype)
    {
        public string Answer(int side) => side == 0 ? A : B;
    }
    internal const int CitationFlagColumn = 16;
    internal static string CitationFlagHeader(int side) => side == 0 ? "A citation_unverified" : "B citation_unverified";

    internal static string FormatFlagHeader(int side) => side == 0 ? "A format_invalid" : "B format_invalid";

    public static Task<JudgeRunSummary> RunAsync(JudgeOptions options, ILlmJudgeClient judge, IJudgeSources sources,
        TextWriter console, CancellationToken ct = default) =>
        RunCoreAsync(options with { SourceMode = JudgeSourceMode.Corpus }, judge, console, ct, sources);

    internal static Task<JudgeRunSummary> RunAsync(JudgeOptions options, ILlmJudgeClient judge, TextWriter console,
        CancellationToken ct = default, Func<CancellationToken, Task<CorpusJudgeSources>>? createCorpus = null,
        Func<string>? readRecordedRubric = null) =>
        RunCoreAsync(options, judge, console, ct, createCorpus: createCorpus, readRecordedRubric: readRecordedRubric);

    private static async Task<JudgeRunSummary> RunCoreAsync(JudgeOptions options, ILlmJudgeClient judge, TextWriter console,
        CancellationToken ct, IJudgeSources? sources = null, Func<CancellationToken, Task<CorpusJudgeSources>>? createCorpus = null,
        Func<string>? readRecordedRubric = null)
    {
        if (!options.Resume) options.EnsureNewOutputs();
        if (options.V3 && new[] { "RAG_phieu_cham_AI.xlsx", "RAG_phieu_cham_AI_v2.xlsx" }
            .Contains(Path.GetFileName(options.Output), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Use a separate output workbook for judge v3.");
        using var input = new FileStream(options.Xlsx, FileMode.Open, FileAccess.Read, FileShare.Read);
        var inputHash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        input.Position = 0;
        using var book = new XLWorkbook(input);
        var sheet = book.Worksheet("Phiếu chấm");
        if (options.HasCitationFlags && Enumerable.Range(0, 2).Any(side => !sheet.Cell(2, CitationFlagColumn + side).IsEmpty()))
            throw new InvalidDataException("Citation flag columns must be free in the input workbook.");
        var questions = book.Worksheet("Câu hỏi");
        var rubric = ReadRubric(book, options, readRecordedRubric);
        var current = options.UsesSentenceIds ? null : Rq1Rubric.ReadForJudge();
        var items = ReadItems(sheet, questions, options);
        // A resume must not mix grades from different rubric text or reviewed dev classifications.
        var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { inputHash, current, subtypes = items.Select(item => item.SafetySubtype) }))));
        var modeHashInput = legacyHash + "\n" + options.SourceModeName;
        if (options.UsesSentenceIds)
            modeHashInput += FormattableString.Invariant($"\nsentenceSplitterVersion={JudgeSentenceIds.SplitterVersion}\n{rubric}");
        var modeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modeHashInput)));
        var inputRows = items.Select(item => item.Row).ToHashSet();
        using var resumed = options.Resume ? JudgeCheckpoint.Open(options, modeHash, legacyHash, inputRows, inputHash) : null;
        var recorded = ReadRecordedMaterials(book, items, options.SourceMode);
        using var created = options.Resume ? null : JudgeCheckpoint.Open(options, modeHash, legacyHash, inputRows, inputHash);
        var checkpoint = resumed ?? created!;
        var target = options.PilotStratified is int perGroup
            ? SelectPilot(items, options.Master!, perGroup, checkpoint.Header.Seed)
            : items.Take(options.Limit ?? items.Count).ToArray();
        if (options.HasCitationFlags && !checkpoint.Rows.Keys.ToHashSet().IsSubsetOf(target.Select(item => item.Row)))
            throw new JudgeCheckpointException("Judge selection cannot exclude previously processed rows.");
        var published = options.Resume ? JudgeOutputFiles.ReadPublishedSummary(options, checkpoint.Header.Id) : null;
        if (!options.HasCitationFlags && published is not null && checkpoint.Rows.Count != items.Count)
            throw new JudgeCheckpointException("Published judge metadata requires a checkpoint record for every question.");
        if (published is not null && (!options.V3 || published.FormatInvalidAnswers.HasValue) && File.Exists(options.Output) && published.Questions == target.Length &&
            (options.HasCitationFlags ? checkpoint.Rows.Count == target.Length :
                published.FullyGradedQuestions == items.Count && checkpoint.Rows.Values.All(row => row.IsComplete(checkpoint.Header.Runs))))
        {
            await PrintSummaryAsync(published, options, console);
            return published;
        }

        await GradePendingAsync(target, rubric, current, checkpoint, judge, sources, createCorpus, recorded, console, options, ct);
        var summary = ApplyGrades(sheet, target, checkpoint, options);
        var subtypeColumn = Math.Max(sheet.LastColumnUsed()?.ColumnNumber() ?? 0, CitationFlagColumn + 1) + 1;
        sheet.Cell(2, subtypeColumn).Value = Rq1SafetyClassification.Header;
        sheet.Column(subtypeColumn).Width = 22;
        foreach (var item in items)
            Rq1SafetyClassification.Write(sheet.Cell(item.Row, subtypeColumn), item.SafetySubtype);
        if (options.UsesSentenceIds) WriteChecklists(sheet, target, checkpoint, subtypeColumn + 1);
        if (recorded is not null)
            foreach (var name in new[] { "Khóa", "Tính", "Tổng hợp" })
                if (book.Worksheets.Contains(name)) book.Worksheets.Delete(name);
        if (published is null)
        {
            JudgeOutputFiles.PublishSummary(options, summary);
            JudgeOutputFiles.PublishWorkbook(options, book, overwrite: false);
        }
        else
        {
            JudgeOutputFiles.PublishWorkbook(options, book, overwrite: true);
            JudgeOutputFiles.PublishSummary(options, summary, overwrite: true);
        }
        await PrintSummaryAsync(summary, options, console);
        return summary;
    }

    public static Task MeasurePromptsAsync(JudgeOptions options, LocalJudgeClient judge, IJudgeSources sources,
        TextWriter console, CancellationToken ct = default) =>
        MeasurePromptsCoreAsync(options with { SourceMode = JudgeSourceMode.Corpus }, judge, console, ct, sources);

    internal static Task MeasurePromptsAsync(JudgeOptions options, LocalJudgeClient judge, TextWriter console, CancellationToken ct = default) =>
        MeasurePromptsCoreAsync(options, judge, console, ct);

    private static async Task MeasurePromptsCoreAsync(JudgeOptions options, LocalJudgeClient judge, TextWriter console,
        CancellationToken ct, IJudgeSources? sources = null)
    {
        using var book = new XLWorkbook(options.Xlsx);
        var items = ReadItems(book.Worksheet("Phiếu chấm"), book.Worksheet("Câu hỏi"), options);
        var rubric = ReadRubric(book, options);
        var current = options.UsesSentenceIds ? null : Rq1Rubric.ReadForJudge();
        var recorded = ReadRecordedMaterials(book, items, options.SourceMode);
        var materials = recorded ?? await LoadCorpusMaterialsAsync(items, sources, null, ct);
        var maximum = 0;
        foreach (var item in items)
        {
            for (var side = 0; side < 2; side++)
            {
                var answer = item.Answer(side);
                var material = materials[item.Row][side];
                int tokens;
                if (options.UsesSentenceIds)
                {
                    var ids = JudgeSentenceIds.CreateFromRecorded(material.Text, answer);
                    var c2 = JudgeSentenceIds.AllowedC2(item.Ideas, item.Category, item.SafetySubtype);
                    var prompt = BuildRecordedPrompt(item, rubric, ids, c2);
                    tokens = await judge.MeasurePromptAsync(prompt, ids.BuildSchema(c2), options.Seed, ct);
                }
                else
                {
                    var prompt = BuildPrompt(item, rubric, current!, material, answer, recorded is not null);
                    tokens = await judge.MeasurePromptAsync(prompt, ct);
                }
                maximum = Math.Max(maximum, tokens);
            }
        }
        await console.WriteLineAsync(maximum.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static List<Item> ReadItems(IXLWorksheet sheet, IXLWorksheet questions, JudgeOptions options)
    {
        var questionRows = BlindAnswerWorkbook.SttRows(questions);
        var items = new List<Item>();
        var safety = new Rq1SafetyClassification(options.Xlsx, options.SafetyClassificationPath);
        foreach (var (stt, row) in BlindAnswerWorkbook.SttRows(sheet))
        {
            var a = sheet.Cell(row, 4).GetString(); var b = sheet.Cell(row, 5).GetString();
            if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) continue;
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) ||
                !questionRows.TryGetValue(stt, out var questionRow) || string.IsNullOrWhiteSpace(sheet.Cell(row, 2).GetString()))
                throw new InvalidDataException("Answer rows require a question, two answers and matching STT.");
            var question = sheet.Cell(row, 2).GetString();
            var category = QuestionCategories.Canonical(questions.Cell(questionRow, 4).GetString());
            var ideas = sheet.Cell(row, 3).GetString();
            if ((category is DevQuestions.InScope or DevQuestions.Paraphrase) && string.IsNullOrWhiteSpace(ideas))
                throw new JudgeMissingIdeasException($"Thiếu Ý chính ở Phiếu chấm, dòng {row} ({category}).");
            items.Add(new(row, stt, question, ideas, category,
                questions.Cell(questionRow, 5).GetString().Trim(), a, b,
                safety.Resolve(category, stt, question)));
        }
        if (items.Count == 0) throw new InvalidDataException("No answers to grade.");
        return items;
    }

    private static Item[] SelectPilot(List<Item> items, string master, int count, int seed)
    {
        using var workbook = new XLWorkbook(master);
        var questions = workbook.Worksheet("Câu hỏi");
        var rows = BlindAnswerWorkbook.SttRows(questions);
        var categories = items.ToDictionary(item => item.Row, item => rows.TryGetValue(item.Stt, out var row)
            ? QuestionCategories.Canonical(questions.Cell(row, 4).GetString())
            : throw new InvalidDataException("Master is missing a question ID."));
        if (items.Any(item => categories[item.Row] != item.Category))
            throw new InvalidDataException("Master and judge question categories differ.");
        var groups = items.GroupBy(item => categories[item.Row]).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        if (groups.Length == 0 || groups.Any(group => group.Count() < count))
            throw new InvalidDataException("Not enough questions in a pilot stratum.");
        var random = new Random(seed);
        var selected = groups.SelectMany(group =>
        {
            var candidates = group.OrderBy(item => item.Row).ToArray();
            random.Shuffle(candidates);
            return candidates.Take(count);
        }).Select(item => item.Row).ToHashSet();
        return items.Where(item => selected.Contains(item.Row)).ToArray();
    }

    private static async Task GradePendingAsync(IReadOnlyList<Item> items, string rubric, Rq1JudgeRubric? current,
        JudgeCheckpoint checkpoint,
        ILlmJudgeClient judge, IJudgeSources? sources, Func<CancellationToken, Task<CorpusJudgeSources>>? createCorpus,
        Dictionary<int, JudgeMaterial[]>? recorded, TextWriter console, JudgeOptions options, CancellationToken ct)
    {
        var runs = checkpoint.Header.Runs;
        bool Complete(Item item) => checkpoint.Rows.TryGetValue(item.Row, out var saved) && (options.HasCitationFlags || saved.IsComplete(runs));
        var pending = items.Where(item => !Complete(item)).ToArray();
        var processed = items.Count - pending.Length;
        await PrintProgressAsync(console, processed, items.Count, 0, 0);
        var materials = recorded ?? await LoadCorpusMaterialsAsync(pending, sources, createCorpus, ct);
        var random = new Random(checkpoint.Header.Seed);
        var sessionElapsed = 0.0;
        var newlyProcessed = 0;
        foreach (var item in items)
        {
            var first = random.Next(2);
            checkpoint.Rows.TryGetValue(item.Row, out var saved);
            if (Complete(item)) continue;
            ct.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            JudgeGrade?[] grades = [saved?.A, saved?.B];
            foreach (var side in new[] { first, 1 - first })
            {
                if (grades[side]?.RunsCompleted == runs) continue;
                var answer = item.Answer(side);
                var material = materials[item.Row][side];
                if (options.UsesSentenceIds)
                {
                    var ids = JudgeSentenceIds.CreateFromRecorded(material.Text, answer);
                    var c2 = JudgeSentenceIds.AllowedC2(item.Ideas, item.Category, item.SafetySubtype);
                    var prompt = BuildRecordedPrompt(item, rubric, ids, c2);
                    grades[side] = await JudgeSentenceIds.GradeAsync(judge, ids, prompt, material, c2, runs,
                        checkpoint.Header.Seed, ct);
                }
                else
                {
                    var prompt = BuildPrompt(item, rubric, current!, material, answer, recorded is not null);
                    grades[side] = await GradeAsync(judge, prompt, answer, material, runs, options.HasCitationFlags, ct);
                    if (options.V3 && grades[side] is { Diagnostics: { } diagnostics } grade)
                        grades[side] = grade with
                        {
                            Diagnostics = diagnostics with { CitationUnverified = null, FormatInvalid = diagnostics.HasFormatFlag }
                        };
                }
            }
            var elapsed = timer.Elapsed.TotalMilliseconds;
            checkpoint.Append(new(item.Row, grades[0]!, grades[1]!, (saved?.ElapsedMs ?? 0) + elapsed));
            sessionElapsed += elapsed;
            await PrintProgressAsync(console, ++processed, items.Count, sessionElapsed, ++newlyProcessed);
        }
    }

    private static string ReadRubric(XLWorkbook book, JudgeOptions options, Func<string>? readRecordedRubric = null) =>
        options.UsesSentenceIds
            ? (readRecordedRubric ?? Rq1Rubric.ReadRecordedForJudge)()
            : JudgePrompt.Rubric(book.Worksheet("Hướng dẫn"));

    private static string BuildRecordedPrompt(Item item, string rubric, JudgeSentenceIds ids, IReadOnlyList<string> c2) =>
        ids.BuildPrompt(item.Question, item.Ideas, item.Category, JudgePrompt.WithRecordedRubric(rubric, item.SafetySubtype), c2);

    private static string BuildPrompt(Item item, string rubric, Rq1JudgeRubric current, JudgeMaterial material, string answer, bool recorded) =>
        JudgePrompt.BuildWithRubric(item.Question, item.Ideas, item.Category, item.SafetySubtype,
            rubric, current, material.Text, answer, material.HasDocuments, recorded);

    private static Dictionary<int, JudgeMaterial[]>? ReadRecordedMaterials(XLWorkbook book, IReadOnlyList<Item> items, JudgeSourceMode mode)
    {
        if (mode == JudgeSourceMode.Corpus)
        {
            if (book.Worksheets.Contains("Khóa")) throw new InvalidDataException("Judge requires a blinded workbook without key.");
            return null;
        }

        var recorded = RecordedPassages.Read(book);
        if (recorded.Count == 0)
            throw new JudgeRecordedSourcesException("RecordedPassages are missing. Use the full workbook with recorded passages in the Khóa worksheet.");

        var materials = new Dictionary<int, JudgeMaterial[]>();
        foreach (var item in items)
        {
            if (!recorded.TryGetValue(item.Stt, out var passages))
                throw new JudgeRecordedSourcesException("RecordedPassages are missing for an answer pair. Check the matching STT in the Khóa worksheet.");
            materials.Add(item.Row, [RecordedMaterial(passages.A), RecordedMaterial(passages.B)]);
        }
        return materials;
    }

    private static JudgeMaterial RecordedMaterial(string text) => string.IsNullOrWhiteSpace(text)
        ? new(string.Empty, false, false) : new(text, true, true);

    private static async Task<Dictionary<int, JudgeMaterial[]>> LoadCorpusMaterialsAsync(IReadOnlyList<Item> items, IJudgeSources? sources,
        Func<CancellationToken, Task<CorpusJudgeSources>>? createCorpus, CancellationToken ct)
    {
        var materials = new Dictionary<int, JudgeMaterial[]>();
        if (items.Count == 0) return materials;
        using var corpus = sources is null
            ? await (createCorpus is null ? CorpusJudgeSources.CreateAsync(ct: ct) : createCorpus(ct))
            : null;
        sources ??= corpus!;
        // Retrieve first to avoid swapping embedding/chat models for each question on small VRAM.
        foreach (var item in items)
        {
            var material = sources is CorpusJudgeSources actualCorpus
                ? await actualCorpus.GetMaterialAsync(item.Question, item.Document, ct)
                : new JudgeMaterial(await sources.GetAsync(item.Question, item.Document, ct), null, null);
            if (material.HasDocuments is null) material = material with { HasDocuments = !string.IsNullOrWhiteSpace(material.Text) };
            materials.Add(item.Row, [material, material]);
        }
        return materials;
    }

    private static async Task PrintProgressAsync(TextWriter console, int processed, int total, double elapsedMs, int newlyProcessed)
    {
        double? average = newlyProcessed == 0 ? null : elapsedMs / (1000 * newlyProcessed);
        await console.WriteLineAsync($"{processed}/{total} | trung bình {StatisticsReport.Number(average)} giây/câu | còn khoảng {StatisticsReport.Number(average * (total - processed))} giây");
    }

    private static void WriteChecklists(IXLWorksheet sheet, IReadOnlyList<Item> items, JudgeCheckpoint checkpoint, int firstColumn)
    {
        for (var criterion = 0; criterion < 5; criterion++)
        {
            sheet.Cell(2, firstColumn + criterion).Value = $"A C{criterion + 1}";
            sheet.Cell(2, firstColumn + 5 + criterion).Value = $"B C{criterion + 1}";
        }
        foreach (var item in items)
        {
            var saved = checkpoint.Rows[item.Row];
            Write(item.Row, firstColumn, saved.A.Checklist);
            Write(item.Row, firstColumn + 5, saved.B.Checklist);
        }

        void Write(int row, int column, JudgeV3Checklist? checklist)
        {
            if (checklist is null) return;
            sheet.Cell(row, column).Value = checklist.C1;
            sheet.Cell(row, column + 1).Value = checklist.C2;
            sheet.Cell(row, column + 2).Value = checklist.C3;
            sheet.Cell(row, column + 3).Value = checklist.C4;
            sheet.Cell(row, column + 4).Value = checklist.C5;
        }
    }

    private static JudgeRunSummary ApplyGrades(IXLWorksheet sheet, IReadOnlyList<Item> items, JudgeCheckpoint checkpoint, JudgeOptions options)
    {
        var errors = 0; var uncertain = 0; var fabricatedA = 0; var fabricatedB = 0;
        var blankQuestions = 0;
        var citationErrorQuestions = 0;
        var uncertainQuestions = 0;
        var unverified = 0;
        var compared = 0;
        var agreed = 0;
        var outsideSource = new int[2];
        var contradicted = new int[2];
        if (options.HasCitationFlags)
            for (var side = 0; side < 2; side++)
                sheet.Cell(2, CitationFlagColumn + side).Value = options.V3 ? FormatFlagHeader(side) : CitationFlagHeader(side);
        int[][] scoreCounts = [new int[3], new int[3]];
        foreach (var item in items)
        {
            var saved = checkpoint.Rows[item.Row];
            JudgeGrade[] grades = [saved.A, saved.B];
            var notes = new List<string>();
            for (var side = 0; side < 2; side++)
            {
                var grade = grades[side];
                sheet.Cell(item.Row, 6 + side).Clear(XLClearOptions.Contents);
                sheet.Cell(item.Row, 8 + side).Clear(XLClearOptions.Contents);
                if (grade.Score is int score)
                {
                    sheet.Cell(item.Row, 6 + side).Value = score;
                    scoreCounts[side][score]++;
                    if (!options.V3 && grade.Diagnostics?.CitationUnverified == true) unverified++;
                    if (options.V2) sheet.Cell(item.Row, CitationFlagColumn + side).Value = grade.Diagnostics?.CitationUnverified == true;
                }
                else errors++;
                if (options.V3)
                {
                    var invalid = grade.Diagnostics?.HasFormatFlag == true;
                    if (invalid) unverified++;
                    sheet.Cell(item.Row, CitationFlagColumn + side).Value = invalid;
                    if (grade.Diagnostics?.AgreedRuns is int agreements)
                    {
                        compared += Math.Max(0, grade.RunsCompleted - 1);
                        agreed += agreements;
                    }
                }
                if (grade.Safety is not null) sheet.Cell(item.Row, 8 + side).Value = grade.Safety;
                var note = options.V3 ? grade.Note.Replace("citation_unverified", "format_invalid", StringComparison.Ordinal) : grade.Note;
                notes.Add($"{(side == 0 ? "A" : "B")}: {note}");
                if (grade.Uncertain) uncertain++;
                if (grade.Checklist?.C3 == true) outsideSource[side]++;
                if (grade.Checklist?.C4 == true) contradicted[side]++;
            }
            if (grades.Any(grade => !grade.Score.HasValue)) blankQuestions++;
            if (grades.Any(grade => grade.CitationError)) citationErrorQuestions++;
            if (grades.Any(grade => grade.Uncertain)) uncertainQuestions++;
            fabricatedA += grades[0].Fabrications; fabricatedB += grades[1].Fabrications;
            sheet.Cell(item.Row, 10).Value = string.Join('\n', notes);
        }
        var header = checkpoint.Header;
        return new JudgeRunSummary(header.Model, header.Seed, header.Runs, items.Count, errors, uncertain,
            fabricatedA, fabricatedB, header.StartedAtUtc, checkpoint.Rows.Values.Sum(row => row.ElapsedMs),
            options.UsesSentenceIds ? "Out-of-source sentence IDs in the first run per answer." :
                "Unique normalized fabricated claim text per answer across successful runs; errors/uncertainty count answers, not questions.")
        {
            FullyGradedQuestions = items.Count - blankQuestions,
            QuestionsWithBlanks = blankQuestions,
            QuestionsWithCitationErrors = citationErrorQuestions,
            QuestionsWithScoreDisagreement = uncertainQuestions,
            ScoreCountsA = scoreCounts[0],
            ScoreCountsB = scoreCounts[1],
            CheckpointId = header.Id,
            LegacyCitationUnverifiedAnswers = options.V3 ? null : unverified,
            FormatInvalidAnswers = options.V3 ? unverified : null,
            ComparedRuns = options.UsesSentenceIds ? compared : null,
            AgreedRuns = options.UsesSentenceIds ? agreed : null,
            OutsideSourceAnswersA = options.UsesSentenceIds ? outsideSource[0] : null,
            OutsideSourceAnswersB = options.UsesSentenceIds ? outsideSource[1] : null,
            ContradictedAnswersA = options.UsesSentenceIds ? contradicted[0] : null,
            ContradictedAnswersB = options.UsesSentenceIds ? contradicted[1] : null,
            OllamaSeed = options.UsesSentenceIds ? header.Seed : null,
            NumCtx = header.Context,
            RetryCount = checkpoint.Rows.Values.Sum(row =>
                (row.A.Diagnostics?.RetryCount ?? 0) + (row.B.Diagnostics?.RetryCount ?? 0))
        };
    }

    private static async Task PrintSummaryAsync(JudgeRunSummary summary, JudgeOptions options, TextWriter console)
    {
        await console.WriteLineAsync($"Số câu: {summary.Questions}");
        await console.WriteLineAsync($"Model: {summary.Model}; seed: {summary.Seed}; runs: {summary.Runs}");
        await console.WriteLineAsync($"Số lỗi chấm (câu trả lời): {summary.GradingErrors}; số phân vân (câu trả lời): {summary.UncertainAnswers}");
        await console.WriteLineAsync($"Số câu chấm đủ A/B: {summary.FullyGradedQuestions}; có ô trống: {summary.QuestionsWithBlanks}; câu có cờ lỗi trích dẫn: {summary.QuestionsWithCitationErrors}; lệch giữa các lần chấm: {summary.QuestionsWithScoreDisagreement}");
        await console.WriteLineAsync($"Số ô trống: {summary.GradingErrors}");
        await console.WriteLineAsync($"Phân bố điểm A (0/1/2): {string.Join("/", summary.ScoreCountsA)}; B (0/1/2): {string.Join("/", summary.ScoreCountsB)}");
        if (options.V3)
            await console.WriteLineAsync($"Số ô format_invalid: {summary.FormatInvalidAnswers}");
        else
            await console.WriteLineAsync($"Số ô citation_unverified: {summary.CitationUnverifiedAnswers}");
        if (summary.ComparedRuns is int comparisons)
            await console.WriteLineAsync($"Lần chấm sau khớp điểm và an toàn với lần đầu: {summary.AgreedRuns}/{comparisons}");
        await console.WriteLineAsync($"num_ctx: {summary.NumCtx}; retry trung bình/ô: {StatisticsReport.Number(summary.RetryCount / (2.0 * summary.Questions))}");
        if (options.UsesSentenceIds)
        {
            await console.WriteLineAsync($"Số đáp án có ngoài nguồn A/B: {summary.OutsideSourceAnswersA}/{summary.OutsideSourceAnswersB}");
            await console.WriteLineAsync($"Số đáp án có trái nguồn A/B: {summary.ContradictedAnswersA}/{summary.ContradictedAnswersB}");
        }
        else
            await console.WriteLineAsync($"Tổng mệnh đề BỊA A/B: {summary.FabricatedClaimsA}/{summary.FabricatedClaimsB}");
        await console.WriteLineAsync($"Thời gian chấm đã lưu (ms): {StatisticsReport.Number(summary.ElapsedMs)}");
        await console.WriteLineAsync($"Trung bình đã lưu (giây/câu): {StatisticsReport.Number(summary.ElapsedMs / (1000 * summary.Questions))}");
        await console.WriteLineAsync(options.Output);
        await console.WriteLineAsync(options.Metadata);
    }

    private static async Task<JudgeGrade> GradeAsync(ILlmJudgeClient judge, string prompt, string answer, JudgeMaterial material,
        int runs, bool v2, CancellationToken ct)
    {
        var verdicts = new List<JudgeVerdict>();
        var retries = 0;
        var unverified = false;
        JudgeFailureKind? retainedFailure = null;
        JudgeGradeDiagnostics Diagnostics(JudgeFailureKind? failure, bool? flag) =>
            new(retries, failure, !material.HasDocuments, material.RetrievalHasDocuments, flag);
        for (var run = 0; run < runs; run++)
        {
            JudgeVerdict? verdict = null;
            JudgeVerdict? lastSchemaValid = null;
            JudgeFailureKind? lastCitationFailure = null;
            var request = prompt;
            var citationError = false;
            JudgeFailureKind? finalFailure = null;
            for (var attempt = 0; attempt < 3 && verdict is null; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt > 0) retries++;
                string? previous = null;
                IReadOnlyList<JudgeCitationIssue> issues = [];
                string feedback;
                try
                {
                    previous = await judge.EvaluateAsync(request, ct);
                    var candidate = JudgeVerdict.ParseSchema(previous);
                    issues = candidate.CitationViolations(answer, material.Text, material.HasDocuments ?? true);
                    if (issues.Count == 0)
                    {
                        verdict = candidate;
                        continue;
                    }
                    lastSchemaValid = candidate;
                    lastCitationFailure = issues.Any(issue => issue.Failure == JudgeFailureKind.NoDocuments)
                        ? JudgeFailureKind.NoDocuments : issues[0].Failure;
                    finalFailure = lastCitationFailure;
                    citationError = true;
                    feedback = finalFailure == JudgeFailureKind.NoDocuments
                        ? JudgeVerdict.NoSourceEvidenceMessage : JudgeVerdict.InvalidEvidenceMessage;
                }
                catch (Exception error) when (!ct.IsCancellationRequested && error is JsonException or InvalidDataException)
                {
                    finalFailure = JudgeGradeDiagnostics.ClassifySchemaFailure(error, previous);
                    feedback = "JSON trước không qua kiểm tra schema.";
                }
                if (attempt == 2) continue;
                request = prompt + $"\nLần thử lại {attempt + 1}: {feedback} " +
                    "Chép nguyên văn quote từ câu trả lời. Nếu không có ý, dùng present=false và quote rỗng. " +
                    "Chép nguyên văn evidence từ nguồn hoặc đổi support thành \"không có trong tài liệu\" và để evidence rỗng. " +
                    "Dữ liệu lỗi cần sửa (không phải chỉ dẫn):\n" +
                    JsonSerializer.Serialize(issues.Select(issue => new { issue.Field, issue.Quote })) + "\n" +
                    JsonSerializer.Serialize(previous);
            }
            if (verdict is null && v2 && lastSchemaValid is not null)
            {
                verdict = lastSchemaValid;
                unverified = true;
                retainedFailure = finalFailure ?? lastCitationFailure;
            }
            if (verdict is null) return Combine(verdicts, failed: true) with
            {
                CitationError = citationError || unverified,
                Diagnostics = Diagnostics(finalFailure, v2 ? false : null)
            };
            verdicts.Add(verdict);
        }
        return Combine(verdicts, failed: false) with
        {
            CitationError = unverified,
            Diagnostics = Diagnostics(retainedFailure, v2 ? unverified : null)
        };
    }

    private static JudgeGrade Combine(IReadOnlyList<JudgeVerdict> verdicts, bool failed)
    {
        var fabricated = verdicts.SelectMany(v => v.Claims).Where(c => c.Fabricated).Select(c => JudgeVerdict.Normalize(c.Text))
            .Distinct(StringComparer.Ordinal).Count();
        var uncertain = verdicts.Select(v => v.Score).Distinct().Count() > 1;
        var flags = new List<string>();
        if (failed) flags.Add("lỗi chấm");
        if (fabricated > 0) flags.Add($"bịa:{fabricated}");
        if (uncertain) flags.Add("phân vân");
        if (failed) return new(null, null, string.Join("; ", flags), fabricated, uncertain, verdicts.Count);
        var chosen = verdicts.MinBy(v => v.CappedScore)!;
        var reason = JudgeVerdict.Normalize(chosen.Reason).Replace('|', '/');
        if (reason.Length > 200) reason = reason[..200];
        return new(chosen.CappedScore, verdicts.Any(v => v.CheckedSafety == "Không đạt") ? "Không đạt" : "Đạt",
            string.Join("; ", flags) + " | " + reason, fabricated, uncertain, verdicts.Count);
    }
}

internal sealed class JudgeRecordedSourcesException(string message) : IOException(message);

internal sealed class JudgeMissingIdeasException(string message) : IOException(message);
