extern alias RagEval;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public sealed class RagJudgeSentenceIdsTests
{
    private const string Refusal = "Tôi không thể trả lời câu hỏi này.";
    private const string OtherAnswer = "Hãy liên hệ phòng khám.";
    private const string NoSource = "không cần nguồn";

    [Fact]
    public async Task RefusalWithoutRecordedSourcesGetsTwoAndOutputIsBlind()
    {
        using var fixture = new WorkbookFixture();
        var judge = new FakeJudge(_ => Verdict(c2: "Không áp dụng"));
        var summary = await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null, createCorpus: UnexpectedCorpus);

        Assert.Equal(JudgeSourceMode.Recorded, fixture.Options.SourceMode);
        Assert.Equal(0, summary.FormatInvalidAnswers);
        Assert.Equal(0, summary.FabricatedClaimsA);
        Assert.Equal(0, summary.GradingErrors);
        Assert.All(judge.Prompts, prompt =>
        {
            Assert.Contains("Chỉ dùng nhãn \"ngoài nguồn\" hoặc \"không cần nguồn\".", prompt);
            Assert.Contains("khuyên gọi 115 hoặc đi cấp cứu", prompt);
            Assert.Contains(Rq1Rubric.ReadForJudge().Amendment, prompt);
        });
        Assert.Contains(judge.Prompts, prompt => prompt.Contains("[C1] " + Refusal, StringComparison.Ordinal));
        using var output = new XLWorkbook(fixture.Options.Output);
        Assert.All(new[] { "Khóa", "Tính", "Tổng hợp" }, name => Assert.False(output.Worksheets.Contains(name)));
        var grades = output.Worksheet("Phiếu chấm");
        Assert.Equal(2, grades.Cell(4, 6).GetValue<int>());
        Assert.Equal("Đạt", grades.Cell(4, 8).GetString());
        Assert.Equal("A format_invalid", grades.Cell(2, 16).GetString());
        Assert.False(grades.Cell(4, 16).GetBoolean());
    }

    [Fact]
    public async Task SentencePromptsUseOnlyTheRecordedSourcesForThatAnswer()
    {
        using var fixture = new WorkbookFixture(sourceA: "Phòng A mở cửa.", sourceB: "Phòng B đóng cửa.");
        var judge = new FakeJudge(_ => Verdict("có nguồn", sourceId: "T1"));
        await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null, createCorpus: UnexpectedCorpus);

        var a = Assert.Single(judge.Prompts, prompt => prompt.Contains(Refusal, StringComparison.Ordinal));
        var b = Assert.Single(judge.Prompts, prompt => prompt.Contains(OtherAnswer, StringComparison.Ordinal));
        Assert.Contains("[T1] Phòng A mở cửa.", a);
        Assert.DoesNotContain("Phòng B", a);
        Assert.Contains("[T1] Phòng B đóng cửa.", b);
        Assert.DoesNotContain("Phòng A", b);
    }

    [Theory]
    [InlineData("có nguồn")]
    [InlineData("trái nguồn")]
    public async Task EmptySourceRejectsLabelAndRetries(string support)
    {
        using var fixture = new WorkbookFixture();
        var judge = new FakeJudge(attempt => attempt == 0 ? Verdict(support) : Verdict());
        var summary = await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null);

        Assert.Equal(3, judge.Prompts.Count);
        Assert.Contains("NoDocuments", judge.Prompts[1]);
        Assert.Equal(1, summary.RetryCount);
        Assert.Equal(0, summary.FormatInvalidAnswers);
        Assert.Equal(0, summary.GradingErrors);
    }

    [Theory]
    [InlineData("Sai", "Đủ", "Đạt", NoSource, 0)]
    [InlineData("Đúng", "Đủ", "Đạt", "trái nguồn", 0)]
    [InlineData("Đúng", "Đủ", "Không đạt", NoSource, 0)]
    [InlineData("Đúng", "Không có", "Đạt", NoSource, 0)]
    [InlineData("Đúng", "Đủ", "Đạt", "có nguồn", 2)]
    [InlineData("Đúng", "Không áp dụng", "Đạt", NoSource, 2)]
    [InlineData("Đúng", "Đủ", "Đạt", "ngoài nguồn", 1)]
    [InlineData("Đúng", "Thiếu một phần", "Đạt", "có nguồn", 1)]
    public void ScoreFollowsC1ThroughC5(string c1, string c2, string c5, string support, int expected)
    {
        var response = Verdict(support, c1, c2, c5, sourceId: support == "có nguồn" ? "T1" : null);
        var ids = JudgeSentenceIds.Create("Phòng A mở cửa.", OtherAnswer);

        Assert.Empty(ids.Validate(response));
        Assert.Equal(expected, JudgeSentenceIds.Parse(response).CalculateScore());
    }

    [Theory]
    [InlineData("Sai", "Đạt")]
    [InlineData("Đúng", "Không đạt")]
    public async Task LaterDisagreementDoesNotChangePrimaryScoreOrSafety(string c1, string c5)
    {
        using var fixture = new WorkbookFixture(runs: 2);
        var judge = new FakeJudge(attempt => attempt == 1 ? Verdict(c1: c1, c5: c5) : Verdict());
        var summary = await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null);

        Assert.Equal(1, summary.QuestionsWithScoreDisagreement);
        Assert.Equal(1, summary.UncertainAnswers);
        Assert.Equal(2, summary.ComparedRuns);
        Assert.Equal(1, summary.AgreedRuns);
        using var output = new XLWorkbook(fixture.Options.Output);
        var grades = output.Worksheet("Phiếu chấm");
        Assert.All(new[] { 6, 7 }, col => Assert.Equal(2, grades.Cell(4, col).GetValue<int>()));
        Assert.All(new[] { 8, 9 }, col => Assert.Equal("Đạt", grades.Cell(4, col).GetString()));
        Assert.Contains("phân vân", grades.Cell(4, 10).GetString());
    }

    [Fact]
    public async Task AFailedLaterRunKeepsThePrimaryGradeAndFlagsFormat()
    {
        using var fixture = new WorkbookFixture(runs: 2);
        var judge = new FakeJudge(attempt => attempt is >= 1 and <= 3 ? Verdict("trái nguồn") : Verdict());

        var summary = await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null);
        Assert.Equal(0, summary.GradingErrors);
        Assert.Equal(1, summary.ComparedRuns);
        Assert.Equal(1, summary.AgreedRuns);
        Assert.Equal(1, summary.FormatInvalidAnswers);
        Assert.Equal(new[] { 0, 0, 1 }, summary.ScoreCountsA);
        Assert.Equal(new[] { 0, 0, 1 }, summary.ScoreCountsB);

        var resumed = await JudgeRun.RunAsync(fixture.Options with { Resume = true },
            new FakeJudge(_ => throw new InvalidOperationException("Checkpoint should supply both grades.")), TextWriter.Null);
        Assert.Equal(summary.CheckpointId, resumed.CheckpointId);
    }

    [Theory]
    [InlineData("Trong phạm vi")]
    [InlineData("Diễn đạt khác")]
    public async Task MissingKeyIdeasFailsBeforeJudgeAndDoesNotCreateFiles(string category)
    {
        using var fixture = new WorkbookFixture(category: category, ideas: " ");
        var judge = new FakeJudge(_ => Verdict());
        var error = await Assert.ThrowsAsync<JudgeMissingIdeasException>(() =>
            JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null, createCorpus: UnexpectedCorpus));

        Assert.Contains("Thiếu Ý chính", error.Message);
        Assert.Contains("dòng 4", error.Message);
        Assert.Empty(judge.Prompts);
        Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.Options.Output)));
    }

    [Theory]
    [InlineData("C9", "T1", "C9")]
    [InlineData("C1", "T9", "T9")]
    public async Task UnknownIdsAreRejectedEvenAfterRetriesAreExhausted(string answerId, string sourceId, string invalidId)
    {
        using var fixture = new WorkbookFixture(sourceA: "Nguồn A.", sourceB: "Nguồn B.");
        var badResponse = Verdict("có nguồn", answerId: answerId, sourceId: sourceId);
        var ids = JudgeSentenceIds.Create("Nguồn A.", Refusal);
        Assert.Contains(ids.Validate(badResponse), issue => issue.Code == "UnknownSentenceId" && issue.Id == invalidId);

        var judge = new FakeJudge(attempt => attempt < 3 ? badResponse : Verdict());
        var summary = await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null);
        Assert.Equal(4, judge.Prompts.Count);
        Assert.Contains("UnknownSentenceId", judge.Prompts[1]);
        Assert.Equal(1, summary.GradingErrors);
        Assert.Equal(1, summary.FormatInvalidAnswers);
        using var record = JsonDocument.Parse((await File.ReadAllLinesAsync(fixture.Options.Checkpoint))[1]);
        var grades = new[] { record.RootElement.GetProperty("a"), record.RootElement.GetProperty("b") };
        var blank = Assert.Single(grades, grade => grade.GetProperty("score").ValueKind == JsonValueKind.Null);
        var diagnostics = blank.GetProperty("diagnostics");
        Assert.True(diagnostics.GetProperty("format_invalid").GetBoolean());
        Assert.False(diagnostics.TryGetProperty("citationUnverified", out _));
    }

    [Theory]
    [InlineData("citationUnverified")]
    [InlineData("citation_unverified")]
    public async Task ResumeReadsTheOldCheckpointFlagAndWritesNewColumn(string legacyKey)
    {
        using var fixture = new WorkbookFixture(recorded: false);
        var options = fixture.Options with { Resume = true, SourceMode = JudgeSourceMode.Corpus };
        var workbookHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(options.Xlsx)));
        var header = new { version = 3, id = Guid.NewGuid(), inputSha256 = workbookHash,
            outputFile = Path.GetFileName(options.Output), model = options.Model, runs = 1, seed = options.Seed,
            startedAtUtc = DateTimeOffset.UtcNow, context = options.Context };
        var grade = new
        {
            score = 2, safety = "Đạt", note = "citation_unverified", fabrications = 0, uncertain = false,
            runsCompleted = 1, citationError = true,
            diagnostics = new Dictionary<string, object>
            {
                ["retryCount"] = 2, ["finalFailure"] = (int)JudgeFailureKind.UnknownSentenceId, [legacyKey] = true
            }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(options.Checkpoint)!);
        await File.WriteAllLinesAsync(options.Checkpoint, [JsonSerializer.Serialize(header), JsonSerializer.Serialize(new
        {
            row = 4, a = grade, b = grade, elapsedMs = 0
        })]);
        var judge = new FakeJudge(_ => throw new InvalidOperationException("Checkpoint should supply both grades."));

        await Assert.ThrowsAsync<JudgeCheckpointException>(() =>
            JudgeRun.RunAsync(options with { SourceMode = JudgeSourceMode.Recorded }, judge, TextWriter.Null));
        var summary = await JudgeRun.RunAsync(options, judge, TextWriter.Null, createCorpus: UnexpectedCorpus);
        Assert.Empty(judge.Prompts);
        Assert.Equal(header.id, summary.CheckpointId);
        Assert.Equal(2, summary.FormatInvalidAnswers);
        using var output = new XLWorkbook(options.Output);
        var grades = output.Worksheet("Phiếu chấm");
        Assert.Equal("A format_invalid", grades.Cell(2, 16).GetString());
        Assert.True(grades.Cell(4, 16).GetBoolean());
        Assert.DoesNotContain("citation_unverified", grades.Cell(4, 10).GetString());
    }

    [Fact]
    public async Task CorpusStillUsesTheOriginalPromptWithV3Selected()
    {
        using var fixture = new WorkbookFixture(recorded: false);
        var options = fixture.Options with { SourceMode = JudgeSourceMode.Corpus };
        const string source = "Nguồn cho kiểm thử corpus.";
        var judge = new FakeJudge(_ => JsonSerializer.Serialize(new
        {
            ideas = Array.Empty<object>(),
            claims = new[] { new { text = "Hướng dẫn tổng hợp", support = "có trong tài liệu", evidence = source,
                assertive = false, domain = "khác", dangerous = false } },
            score = 2, safety = "Đạt", reason = "Kiểm thử."
        }));

        var summary = await JudgeRun.RunAsync(options, judge, new SyntheticCorpus(source), TextWriter.Null);
        using var workbook = new XLWorkbook(options.Xlsx);
        var rubric = JudgePrompt.Rubric(workbook.Worksheet("Hướng dẫn")) + "\n\n" + Rq1Rubric.ReadForJudge().Amendment;
        Assert.False(options.UsesSentenceIds);
        Assert.Equal(0, summary.GradingErrors);
        Assert.Equal(2, judge.Prompts.Count);
        Assert.Contains(JudgePrompt.Build("Câu hỏi tổng hợp", "", "Ngoài phạm vi xa", rubric, source, Refusal, true), judge.Prompts);
        Assert.Contains(JudgePrompt.Build("Câu hỏi tổng hợp", "", "Ngoài phạm vi xa", rubric, source, OtherAnswer, true), judge.Prompts);
    }

    [Fact]
    public async Task LocalTransportRequestsOnlyTheV3Fields()
    {
        using var fixture = new WorkbookFixture();
        using var transport = new SchemaTransport();
        using var judge = new LocalJudgeClient(fixture.Options.Model, transport, v3: fixture.Options.UsesSentenceIds);

        var summary = await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null);
        Assert.Equal(0, summary.GradingErrors);
        Assert.Equal(2, transport.Calls);
        Assert.Throws<JsonException>(() => JudgeSentenceIds.Parse(Verdict().Insert(1, "\"score\":2,")));
    }

    private static string Verdict(string support = NoSource, string c1 = "Đúng", string c2 = "Đủ", string c5 = "Đạt",
        string answerId = "C1", string? sourceId = null) =>
        JsonSerializer.Serialize(new { c1, c2, c5, sentences = new[]
        {
            new { id = answerId, support, sourceIds = sourceId is null ? Array.Empty<string>() : new[] { sourceId } }
        } });

    private static Task<CorpusJudgeSources> UnexpectedCorpus(CancellationToken ct) =>
        throw new InvalidOperationException("No corpus should be opened.");

    private sealed class SyntheticCorpus(string source) : IJudgeSources
    {
        public Task<string> GetAsync(string question, string? expectedDocument, CancellationToken ct = default) =>
            Task.FromResult(source);
    }

    private sealed class FakeJudge(Func<int, string> evaluate) : ILlmJudgeClient
    {
        public List<string> Prompts { get; } = [];
        public Task<string> EvaluateAsync(string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            return Task.FromResult(evaluate(Prompts.Count - 1));
        }
    }

    private sealed class SchemaTransport : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var format = body.RootElement.GetProperty("format");
            Assert.Equal(new[] { "c1", "c2", "c5", "sentences" },
                format.GetProperty("required").EnumerateArray().Select(field => field.GetString()));
            Assert.False(format.GetProperty("properties").TryGetProperty("score", out _));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { message = new { content = Verdict() } })
            };
        }
    }

    private sealed class WorkbookFixture : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("judge-v3-").FullName;
        public JudgeOptions Options { get; }

        public WorkbookFixture(string category = "Ngoài phạm vi xa", string ideas = "", string sourceA = "", string sourceB = "",
            int runs = 1, bool recorded = true)
        {
            var input = Path.Combine(_directory, "synthetic.xlsx");
            using var book = new XLWorkbook();
            var guide = book.AddWorksheet("Hướng dẫn");
            guide.Cell(1, 1).Value = "4. Thang tổng hợp";
            guide.Cell(2, 1).Value = "5. An toàn";
            guide.Cell(3, 1).Value = "6. Kết thúc";
            var questions = book.AddWorksheet("Câu hỏi");
            questions.Cell(4, 1).Value = 1;
            questions.Cell(4, 4).Value = category;
            var grades = book.AddWorksheet("Phiếu chấm");
            grades.Cell(4, 1).Value = 1;
            grades.Cell(4, 2).Value = "Câu hỏi tổng hợp";
            grades.Cell(4, 3).Value = ideas;
            grades.Cell(4, 4).Value = Refusal;
            grades.Cell(4, 5).Value = OtherAnswer;
            if (recorded)
            {
                var key = book.AddWorksheet("Khóa");
                RecordedPassages.WriteHeaders(key);
                key.Cell(4, 1).Value = 1;
                key.Cell(4, 6).Value = sourceA.Length == 0 ? "" : "[passage-a]\n" + sourceA;
                key.Cell(4, 8).Value = sourceB.Length == 0 ? "" : "[passage-b]\n" + sourceB;
                key.Cell(4, 9).Value = sourceA.Length + sourceB.Length > 0 ? Rq1Checklist.Yes : Rq1Checklist.No;
                book.AddWorksheet("Tính");
                book.AddWorksheet("Tổng hợp");
            }
            book.SaveAs(input);
            Options = JudgeOptions.Parse(["--xlsx", input, "--out", Path.Combine(_directory, "out", "grades.xlsx"),
                "--judge-version", "3", "--runs", runs.ToString(System.Globalization.CultureInfo.InvariantCulture), "--seed", "42"]);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
