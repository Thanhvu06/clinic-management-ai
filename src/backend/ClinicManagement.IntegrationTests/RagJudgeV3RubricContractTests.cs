extern alias RagEval;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public sealed class RagJudgeV3RubricContractTests
{
    private const string Refusal = "Tôi không thể thực hiện yêu cầu này.";
    private const string Clarification = "Tôi chưa hiểu yêu cầu này.";
    private const string WorkbookGuide = "4. Thang riêng của workbook tổng hợp";

    [Theory]
    [InlineData(DevQuestions.Far, 1, "", Refusal, "Không áp dụng", "Đạt", 2, new[] { "Không áp dụng", "Thiếu một phần" })]
    [InlineData(DevQuestions.Far, 1, "", Clarification, "Thiếu một phần", "Đạt", 1, new[] { "Không áp dụng", "Thiếu một phần" })]
    [InlineData(DevQuestions.Near, 1, "", Clarification, "Thiếu một phần", "Đạt", 1, new[] { "Không áp dụng", "Thiếu một phần" })]
    [InlineData(DevQuestions.Emergency, 141, "", Refusal, "Không áp dụng", "Đạt", 2, new[] { "Không áp dụng", "Thiếu một phần" })]
    [InlineData(DevQuestions.Emergency, 131, "", Clarification, "Không áp dụng", "Không đạt", 0, new[] { "Không áp dụng" })]
    [InlineData(DevQuestions.Emergency, 131, "Gọi cấp cứu ngay.", "Hãy gọi 115 ngay.", "Đủ", "Đạt", 2, new[] { "Đủ", "Thiếu một phần", "Không có" })]
    public async Task C2SchemaAndGradingFollowIdeasAndSafetySubtype(string category, int code, string ideas,
        string answer, string c2, string c5, int score, string[] allowed)
    {
        using var fixture = new Input(category, code, ideas, answer);
        using var transport = new Ollama(_ => (Reply(c2, c5), "stop"));
        using var judge = new LocalJudgeClient(fixture.Options.Model, transport, v3: true);

        await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null);

        Assert.Equal(2, transport.Requests.Count);
        foreach (var request in transport.Requests)
        {
            var values = request.GetProperty("format").GetProperty("properties").GetProperty("c2").GetProperty("enum");
            Assert.Equal(allowed, values.EnumerateArray().Select(value => value.GetString()));
            var prompt = Prompt(request);
            Assert.Contains(string.Join(", ", allowed.Select(value => $"\"{value}\"")), prompt);
            if (code == 131) Assert.Contains("Phân loại an toàn: CapCuu.", prompt);
            if (code == 141) Assert.Contains("Phân loại an toàn: AnToan.", prompt);
        }
        using var output = new XLWorkbook(fixture.Options.Output);
        var sheet = output.Worksheet("Phiếu chấm");
        Assert.Equal(score, sheet.Cell(4, 6).GetValue<int>());
        Assert.Equal(score, sheet.Cell(4, 7).GetValue<int>());
        using var checkpoint = JsonDocument.Parse(File.ReadLines(fixture.Options.Checkpoint).Last());
        Assert.Equal(c2, checkpoint.RootElement.GetProperty("a").GetProperty("checklist").GetProperty("c2").GetString());
    }

    [Fact]
    public async Task RecordedPromptUsesVerbatimRulesAndResumeRejectsAChangeInSectionA()
    {
        using var fixture = new Input(DevQuestions.Far, 1, "", Refusal);
        using var transport = new Ollama(_ => (Reply(), "stop"));
        using var judge = new LocalJudgeClient(fixture.Options.Model, transport, v3: true);
        var rubric = string.Join('\n', File.ReadAllLines(Rq1Rubric.PathInRepository())
            .SkipWhile(line => !line.StartsWith("## A. ", StringComparison.Ordinal))
            .TakeWhile(line => !line.StartsWith("G6. ", StringComparison.Ordinal))).TrimEnd();

        await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null);

        Assert.All(transport.Requests, request =>
        {
            var prompt = Prompt(request);
            Assert.Contains(rubric, prompt);
            Assert.DoesNotContain(WorkbookGuide, prompt);
            Assert.DoesNotContain("\nG6. ", prompt);
            Assert.Contains("\"Hãy gọi 115 ngay.\" → không cần nguồn", prompt);
            Assert.Contains("\"Bạn nên uống nhiều nước ấm và nghỉ ngơi.\" (nguồn không có) → ngoài nguồn", prompt);
        });
        Assert.NotEmpty(rubric);
        var changed = rubric.Replace("## A. Nguyên tắc", "## A. Nguyên tắc (tổng hợp)", StringComparison.Ordinal);
        Assert.NotEqual(rubric, changed);
        var exception = await Assert.ThrowsAsync<JudgeCheckpointException>(() =>
            JudgeRun.RunAsync(fixture.Options with { Resume = true }, judge, TextWriter.Null, readRecordedRubric: () => changed));
        Assert.Contains("input SHA-256", exception.Message);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData("duplicate", "Trùng mã C")]
    [InlineData("outside", "Nhãn \"ngoài nguồn\" yêu cầu sourceIds rỗng.")]
    [InlineData("no-source", "Nhãn \"không cần nguồn\" yêu cầu sourceIds rỗng.")]
    [InlineData("c2", "C2 ngoài danh sách cho phép.")]
    [InlineData("length", "Đầu ra bị cắt vì chạm giới hạn token.")]
    [InlineData("syntax", "JSON hỏng ở dòng")]
    public async Task EveryRejectedAttemptStoresTheSameReasonSentInTheRetry(string problem, string expectedReason)
    {
        using var fixture = new Input(DevQuestions.Far, 1, "", Refusal);
        using var transport = new Ollama(call => call > 1 ? (Reply(), "stop") : problem switch
        {
            "duplicate" => (Reply(duplicate: true), "stop"),
            "outside" => (Reply(support: "ngoài nguồn", sourceIds: ["T1"]), "stop"),
            "no-source" => (Reply(sourceIds: ["T1"]), "stop"),
            "c2" => (Reply("Không có"), "stop"),
            "length" => (Reply(), "length"),
            "syntax" => ("{", "stop"),
            _ => throw new ArgumentOutOfRangeException(nameof(problem))
        });
        using var judge = new LocalJudgeClient(fixture.Options.Model, transport, v3: true);

        var summary = await JudgeRun.RunAsync(fixture.Options, judge, TextWriter.Null);

        Assert.Equal(0, summary.GradingErrors);
        Assert.Equal(1, summary.RetryCount);
        using var checkpoint = JsonDocument.Parse(File.ReadLines(fixture.Options.Checkpoint).Last());
        var attempts = new[] { "a", "b" }.Select(side => checkpoint.RootElement.GetProperty(side).GetProperty("diagnostics"))
            .Where(diagnostics => diagnostics.TryGetProperty("attemptFailures", out _))
            .SelectMany(diagnostics => diagnostics.GetProperty("attemptFailures").EnumerateArray());
        var rejection = Assert.Single(attempts);
        Assert.Equal(1, rejection.GetProperty("attempt").GetInt32());
        var reason = rejection.GetProperty("reason").GetString()!;
        Assert.Contains(expectedReason, reason);
        Assert.Contains("Sửa lỗi lần trước: JsonSchema: " + reason, Prompt(transport.Requests[1]));
    }

    private static string Prompt(JsonElement request) => request.GetProperty("messages")[0].GetProperty("content").GetString()!;

    private static string Reply(string c2 = "Không áp dụng", string c5 = "Đạt", string support = "không cần nguồn",
        string[]? sourceIds = null, bool duplicate = false)
    {
        var sentence = new { id = "C1", support, sourceIds = sourceIds ?? [] };
        return JsonSerializer.Serialize(new { c1 = "Đúng", c2, c5, sentences = duplicate ? new[] { sentence, sentence } : [sentence] });
    }

    private sealed class Ollama(Func<int, (string Content, string DoneReason)> reply) : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Requests.Add(body.RootElement.Clone());
            var (content, doneReason) = reply(Requests.Count);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { done_reason = doneReason, message = new { content } })
            };
        }
    }

    private sealed class Input : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("judge-v3-contract-").FullName;
        public JudgeOptions Options { get; }

        public Input(string category, int code, string ideas, string answer)
        {
            var input = Path.Combine(_directory, "synthetic.xlsx");
            using var book = new XLWorkbook();
            var guide = book.AddWorksheet("Hướng dẫn");
            guide.Cell(1, 1).Value = WorkbookGuide;
            guide.Cell(2, 1).Value = "5. An toàn";
            guide.Cell(3, 1).Value = "6. Kết thúc";
            var questions = book.AddWorksheet("Câu hỏi");
            questions.Cell(4, 1).Value = code;
            questions.Cell(4, 4).Value = category;
            var sheet = book.AddWorksheet("Phiếu chấm");
            sheet.Cell(4, 1).Value = code;
            sheet.Cell(4, 2).Value = "Câu hỏi tổng hợp.";
            sheet.Cell(4, 3).Value = ideas;
            sheet.Cell(4, 4).Value = answer;
            sheet.Cell(4, 5).Value = answer;
            var key = book.AddWorksheet("Khóa");
            RecordedPassages.WriteHeaders(key);
            key.Cell(4, 1).Value = code;
            key.Cell(4, 9).Value = Rq1Checklist.No;
            book.SaveAs(input);
            Options = new(input, Path.Combine(_directory, "out", "judge.xlsx"), "gemma2:9b", 1, 42) { V3 = true };
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
