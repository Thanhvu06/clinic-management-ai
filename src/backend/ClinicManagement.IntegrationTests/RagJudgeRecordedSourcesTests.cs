extern alias RagEval;

using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public sealed class RagJudgeRecordedSourcesTests
{
    private const string Code = "42";
    private const string Question = "Tôi cần đăng ký tài khoản như thế nào?";
    private const string Ideas = "Ý tổng hợp.";
    private const string AnswerA = "Đáp án thử nghiệm A.";
    private const string AnswerB = "Đáp án thử nghiệm B.";
    private static readonly ReplayPassage PassageA = new("dot-a", "Đoạn chỉ kèm A.", 1);
    private static readonly ReplayPassage PassageB = new("dot-b", "Đoạn chỉ kèm B.", 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recorded_judges_each_side_with_its_own_passages_without_initializing_corpus(bool v2)
    {
        using var fixture = new WorkbookFixture([PassageA], [PassageB]);
        var options = fixture.Options() with { V2 = v2 };
        Assert.Equal(JudgeSourceMode.Recorded, options.SourceMode);
        var judge = new RecordingJudge();

        var result = await JudgeRun.RunAsync(options, judge, TextWriter.Null, createCorpus: UnexpectedCorpus);

        Assert.Equal(1, result.FullyGradedQuestions);
        Assert.Equal(2, judge.Prompts.Count);
        Assert.Equal(ReplayPassage.Format([PassageA]), SourceBlock(judge.Prompts[AnswerA]));
        Assert.Equal(ReplayPassage.Format([PassageB]), SourceBlock(judge.Prompts[AnswerB]));
        Assert.Contains("Chỉ đối chiếu C3, C4", judge.Prompts[AnswerA]);
        Assert.All(judge.Prompts.Values, prompt => Assert.DoesNotContain("KEY_MODE_PRIVATE", prompt));
        using var output = new XLWorkbook(options.Output);
        Assert.False(output.Worksheets.Contains("Khóa"));
        Assert.False(output.Worksheets.Contains("Tính"));
        Assert.False(output.Worksheets.Contains("Tổng hợp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recorded_without_passages_sends_an_empty_source_block_with_a_notice(bool bothEmpty)
    {
        using var fixture = new WorkbookFixture([], bothEmpty ? [] : [PassageB]);
        var judge = new RecordingJudge();

        await JudgeRun.RunAsync(fixture.Options(), judge, TextWriter.Null, createCorpus: UnexpectedCorpus);

        Assert.Empty(SourceBlock(judge.Prompts[AnswerA]));
        Assert.Contains("KHÔNG CÓ ĐOẠN TRUY XUẤT KÈM CÂU TRẢ LỜI NÀY", judge.Prompts[AnswerA]);
        Assert.Equal(bothEmpty ? "" : ReplayPassage.Format([PassageB]), SourceBlock(judge.Prompts[AnswerB]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recorded_requires_a_full_workbook_with_recorded_passages(bool hasKey)
    {
        using var fixture = new WorkbookFixture(includeKey: hasKey);
        var options = fixture.Options();
        var judge = new RecordingJudge();

        var error = await Assert.ThrowsAsync<JudgeRecordedSourcesException>(() =>
            JudgeRun.RunAsync(options, judge, TextWriter.Null, createCorpus: UnexpectedCorpus));

        Assert.Contains("RecordedPassages are missing", error.Message);
        Assert.Contains("Khóa", error.Message);
        Assert.Empty(judge.Prompts);
        Assert.False(File.Exists(options.Checkpoint));
        Assert.False(File.Exists(options.Output));
    }

    [Fact]
    public async Task Explicit_corpus_preserves_the_legacy_prompt_for_both_answers()
    {
        using var fixture = new WorkbookFixture(includeKey: false);
        using var baseline = await CorpusJudgeSources.CreateAsync(new EmbeddingTransport(), fixture.Root);
        var sources = await baseline.GetAsync(Question, "accounts");
        Assert.StartsWith("TÀI LIỆU ĐÚNG (TOÀN VĂN):\n", sources);
        Assert.Contains("\nTOP-4 ĐOẠN HYBRID:\n[", sources);
        var options = fixture.Options("corpus");
        Assert.Equal(JudgeSourceMode.Corpus, options.SourceMode);
        var judge = new RecordingJudge();
        using var transport = new EmbeddingTransport();

        await JudgeRun.RunAsync(options, judge, TextWriter.Null,
            createCorpus: ct => CorpusJudgeSources.CreateAsync(transport, fixture.Root, ct));

        Assert.Equal(1, transport.Requests);
        using var input = new XLWorkbook(options.Xlsx);
        var rubric = JudgePrompt.Rubric(input.Worksheet("Hướng dẫn")) + "\n\n" + Rq1Rubric.ReadForJudge().Amendment;
        Assert.Equal(JudgePrompt.Build(Question, Ideas, "Trong phạm vi", rubric, sources, AnswerA, true), judge.Prompts[AnswerA]);
        Assert.Equal(JudgePrompt.Build(Question, Ideas, "Trong phạm vi", rubric, sources, AnswerB, true), judge.Prompts[AnswerB]);
    }

    [Theory]
    [InlineData("recorded", "corpus")]
    [InlineData("corpus", "recorded")]
    public async Task Resume_rejects_different_source_mode_before_loading_sources(string originalMode, string attemptedMode)
    {
        var recorded = originalMode == "recorded";
        using var fixture = recorded ? new WorkbookFixture([PassageA], [PassageB]) : new WorkbookFixture(includeKey: false);
        var options = fixture.Options(originalMode);
        using var transport = new EmbeddingTransport();
        await JudgeRun.RunAsync(options, new RecordingJudge(), TextWriter.Null,
            createCorpus: recorded ? UnexpectedCorpus : ct => CorpusJudgeSources.CreateAsync(transport, fixture.Root, ct));
        var checkpoint = await File.ReadAllBytesAsync(options.Checkpoint);
        using var header = JsonDocument.Parse(File.ReadLines(options.Checkpoint).First());
        Assert.Equal(originalMode, header.RootElement.GetProperty("sourceMode").GetString());
        var judge = new RecordingJudge();

        var error = await Assert.ThrowsAsync<JudgeCheckpointException>(() => JudgeRun.RunAsync(
            fixture.Options(attemptedMode) with { Resume = true }, judge, TextWriter.Null, createCorpus: UnexpectedCorpus));

        Assert.Contains("checkpoint source mode differs", error.Message);
        Assert.Empty(judge.Prompts);
        Assert.Equal(checkpoint, await File.ReadAllBytesAsync(options.Checkpoint));
        var resumed = await JudgeRun.RunAsync(options with { Resume = true }, judge, TextWriter.Null, createCorpus: UnexpectedCorpus);
        Assert.Equal(1, resumed.FullyGradedQuestions);
        Assert.Empty(judge.Prompts);
    }

    private static Task<CorpusJudgeSources> UnexpectedCorpus(CancellationToken ct) =>
        throw new InvalidOperationException("This execution must not initialize the corpus.");

    private static string SourceBlock(string prompt)
    {
        const string start = "<TAI_LIEU_GOC>\n";
        var offset = prompt.IndexOf(start, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        var end = prompt.IndexOf("\n</TAI_LIEU_GOC>", offset, StringComparison.Ordinal);
        return prompt[(offset + start.Length)..end];
    }

    private sealed class RecordingJudge : ILlmJudgeClient
    {
        public Dictionary<string, string> Prompts { get; } = new();

        public Task<string> EvaluateAsync(string prompt, CancellationToken ct = default)
        {
            var answer = Assert.Single(new[] { AnswerA, AnswerB }, prompt.Contains);
            Prompts.Add(answer, prompt);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                ideas = Array.Empty<object>(),
                claims = new[] { new
                {
                    text = answer, support = "không có trong tài liệu", evidence = "",
                    assertive = false, domain = "khác", dangerous = false
                } },
                score = 2,
                safety = "Đạt",
                reason = "Mẫu"
            }));
        }
    }

    private sealed class EmbeddingTransport : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return new() { Content = JsonContent.Create(new { models = Array.Empty<object>() }) };
            Requests++;
            Assert.Equal("/api/embed", request.RequestUri.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var vectors = body.RootElement.GetProperty("input").EnumerateArray().Select(_ => new[] { 1.0, 0.0 }).ToArray();
            return new() { Content = JsonContent.Create(new { embeddings = vectors }) };
        }
    }

    private sealed class WorkbookFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("rag-judge-recorded-").FullName;
        private string Input => Path.Combine(Root, "input.xlsx");

        public WorkbookFixture(ReplayPassage[]? a = null, ReplayPassage[]? b = null, bool includeKey = true)
        {
            using var book = new XLWorkbook();
            var guide = book.AddWorksheet("Hướng dẫn");
            guide.Cell(1, 1).Value = "4. Điểm";
            guide.Cell(2, 1).Value = "5. An toàn";
            guide.Cell(3, 1).Value = "6. Kết thúc";
            var roster = book.AddWorksheet("Câu hỏi");
            roster.Cell(4, 1).Value = Code;
            roster.Cell(4, 3).Value = Question;
            roster.Cell(4, 4).Value = "Trong phạm vi";
            roster.Cell(4, 5).Value = "accounts";
            var grades = book.AddWorksheet("Phiếu chấm");
            grades.Cell(4, 1).Value = Code;
            grades.Cell(4, 2).Value = Question;
            grades.Cell(4, 3).Value = Ideas;
            grades.Cell(4, 4).Value = AnswerA;
            grades.Cell(4, 5).Value = AnswerB;
            if (includeKey)
            {
                var key = book.AddWorksheet("Khóa");
                key.Cell(5, 1).Value = Code;
                key.Cell(5, 2).Value = "KEY_MODE_PRIVATE";
                book.AddWorksheet("Tính");
                book.AddWorksheet("Tổng hợp");
                if (a is not null && b is not null)
                {
                    RecordedPassages.WriteHeaders(key);
                    RecordedPassages.WriteRow(key, 5, new(Code, AnswerA, AnswerB, "KEY_MODE_PRIVATE", 0, 0)
                    {
                        PassagesA = a,
                        PassagesB = b
                    });
                }
            }
            book.SaveAs(Input);
        }

        public JudgeOptions Options(string? sourceMode = null)
        {
            string[] args = ["--xlsx", Input, "--out", Path.Combine(Root, "out", "graded.xlsx"), "--runs", "1", "--seed", "42"];
            return JudgeOptions.Parse(sourceMode is null ? args : [..args, "--judge-sources", sourceMode]);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
