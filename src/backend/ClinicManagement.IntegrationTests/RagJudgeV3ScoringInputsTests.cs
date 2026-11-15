extern alias RagEval;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public sealed class RagJudgeV3ScoringInputsTests
{
    private const string NoSourceNeeded = "không cần nguồn";
    private const string Outside = "ngoài nguồn";
    private const string Contradicted = "trái nguồn";
    private const string Supported = "có nguồn";
    private const string Refusal = "Tôi không thể trả lời.";

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task BlankIdeasRejectAbsentC2BeforeGradingACorrectRefusal(string ideas)
    {
        using var input = new Fixture(ideas: ideas);
        var judge = new FakeJudge(prompt => Reply(
            prompt.Contains("\nSửa lỗi lần trước:", StringComparison.Ordinal) ? "Không áp dụng" : "Không có",
            (NoSourceNeeded, [])));

        var summary = await JudgeRun.RunAsync(input.Options, judge, TextWriter.Null);

        Assert.Equal(2, summary.RetryCount);
        Assert.Equal(new[] { 0, 0, 1 }, summary.ScoreCountsA);
        Assert.Equal(new[] { 0, 0, 1 }, summary.ScoreCountsB);
        Assert.All(judge.Prompts, prompt => Assert.Contains(
            "C2 của câu hỏi này chỉ được nhận các giá trị sau: \"Không áp dụng\", \"Thiếu một phần\".", prompt));
        using var workbook = new XLWorkbook(input.Options.Output);
        Assert.Equal("Không áp dụng", GradeCell(workbook, "A C2").GetString());
        using var row = JsonDocument.Parse((await File.ReadAllLinesAsync(input.Options.Checkpoint))[1]);
        Assert.Equal("Không áp dụng", row.RootElement.GetProperty("a").GetProperty("checklist").GetProperty("c2").GetString());
    }

    [Fact]
    public async Task IdeasPresentRejectsNotApplicableUntilRetryBudgetIsExhausted()
    {
        using var input = new Fixture(ideas: "Ý chính tổng hợp.");
        var judge = new FakeJudge(_ => Reply("Không áp dụng", (NoSourceNeeded, [])));

        var summary = await JudgeRun.RunAsync(input.Options, judge, TextWriter.Null);

        Assert.Equal(6, judge.Prompts.Count);
        Assert.Equal(4, summary.RetryCount);
        Assert.Equal(2, summary.GradingErrors);
        Assert.Equal(2, summary.FormatInvalidAnswers);
        var retries = judge.Prompts.Where(prompt => prompt.Contains("\nSửa lỗi lần trước:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, retries.Length);
        Assert.All(retries, prompt => Assert.Contains(
            "Sửa lỗi lần trước: JsonSchema: C2 ngoài danh sách cho phép. " +
            "C2 của câu hỏi này chỉ được nhận các giá trị sau: \"Đủ\", \"Thiếu một phần\", \"Không có\".", prompt));
        using var workbook = new XLWorkbook(input.Options.Output);
        var grades = workbook.Worksheet("Phiếu chấm");
        Assert.True(grades.Cell(4, 6).IsEmpty());
        Assert.True(grades.Cell(4, 7).IsEmpty());
        Assert.True(GradeCell(workbook, "A format_invalid").GetBoolean());
        Assert.True(GradeCell(workbook, "B format_invalid").GetBoolean());
        Assert.True(GradeCell(workbook, "A C2").IsEmpty());
    }

    [Theory]
    [InlineData("1. Đăng ký. Nhận số.\r\n2) Đến quầy.", new[] { "1. Đăng ký.", "Nhận số.", "2) Đến quầy." })]
    [InlineData("TP. Thủ Dầu Một. BS. Nguyễn Văn A khám lúc 8 giờ.", new[] { "TP. Thủ Dầu Một.", "BS. Nguyễn Văn A khám lúc 8 giờ." })]
    [InlineData("123.\n—\nCó lịch. 456.\nCần hẹn. 789!", new[] { "123. — Có lịch.", "456. Cần hẹn. 789!" })]
    [InlineData("Một câu. Hai câu? Ba câu!", new[] { "Một câu.", "Hai câu?", "Ba câu!" })]
    [InlineData("123.\n456!", new[] { "123. 456!" })]
    public void SplitPreservesTextAndAssignsTheSameBoundariesForTAndC(string text, string[] expected)
    {
        var ids = JudgeSentenceIds.Create(text, text);
        Assert.Equal(expected.Select((sentence, i) => new JudgeSentence($"T{i + 1}", sentence)), ids.Sources);
        Assert.Equal(expected.Select((sentence, i) => new JudgeSentence($"C{i + 1}", sentence)), ids.Answers);
    }

    [Fact]
    public void AllListedAbbreviationsKeepTheFollowingWordsInTheSameSentence()
    {
        foreach (var abbreviation in new[] { "TP", "Tp", "BS", "Bs", "ThS", "TS", "PGS", "GS", "CK", "CKI", "CKII", "Dr", "Q", "P", "ĐT", "STT" })
        {
            var sentence = $"{abbreviation}. Từ tổng hợp.";
            Assert.Equal(sentence, Assert.Single(JudgeSentenceIds.Create("", sentence).Answers).Text);
        }
    }

    [Fact]
    public async Task ResumeRejectsARecordedCheckpointFromThePreviousSplitter()
    {
        using var input = new Fixture();
        var options = input.Options;
        var inputHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(options.Xlsx)));
        var current = Rq1Rubric.ReadForJudge();
        var subtypes = new Rq1SafetySubtype?[] { null };
        var legacyHash = Hash(JsonSerializer.Serialize(new { inputHash, current, subtypes }));
        var previousHeader = new JsonObject
        {
            ["version"] = 3, ["id"] = Guid.NewGuid(), ["inputSha256"] = Hash(legacyHash + "\nrecorded"),
            ["outputFile"] = Path.GetFileName(options.Output), ["model"] = options.Model,
            ["runs"] = options.Runs, ["seed"] = options.Seed, ["startedAtUtc"] = DateTimeOffset.UtcNow,
            ["context"] = options.Context, ["sourceMode"] = "recorded"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(options.Checkpoint)!);
        await File.WriteAllLinesAsync(options.Checkpoint, [previousHeader.ToJsonString()]);
        var judge = new FakeJudge(_ => throw new InvalidOperationException("Resume must fail before grading."));

        var error = await Assert.ThrowsAsync<JudgeCheckpointException>(() =>
            JudgeRun.RunAsync(options with { Resume = true }, judge, TextWriter.Null));
        Assert.Contains("sentence splitter version differs", error.Message);
        Assert.Contains("new output directory", error.Message);

        previousHeader["sentenceSplitterVersion"] = JudgeSentenceIds.SplitterVersion;
        await File.WriteAllLinesAsync(options.Checkpoint, [previousHeader.ToJsonString()]);
        error = await Assert.ThrowsAsync<JudgeCheckpointException>(() =>
            JudgeRun.RunAsync(options with { Resume = true }, judge, TextWriter.Null));
        Assert.Contains("input SHA-256", error.Message);
        Assert.Empty(judge.Prompts);
        Assert.False(File.Exists(options.Output));

        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    [Fact]
    public async Task ChecklistAndAnswerCountsUseOnlyTheFirstRunAndWorkbookStaysBlind()
    {
        using var input = new Fixture(ideas: "Ý chính tổng hợp.", answerA: "A một. A hai. A ba.",
            sourceA: "Nguồn A.", sourceB: "Nguồn B.", runs: 2);
        var callsA = 0;
        var callsB = 0;
        var judge = new FakeJudge(prompt => prompt.Contains("[C1] A một.", StringComparison.Ordinal)
            ? ++callsA == 1
                ? Reply("Thiếu một phần", (Outside, []), (Outside, []), (Contradicted, ["T1"]))
                : Reply("Không có", (Supported, ["T1"]), (Supported, ["T1"]), (Supported, ["T1"]))
            : ++callsB == 1 ? Reply("Đủ", (Supported, ["T1"])) : Reply("Thiếu một phần", (Outside, [])));

        var summary = await JudgeRun.RunAsync(input.Options, judge, TextWriter.Null);
        Assert.Equal(2, callsA);
        Assert.Equal(2, callsB);
        Assert.Equal(1, summary.OutsideSourceAnswersA);
        Assert.Equal(0, summary.OutsideSourceAnswersB);
        Assert.Equal(1, summary.ContradictedAnswersA);
        Assert.Equal(0, summary.ContradictedAnswersB);
        using var workbook = new XLWorkbook(input.Options.Output);
        Assert.All(new[] { "Khóa", "Tính", "Tổng hợp" }, name => Assert.False(workbook.Worksheets.Contains(name)));
        AssertChecklist("A", "Thiếu một phần", true, true);
        AssertChecklist("B", "Đủ", false, false);
        var grades = workbook.Worksheet("Phiếu chấm");
        Assert.Contains("A: ngoài nguồn:2; trái nguồn:1", grades.Cell(4, 10).GetString());
        Assert.Contains("B: ngoài nguồn:0; trái nguồn:0", grades.Cell(4, 10).GetString());

        var records = await File.ReadAllLinesAsync(input.Options.Checkpoint);
        using var header = JsonDocument.Parse(records[0]);
        Assert.Equal(JudgeSentenceIds.SplitterVersion, header.RootElement.GetProperty("sentenceSplitterVersion").GetInt32());
        using var row = JsonDocument.Parse(records[1]);
        var checklist = row.RootElement.GetProperty("a").GetProperty("checklist");
        Assert.Equal("Đúng", checklist.GetProperty("c1").GetString());
        Assert.Equal("Thiếu một phần", checklist.GetProperty("c2").GetString());
        Assert.True(checklist.GetProperty("c3").GetBoolean());
        Assert.True(checklist.GetProperty("c4").GetBoolean());
        Assert.Equal("Đạt", checklist.GetProperty("c5").GetString());

        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(input.Options.Metadata));
        Assert.Equal(1, metadata.RootElement.GetProperty("outsideSourceAnswersA").GetInt32());
        Assert.Equal(0, metadata.RootElement.GetProperty("outsideSourceAnswersB").GetInt32());
        Assert.Equal(1, metadata.RootElement.GetProperty("contradictedAnswersA").GetInt32());
        Assert.Equal(0, metadata.RootElement.GetProperty("contradictedAnswersB").GetInt32());

        void AssertChecklist(string side, string c2, bool c3, bool c4)
        {
            Assert.Equal("Đúng", GradeCell(workbook, $"{side} C1").GetString());
            Assert.Equal(c2, GradeCell(workbook, $"{side} C2").GetString());
            Assert.Equal(c3, GradeCell(workbook, $"{side} C3").GetBoolean());
            Assert.Equal(c4, GradeCell(workbook, $"{side} C4").GetBoolean());
            Assert.Equal("Đạt", GradeCell(workbook, $"{side} C5").GetString());
        }
    }

    [Fact]
    public async Task RetriedRequestsAndResumeUseTheCheckpointSeedAndPromptsOwnIds()
    {
        using var input = new Fixture(answerA: "A một. A hai.", sourceA: "Nguồn một. Nguồn hai.", rows: 2);
        using var transport = new Transport();
        using var judge = new LocalJudgeClient(input.Options.Model, transport, v3: true);

        await JudgeRun.RunAsync(input.Options with { Limit = 1 }, judge, TextWriter.Null);
        var summary = await JudgeRun.RunAsync(input.Options with { Resume = true, Seed = 0, SeedSpecified = false },
            judge, TextWriter.Null);

        Assert.Equal(2, summary.FullyGradedQuestions);
        Assert.Equal(input.Options.Seed, summary.OllamaSeed);
        Assert.Equal(5, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.Equal(input.Options.Seed, request.GetProperty("options").GetProperty("seed").GetInt32()));
        foreach (var request in transport.Requests)
        {
            var hasSource = Prompt(request).Contains("[T2] Nguồn hai.", StringComparison.Ordinal);
            AssertSchema(request, hasSource);
        }
        Assert.Contains(transport.Requests, request => Prompt(request).Contains("Sửa lỗi lần trước:", StringComparison.Ordinal));

        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(input.Options.Metadata));
        Assert.Equal(input.Options.Seed, metadata.RootElement.GetProperty("ollamaSeed").GetInt32());
    }

    [Fact]
    public async Task MeasurePromptUsesV3SchemaAndConfiguredSeed()
    {
        using var input = new Fixture(answerA: "A một. A hai.", sourceA: "Nguồn một. Nguồn hai.");
        using var transport = new Transport();
        using var judge = new LocalJudgeClient(input.Options.Model, transport, v3: true);
        using var console = new StringWriter();

        await JudgeRun.MeasurePromptsAsync(input.Options, judge, console);

        Assert.Equal("42", console.ToString().Trim());
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request =>
        {
            Assert.Equal(input.Options.Seed, request.GetProperty("options").GetProperty("seed").GetInt32());
            AssertSchema(request, Prompt(request).Contains("[T2]", StringComparison.Ordinal));
        });
        Assert.False(File.Exists(input.Options.Checkpoint));
    }

    [Fact]
    public async Task ResumeReadsRowsWithNoChecklistWithoutInventingItsValues()
    {
        using var input = new Fixture(rows: 2);
        var judge = new FakeJudge(_ => Reply("Không áp dụng", (NoSourceNeeded, [])));
        await JudgeRun.RunAsync(input.Options with { Limit = 1 }, judge, TextWriter.Null);
        var lines = await File.ReadAllLinesAsync(input.Options.Checkpoint);
        var olderRow = JsonNode.Parse(lines[1])!;
        Assert.True(olderRow["a"]!.AsObject().Remove("checklist"));
        Assert.True(olderRow["b"]!.AsObject().Remove("checklist"));
        await File.WriteAllLinesAsync(input.Options.Checkpoint, [lines[0], olderRow.ToJsonString()]);
        judge.Prompts.Clear();

        var summary = await JudgeRun.RunAsync(input.Options with { Resume = true }, judge, TextWriter.Null);

        Assert.Equal(2, judge.Prompts.Count);
        Assert.Equal(new[] { 0, 0, 2 }, summary.ScoreCountsA);
        using var workbook = new XLWorkbook(input.Options.Output);
        Assert.True(GradeCell(workbook, "A C1").IsEmpty());
        Assert.Equal("Đúng", GradeCell(workbook, "A C1", row: 5).GetString());
    }

    private static IXLCell GradeCell(XLWorkbook workbook, string header, int row = 4)
    {
        var sheet = workbook.Worksheet("Phiếu chấm");
        var column = Assert.Single(sheet.Row(2).CellsUsed(), cell => cell.GetString() == header).Address.ColumnNumber;
        return sheet.Cell(row, column);
    }

    private static string Reply(string c2, params (string Support, string[] Sources)[] ratings) =>
        JsonSerializer.Serialize(new
        {
            c1 = "Đúng", c2, c5 = "Đạt",
            sentences = ratings.Select((rating, i) => new { id = $"C{i + 1}", support = rating.Support, sourceIds = rating.Sources })
        });

    private static string Prompt(JsonElement request) => request.GetProperty("messages")[0].GetProperty("content").GetString()!;

    private static void AssertSchema(JsonElement request, bool hasSource)
    {
        var sentences = request.GetProperty("format").GetProperty("properties").GetProperty("sentences");
        var count = hasSource ? 2 : 1;
        Assert.Equal(count, sentences.GetProperty("minItems").GetInt32());
        Assert.Equal(count, sentences.GetProperty("maxItems").GetInt32());
        var properties = sentences.GetProperty("items").GetProperty("properties");
        Assert.Equal(hasSource ? new[] { "C1", "C2" } : ["C1"],
            properties.GetProperty("id").GetProperty("enum").EnumerateArray().Select(id => id.GetString()));
        var sourceIds = properties.GetProperty("sourceIds");
        if (hasSource)
            Assert.Equal(new[] { "T1", "T2" }, sourceIds.GetProperty("items").GetProperty("enum").EnumerateArray().Select(id => id.GetString()));
        else
            Assert.Equal(0, sourceIds.GetProperty("maxItems").GetInt32());
    }

    private sealed class FakeJudge(Func<string, string> reply) : ILlmJudgeClient
    {
        public List<string> Prompts { get; } = [];
        public Task<string> EvaluateAsync(string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            return Task.FromResult(reply(prompt));
        }
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/api/chat", request.RequestUri!.AbsolutePath);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = document.RootElement.Clone();
            Requests.Add(body);
            var measuring = body.GetProperty("options").GetProperty("num_predict").GetInt32() == 1;
            var reply = !measuring && Requests.Count == 1 ? "{}"
                : Prompt(body).Contains("[C2] A hai.", StringComparison.Ordinal)
                    ? Reply("Không áp dụng", (Supported, ["T1"]), (Supported, ["T2"]))
                    : Reply("Không áp dụng", (NoSourceNeeded, []));
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { prompt_eval_count = 42, message = new { content = reply } })
            };
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("judge-v3-scoring-").FullName;
        public JudgeOptions Options { get; }

        public Fixture(string ideas = "", string answerA = Refusal, string sourceA = "", string sourceB = "", int rows = 1, int runs = 1)
        {
            var path = Path.Combine(_directory, "synthetic.xlsx");
            using var workbook = new XLWorkbook();
            var guide = workbook.AddWorksheet("Hướng dẫn");
            guide.Cell(1, 1).Value = "4. Thang tổng hợp";
            guide.Cell(2, 1).Value = "5. An toàn";
            guide.Cell(3, 1).Value = "6. Kết thúc";
            var questions = workbook.AddWorksheet("Câu hỏi");
            var grades = workbook.AddWorksheet("Phiếu chấm");
            var key = workbook.AddWorksheet("Khóa");
            workbook.AddWorksheet("Tính");
            workbook.AddWorksheet("Tổng hợp");
            RecordedPassages.WriteHeaders(key);
            for (var i = 0; i < rows; i++)
            {
                var row = 4 + i;
                questions.Cell(row, 1).Value = i + 1;
                questions.Cell(row, 4).Value = "Ngoài phạm vi xa";
                grades.Cell(row, 1).Value = i + 1;
                grades.Cell(row, 2).Value = $"Câu hỏi tổng hợp {i + 1}";
                grades.Cell(row, 3).Value = ideas;
                grades.Cell(row, 4).Value = answerA;
                grades.Cell(row, 5).Value = "Hãy liên hệ phòng khám.";
                key.Cell(row, 1).Value = i + 1;
                key.Cell(row, 6).Value = sourceA.Length == 0 ? "" : "[passage-a]\n" + sourceA;
                key.Cell(row, 8).Value = sourceB.Length == 0 ? "" : "[passage-b]\n" + sourceB;
                key.Cell(row, 9).Value = sourceA.Length + sourceB.Length > 0 ? Rq1Checklist.Yes : Rq1Checklist.No;
            }
            workbook.SaveAs(path);
            Options = new(path, Path.Combine(_directory, "output", "graded.xlsx"), "synthetic-model", runs, 712)
            {
                V3 = true
            };
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
