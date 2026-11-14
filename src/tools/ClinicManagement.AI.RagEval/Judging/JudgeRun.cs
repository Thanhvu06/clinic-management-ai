using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public int CitationUnverifiedAnswers { get; init; }
}

public static class JudgeRun
{
    private sealed record Item(int Row, string Question, string Ideas, string Category, string? Document, string A, string B, Rq1SafetySubtype? SafetySubtype);
    internal const int CitationFlagColumn = 16;
    internal static string CitationFlagHeader(int side) => side == 0 ? "A citation_unverified" : "B citation_unverified";

    public static async Task<JudgeRunSummary> RunAsync(JudgeOptions options, ILlmJudgeClient judge, IJudgeSources sources,
        TextWriter console, CancellationToken ct = default)
    {
        if (!options.Resume) options.EnsureNewOutputs();
        using var input = new FileStream(options.Xlsx, FileMode.Open, FileAccess.Read, FileShare.Read);
        var inputHash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        input.Position = 0;
        using var book = new XLWorkbook(input);
        if (book.Worksheets.Contains("Khóa")) throw new InvalidDataException("Judge requires a blinded workbook without key.");
        var sheet = book.Worksheet("Phiếu chấm");
        if (options.V2 && Enumerable.Range(0, 2).Any(side => !sheet.Cell(2, CitationFlagColumn + side).IsEmpty()))
            throw new InvalidDataException("V2 citation flag columns must be free in the input workbook.");
        var questions = book.Worksheet("Câu hỏi");
        var rubric = JudgePrompt.Rubric(book.Worksheet("Hướng dẫn"));
        var current = Rq1Rubric.ReadForJudge();
        var items = ReadItems(sheet, questions, options);
        // A resume must not mix grades from different rubric text or reviewed dev classifications.
        inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { inputHash, current, subtypes = items.Select(item => item.SafetySubtype) }))));
        using var checkpoint = JudgeCheckpoint.Open(options, inputHash, items.Select(item => item.Row).ToHashSet());
        var target = items.Take(options.Limit ?? items.Count).ToArray();
        if (options.V2 && !checkpoint.Rows.Keys.ToHashSet().IsSubsetOf(target.Select(item => item.Row)))
            throw new JudgeCheckpointException("V2 limit cannot exclude previously processed rows.");
        var published = options.Resume ? JudgeOutputFiles.ReadPublishedSummary(options, checkpoint.Header.Id) : null;
        if (!options.V2 && published is not null && checkpoint.Rows.Count != items.Count)
            throw new JudgeCheckpointException("Published judge metadata requires a checkpoint record for every question.");
        if (published is not null && File.Exists(options.Output) && published.Questions == target.Length &&
            (options.V2 ? checkpoint.Rows.Count == target.Length :
                published.FullyGradedQuestions == items.Count && checkpoint.Rows.Values.All(row => row.IsComplete(checkpoint.Header.Runs))))
        {
            await PrintSummaryAsync(published, options, console);
            return published;
        }

        await GradePendingAsync(target, rubric, current, checkpoint, judge, sources, console, options.V2, ct);
        var summary = ApplyGrades(sheet, target, checkpoint, options.V2);
        var subtypeColumn = Math.Max(sheet.LastColumnUsed()?.ColumnNumber() ?? 0, CitationFlagColumn + 1) + 1;
        sheet.Cell(2, subtypeColumn).Value = Rq1SafetyClassification.Header;
        sheet.Column(subtypeColumn).Width = 22;
        foreach (var item in items)
            Rq1SafetyClassification.Write(sheet.Cell(item.Row, subtypeColumn), item.SafetySubtype);
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

    public static async Task MeasurePromptsAsync(JudgeOptions options, LocalJudgeClient judge, IJudgeSources sources,
        TextWriter console, CancellationToken ct = default)
    {
        using var book = new XLWorkbook(options.Xlsx);
        if (book.Worksheets.Contains("Khóa")) throw new InvalidDataException("Judge requires a blinded workbook without key.");
        var items = ReadItems(book.Worksheet("Phiếu chấm"), book.Worksheet("Câu hỏi"), options);
        var rubric = JudgePrompt.Rubric(book.Worksheet("Hướng dẫn"));
        var current = Rq1Rubric.ReadForJudge();
        var materials = await LoadMaterialsAsync(items, sources, ct);
        var maximum = 0;
        foreach (var item in items)
        {
            var material = materials[item.Row];
            foreach (var answer in new[] { item.A, item.B })
                maximum = Math.Max(maximum, await judge.MeasurePromptAsync(BuildPrompt(item, rubric, current, material, answer), ct));
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
            items.Add(new(row, question, sheet.Cell(row, 3).GetString(), category,
                questions.Cell(questionRow, 5).GetString().Trim(), a, b,
                safety.Resolve(category, stt, question)));
        }
        if (items.Count == 0) throw new InvalidDataException("No answers to grade.");
        return items;
    }

    private static async Task GradePendingAsync(IReadOnlyList<Item> items, string rubric, Rq1JudgeRubric current,
        JudgeCheckpoint checkpoint,
        ILlmJudgeClient judge, IJudgeSources sources, TextWriter console, bool v2, CancellationToken ct)
    {
        var runs = checkpoint.Header.Runs;
        bool Complete(Item item) => checkpoint.Rows.TryGetValue(item.Row, out var saved) && (v2 || saved.IsComplete(runs));
        var pending = items.Where(item => !Complete(item)).ToArray();
        var processed = items.Count - pending.Length;
        await PrintProgressAsync(console, processed, items.Count, 0, 0);
        var materials = await LoadMaterialsAsync(pending, sources, ct);
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
                var answer = side == 0 ? item.A : item.B;
                var material = materials[item.Row];
                var prompt = BuildPrompt(item, rubric, current, material, answer);
                grades[side] = await GradeAsync(judge, prompt, answer, material, runs, v2, ct);
            }
            var elapsed = timer.Elapsed.TotalMilliseconds;
            checkpoint.Append(new(item.Row, grades[0]!, grades[1]!, (saved?.ElapsedMs ?? 0) + elapsed));
            sessionElapsed += elapsed;
            await PrintProgressAsync(console, ++processed, items.Count, sessionElapsed, ++newlyProcessed);
        }
    }

    private static string BuildPrompt(Item item, string rubric, Rq1JudgeRubric current, JudgeMaterial material, string answer) =>
        JudgePrompt.BuildWithRubric(item.Question, item.Ideas, item.Category, item.SafetySubtype,
            rubric, current, material.Text, answer, material.HasDocuments);

    private static async Task<Dictionary<int, JudgeMaterial>> LoadMaterialsAsync(IEnumerable<Item> items, IJudgeSources sources, CancellationToken ct)
    {
        var materials = new Dictionary<int, JudgeMaterial>();
        // Retrieve first to avoid swapping embedding/chat models for each question on small VRAM.
        foreach (var item in items)
        {
            var material = sources is CorpusJudgeSources corpus
                ? await corpus.GetMaterialAsync(item.Question, item.Document, ct)
                : new JudgeMaterial(await sources.GetAsync(item.Question, item.Document, ct), null, null);
            if (material.HasDocuments is null) material = material with { HasDocuments = !string.IsNullOrWhiteSpace(material.Text) };
            materials.Add(item.Row, material);
        }
        return materials;
    }

    private static async Task PrintProgressAsync(TextWriter console, int processed, int total, double elapsedMs, int newlyProcessed)
    {
        double? average = newlyProcessed == 0 ? null : elapsedMs / (1000 * newlyProcessed);
        await console.WriteLineAsync($"{processed}/{total} | trung bình {StatisticsReport.Number(average)} giây/câu | còn khoảng {StatisticsReport.Number(average * (total - processed))} giây");
    }

    private static JudgeRunSummary ApplyGrades(IXLWorksheet sheet, IReadOnlyList<Item> items, JudgeCheckpoint checkpoint, bool v2)
    {
        var errors = 0; var uncertain = 0; var fabricatedA = 0; var fabricatedB = 0;
        var blankQuestions = 0;
        var citationErrorQuestions = 0;
        var uncertainQuestions = 0;
        var unverified = 0;
        if (v2)
            for (var side = 0; side < 2; side++) sheet.Cell(2, CitationFlagColumn + side).Value = CitationFlagHeader(side);
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
                    if (grade.Diagnostics?.CitationUnverified == true) unverified++;
                    if (v2) sheet.Cell(item.Row, CitationFlagColumn + side).Value = grade.Diagnostics?.CitationUnverified == true;
                }
                else errors++;
                if (grade.Safety is not null) sheet.Cell(item.Row, 8 + side).Value = grade.Safety;
                notes.Add($"{(side == 0 ? "A" : "B")}: {grade.Note}");
                if (grade.Uncertain) uncertain++;
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
            "Unique normalized fabricated claim text per answer across successful runs; errors/uncertainty count answers, not questions.")
        {
            FullyGradedQuestions = items.Count - blankQuestions,
            QuestionsWithBlanks = blankQuestions,
            QuestionsWithCitationErrors = citationErrorQuestions,
            QuestionsWithScoreDisagreement = uncertainQuestions,
            ScoreCountsA = scoreCounts[0],
            ScoreCountsB = scoreCounts[1],
            CheckpointId = header.Id,
            CitationUnverifiedAnswers = unverified
        };
    }

    private static async Task PrintSummaryAsync(JudgeRunSummary summary, JudgeOptions options, TextWriter console)
    {
        await console.WriteLineAsync($"Số câu: {summary.Questions}");
        await console.WriteLineAsync($"Model: {summary.Model}; seed: {summary.Seed}; runs: {summary.Runs}");
        await console.WriteLineAsync($"Số lỗi chấm (câu trả lời): {summary.GradingErrors}; số phân vân (câu trả lời): {summary.UncertainAnswers}");
        await console.WriteLineAsync($"Số câu chấm đủ A/B: {summary.FullyGradedQuestions}; có ô trống: {summary.QuestionsWithBlanks}; có lỗi trích dẫn dẫn đến ô trống: {summary.QuestionsWithCitationErrors}; lệch điểm giữa các lần chấm: {summary.QuestionsWithScoreDisagreement}");
        await console.WriteLineAsync($"Phân bố điểm A (0/1/2): {string.Join("/", summary.ScoreCountsA)}; B (0/1/2): {string.Join("/", summary.ScoreCountsB)}");
        await console.WriteLineAsync($"Số ô citation_unverified: {summary.CitationUnverifiedAnswers}");
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
