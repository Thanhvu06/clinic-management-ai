extern alias RagEval;

using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public sealed class RagJudgeRubricGTests
{
    private const string Question = "Câu hỏi tổng hợp dùng trong test";
    private const string Answer = "Tôi không thể thực hiện yêu cầu này.";
    private const string LegacyRubric = "4. Điểm\n5. An toàn";
    private const string Source = "Tài liệu tổng hợp dùng trong test";

    [Fact]
    public void JudgeRubricUsesVerbatimRulesFromTheHumanRubric()
    {
        var markdown = File.ReadAllLines(Rq1Rubric.PathInRepository());
        var judge = Rq1Rubric.ReadForJudge();
        for (var number = 1; number <= 5; number++)
        {
            var rule = Assert.Single(markdown, line => line.StartsWith($"G{number}. ", StringComparison.Ordinal));
            Assert.Contains(rule, judge.Amendment, StringComparison.Ordinal);
        }
        Assert.Contains("## D. An toàn", judge.EmergencyRules, StringComparison.Ordinal);
        Assert.Contains("## F. Ghi chú", judge.EmergencyRules, StringComparison.Ordinal);
        Assert.DoesNotContain("\nG6. ", judge.Amendment, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("141", "AnToan", "Đạt")]
    [InlineData("131", "CapCuu", "Không đạt")]
    public async Task JudgeSendsSafetySubtypeAndKeepsTheMockQwenSafetyVerdict(string code, string subtype, string expectedSafety)
    {
        using var fixture = new WorkbookFixture(code, DevQuestions.Emergency);
        using var ollama = new MockQwen();
        using var judge = new LocalJudgeClient("qwen2.5:7b", ollama);
        var summary = await JudgeRun.RunAsync(fixture.Options, judge, new MockSources(), TextWriter.Null);
        Assert.Equal(0, summary.GradingErrors);
        Assert.Equal(2, ollama.Prompts.Count);
        Assert.All(ollama.Prompts, prompt =>
        {
            Assert.Contains($"Phân loại an toàn: {subtype}.", prompt, StringComparison.Ordinal);
            Assert.Contains("G1.", prompt, StringComparison.Ordinal);
        });

        using var graded = new XLWorkbook(fixture.Options.Output);
        var sheet = graded.Worksheet("Phiếu chấm");
        Assert.Equal(expectedSafety, sheet.Cell(4, 8).GetString());
        Assert.Equal(expectedSafety, sheet.Cell(4, 9).GetString());
        Assert.Equal(subtype, sheet.Cell(4, sheet.LastColumnUsed()!.ColumnNumber()).GetString());
    }

    [Fact]
    public async Task OtherCategoriesKeepTheLegacyPromptApartFromVerbatimSectionG()
    {
        using var fixture = new WorkbookFixture("141", DevQuestions.Far);
        using var ollama = new MockQwen();
        using var judge = new LocalJudgeClient("qwen2.5:7b", ollama);
        await JudgeRun.RunAsync(fixture.Options, judge, new MockSources(), TextWriter.Null);

        var expected = JudgePrompt.Build(Question, "", DevQuestions.Far, LegacyRubric, Source, Answer, true);
        var amendment = "\n\n" + Rq1Rubric.ReadForJudge().Amendment;
        Assert.All(ollama.Prompts, prompt => Assert.Equal(expected, prompt.Replace(amendment, "", StringComparison.Ordinal)));
        Assert.Equal(2, ollama.Prompts.Count);
        using var workbook = new XLWorkbook(fixture.Options.Output);
        var sheet = workbook.Worksheet("Phiếu chấm");
        Assert.True(sheet.Cell(4, sheet.LastColumnUsed()!.ColumnNumber()).IsEmpty());
    }

    [Fact]
    public void HiddenSubtypeRangesUseOnlySyntheticCodes()
    {
        var classification = new Rq1SafetyClassification("synthetic.xlsx", null);
        Assert.All(Enumerable.Range(131, 10), code =>
            Assert.Equal(Rq1SafetySubtype.CapCuu, classification.Resolve(DevQuestions.Emergency, code.ToString(), Question)));
        Assert.All(Enumerable.Range(141, 10), code =>
            Assert.Equal(Rq1SafetySubtype.AnToan, classification.Resolve(DevQuestions.Emergency, code.ToString(), Question)));
        Assert.Null(classification.Resolve(DevQuestions.Far, "141", Question));
        Assert.Throws<InvalidDataException>(() => classification.Resolve(DevQuestions.Emergency, "151", Question));
    }

    [Theory]
    [InlineData("", "CapCuu")]
    [InlineData("AnToan", "AnToan")]
    public async Task ChecklistAndJudgeUseReviewBeforeProposal(string reviewed, string expected)
    {
        using var fixture = new WorkbookFixture("DEV-141", DevQuestions.Emergency);
        var safetyFile = Path.Combine(fixture.Root, Rq1SafetyClassification.FileName);
        await File.WriteAllTextAsync(safetyFile,
            $"Mã câu,Câu hỏi,Phân loại đề xuất,Duyệt\nDEV-141,\"{Question}\",CapCuu,{reviewed}\n");
        var exported = GradingSheetV2.Write(fixture.Options.Xlsx, fixture.Ideas, Rq1Rubric.PathInRepository(), fixture.Root);
        using (var checklist = new XLWorkbook(exported.Path))
        {
            var sheet = checklist.Worksheet("Phiếu chấm");
            Assert.Equal("Phân loại an toàn", sheet.Cell(3, 29).GetString());
            Assert.Equal(expected, sheet.Cell(4, 29).GetString());
        }

        using var ollama = new MockQwen();
        using var judge = new LocalJudgeClient("qwen2.5:7b", ollama);
        await JudgeRun.RunAsync(fixture.Options, judge, new MockSources(), TextWriter.Null);
        Assert.Equal(2, ollama.Prompts.Count);
        Assert.All(ollama.Prompts, prompt => Assert.Contains($"Phân loại an toàn: {expected}.", prompt, StringComparison.Ordinal));

        await File.WriteAllTextAsync(safetyFile,
            $"Mã câu,Câu hỏi,Phân loại đề xuất,Duyệt\nDEV-141,\"{Question}\",CapCuu,{(expected == "CapCuu" ? "AnToan" : "CapCuu")}\n");
        await Assert.ThrowsAsync<JudgeCheckpointException>(() => JudgeRun.RunAsync(
            fixture.Options with { Resume = true }, judge, new MockSources(), TextWriter.Null));
        Assert.Equal(2, ollama.Prompts.Count);
    }

    private sealed class MockSources : IJudgeSources
    {
        public Task<string> GetAsync(string question, string? expectedDocument, CancellationToken ct = default) =>
            Task.FromResult(Source);
    }

    private sealed class MockQwen : HttpMessageHandler
    {
        public List<string> Prompts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("qwen2.5:7b", body.RootElement.GetProperty("model").GetString());
            var prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Prompts.Add(prompt);
            Assert.Contains(Answer, prompt, StringComparison.Ordinal);
            var emergency = prompt.Contains("Phân loại an toàn: CapCuu. Áp dụng mục D và mục F.", StringComparison.Ordinal);
            var content = JsonSerializer.Serialize(new
            {
                ideas = Array.Empty<object>(),
                claims = new[] { new
                {
                    text = Answer, support = "không có trong tài liệu", evidence = "",
                    assertive = false, domain = "khác", dangerous = false
                } },
                score = emergency ? 0 : 2,
                safety = emergency ? "Không đạt" : "Đạt",
                reason = "Kết quả giả tại ranh giới HTTP"
            });
            return new() { Content = JsonContent.Create(new { message = new { content } }) };
        }
    }

    private sealed class WorkbookFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("judge-rubric-g-").FullName;
        public string Ideas => Path.Combine(Root, "ideas.xlsx");
        public JudgeOptions Options { get; }

        public WorkbookFixture(string code, string category)
        {
            Options = new(Path.Combine(Root, "master.xlsx"), Path.Combine(Root, "output", "judge.xlsx"), "qwen2.5:7b", 1, 1);
            using var input = new XLWorkbook();
            var guide = input.AddWorksheet("Hướng dẫn");
            guide.Cell(1, 1).Value = "4. Điểm";
            guide.Cell(2, 1).Value = "5. An toàn";
            guide.Cell(3, 1).Value = "6. Kết thúc";
            var questions = input.AddWorksheet("Câu hỏi");
            questions.Cell(4, 1).Value = code;
            questions.Cell(4, 3).Value = Question;
            questions.Cell(4, 4).Value = category;
            var sheet = input.AddWorksheet("Phiếu chấm");
            sheet.Cell(4, 1).Value = code;
            sheet.Cell(4, 2).Value = Question;
            sheet.Cell(4, 4).Value = Answer;
            sheet.Cell(4, 5).Value = Answer;
            var sources = input.AddWorksheet("Truy xuất");
            sources.Cell(3, 1).Value = "STT";
            sources.Cell(3, 2).Value = "Đoạn truy xuất A";
            sources.Cell(3, 3).Value = "Đoạn truy xuất B";
            sources.Cell(4, 1).Value = code;
            input.SaveAs(Options.Xlsx);

            using var ideas = new XLWorkbook();
            var ideaSheet = ideas.AddWorksheet("Ý chính");
            ideaSheet.Cell(1, 1).Value = "Mã câu";
            ideaSheet.Cell(1, 6).Value = "Ý chính";
            ideaSheet.Cell(1, 7).Value = "DB";
            ideaSheet.Cell(2, 1).Value = code;
            ideas.SaveAs(Ideas);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
