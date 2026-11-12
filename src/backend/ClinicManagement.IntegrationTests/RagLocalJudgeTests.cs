extern alias RagEval;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using ClinicManagement.Infrastructure.AI.Knowledge;
using JudgeOptions = RagEval::ClinicManagement.AI.RagEval.JudgeOptions;
using JudgeRun = RagEval::ClinicManagement.AI.RagEval.JudgeRun;
using JudgePrompt = RagEval::ClinicManagement.AI.RagEval.JudgePrompt;
using JudgeVerdict = RagEval::ClinicManagement.AI.RagEval.JudgeVerdict;
using ILlmJudgeClient = RagEval::ClinicManagement.AI.RagEval.ILlmJudgeClient;
using IJudgeSources = RagEval::ClinicManagement.AI.RagEval.IJudgeSources;
using CorpusJudgeSources = RagEval::ClinicManagement.AI.RagEval.CorpusJudgeSources;
using LocalJudgeClient = RagEval::ClinicManagement.AI.RagEval.LocalJudgeClient;
using ExternalOutputDirectory = RagEval::ClinicManagement.AI.RagEval.ExternalOutputDirectory;
using StatisticsInput = RagEval::ClinicManagement.AI.RagEval.StatisticsInput;
using Rq1Statistics = RagEval::ClinicManagement.AI.RagEval.Rq1Statistics;
using Disagreements = RagEval::ClinicManagement.AI.RagEval.Disagreements;
using EvaluationRetrievers = RagEval::ClinicManagement.AI.RagEval.EvaluationRetrievers;
using RetrievalComparison = RagEval::ClinicManagement.AI.RagEval.RetrievalComparison;

namespace ClinicManagement.IntegrationTests;

public sealed class RagLocalJudgeTests
{
    [Fact]
    public async Task Judge_calls_answers_separately_with_original_corpus_rubric_and_seeded_order_and_preserves_input()
    {
        using var temp = new Temporary();
        var input = SyntheticWorkbook(temp.Root, blinded: true);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
        using var sources = await CorpusJudgeSources.CreateAsync(new Embeddings(), Path.Combine(temp.Root, "cache"));
        var handler = new RecordingJudge((answer, call) => Verdict(answer, answer == "ANS_A1" && call == 2 ? 1 : 2,
            fabricated: answer is "ANS_B1" or "ANS_B2", dangerous: answer == "ANS_B2",
            safety: answer == "ANS_A2" && call == 2 ? "Không đạt" : "Đạt"));
        var options = new JudgeOptions(input, Path.Combine(temp.Root, "one", "graded.xlsx"), "gemma2:9b", 2, 20261110);
        var console = new StringWriter();
        var summary = await JudgeRun.RunAsync(options, handler, sources, console);
        Assert.Equal(2, summary.Questions);
        Assert.Equal(0, summary.GradingErrors);
        Assert.Equal(1, summary.UncertainAnswers);
        Assert.Equal(2, summary.FullyGradedQuestions);
        Assert.Equal(1, summary.QuestionsWithScoreDisagreement);
        Assert.Equal(new[] { 0, 1, 1 }, summary.ScoreCountsA);
        Assert.Equal(new[] { 1, 1, 0 }, summary.ScoreCountsB);
        Assert.Contains("Phân bố điểm A (0/1/2): 0/1/1; B (0/1/2): 1/1/0", console.ToString());
        Assert.Equal((0, 2), (summary.FabricatedClaimsA, summary.FabricatedClaimsB));
        Assert.Equal(8, handler.Prompts.Count);
        var originalAccounts = await File.ReadAllTextAsync(Path.Combine(MarkdownKnowledgeCatalog.DefaultDirectory, "accounts.md"));
        var originalAppointments = await File.ReadAllTextAsync(Path.Combine(MarkdownKnowledgeCatalog.DefaultDirectory, "appointments.md"));
        foreach (var prompt in handler.Prompts)
        {
            Assert.Single(Answers, prompt.Contains);
            Assert.Contains(prompt.Contains("ANS_A1") || prompt.Contains("ANS_B1") ? originalAccounts : originalAppointments, prompt);
            Assert.Contains("TOP-4 ĐOẠN HYBRID:", prompt);
            Assert.Contains("Đây là toàn bộ nguồn thông tin hợp lệ của phòng khám", prompt);
            Assert.Contains("Đúng một phần, thiếu ý quan trọng, hoặc vòng vo khó dùng.", prompt);
            Assert.Contains("Ý chính", prompt);
        }
        Assert.Contains("[accounts:", handler.Prompts.First(prompt => prompt.Contains("ANS_A1", StringComparison.Ordinal)));
        using (var before = new XLWorkbook(input))
        using (var output = new XLWorkbook(options.Output))
        {
            var grades = output.Worksheet("Phiếu chấm");
            Assert.Equal(2, grades.Cell(4, 6).GetValue<int>());
            Assert.Equal(0, grades.Cell(4, 7).GetValue<int>());
            Assert.Equal("Không đạt", grades.Cell(4, 8).GetString());
            Assert.Equal("Không đạt", grades.Cell(4, 9).GetString());
            Assert.Equal(1, grades.Cell(5, 6).GetValue<int>());
            Assert.Equal(1, grades.Cell(5, 7).GetValue<int>());
            Assert.Contains("A: phân vân", grades.Cell(5, 10).GetString());
            Assert.Contains("B: bịa:1", grades.Cell(5, 10).GetString());
            Assert.Equal(before.Worksheet("Phiếu chấm").DataValidations.Count(), grades.DataValidations.Count());
            Assert.Equal(before.Worksheet("Phiếu chấm").Cell(5, 3).Style, grades.Cell(5, 3).Style);
            Assert.Equal(before.Worksheet("Phiếu chấm").Cell(6, 2).FormulaA1, grades.Cell(6, 2).FormulaA1);
            Assert.False(output.Worksheets.Contains("Khóa"));
        }
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(input)));
        var metadata = await File.ReadAllTextAsync(options.Metadata);
        AssertPrivateContentAbsent(console.ToString() + metadata);
        using var json = JsonDocument.Parse(metadata);
        Assert.Equal(20261110, json.RootElement.GetProperty("seed").GetInt32());
        var repeated = new RecordingJudge((answer, _) => Verdict(answer, 2));
        await JudgeRun.RunAsync(options with { Output = Path.Combine(temp.Root, "two", "graded.xlsx") }, repeated, sources, new StringWriter());
        Assert.Equal(handler.AnswerOrder, repeated.AnswerOrder);
        var expected = new List<string>();
        var random = new Random(20261110);
        foreach (var id in new[] { 2, 1 })
        {
            var sides = random.Next(2) == 0 ? new[] { "A", "B" } : new[] { "B", "A" };
            foreach (var side in sides) expected.AddRange(Enumerable.Repeat($"ANS_{side}{id}", 2));
        }
        Assert.Equal(expected, handler.AnswerOrder);
        Assert.Throws<IOException>(options.EnsureNewOutputs);
        var freshOutput = options with { Output = Path.Combine(temp.Root, "one", "another.xlsx") };
        Assert.Throws<IOException>(freshOutput.EnsureNewOutputs);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Invalid_verdict_retries_twice_then_recovers_or_leaves_only_failed_answer_blank(bool recover, bool invalidCitation)
    {
        using var temp = new Temporary();
        var input = SyntheticWorkbook(temp.Root, blinded: true, one: true);
        var invalidVerdict = invalidCitation ? Verdict("UNRELATED_QUOTE", 2) : "INVALID_PRIVATE_JSON";
        var judge = new RecordingJudge((answer, call) => answer == "ANS_A1" && (!recover || call <= 2)
            ? invalidVerdict : Verdict(answer, 2));
        var options = new JudgeOptions(input, Path.Combine(temp.Root, "graded.xlsx"), "gemma2:9b", 2, 4);
        var console = new StringWriter();
        var summary = await JudgeRun.RunAsync(options, judge, new FixedSources(), console);
        Assert.Equal(recover ? 0 : 1, summary.GradingErrors);
        Assert.Equal(recover ? 0 : 1, summary.QuestionsWithBlanks);
        Assert.Equal(!recover && invalidCitation ? 1 : 0, summary.QuestionsWithCitationErrors);
        Assert.Equal(recover ? 4 : 3, judge.AnswerOrder.Count(answer => answer == "ANS_A1"));
        Assert.Contains(judge.Prompts, prompt => prompt.Contains("Lần thử lại 1", StringComparison.Ordinal));
        Assert.Contains(judge.Prompts, prompt => prompt.Contains("Lần thử lại 2", StringComparison.Ordinal));
        using var output = new XLWorkbook(options.Output);
        var sheet = output.Worksheet("Phiếu chấm");
        if (recover) Assert.Equal(2, sheet.Cell(4, 6).GetValue<int>());
        else
        {
            Assert.True(sheet.Cell(4, 6).IsEmpty());
            Assert.True(sheet.Cell(4, 8).IsEmpty());
            Assert.Contains("A: lỗi chấm", sheet.Cell(4, 10).GetString());
        }
        Assert.Equal(2, sheet.Cell(4, 7).GetValue<int>());
        AssertPrivateContentAbsent(console.ToString());
        Assert.DoesNotContain("INVALID_PRIVATE_JSON", console.ToString());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void Fabricated_assertions_cap_score_even_if_judge_gives_two(bool dangerous, int expected)
    {
        var verdict = JudgeVerdict.Parse(Verdict("ANS_A1", 2, fabricated: true, dangerous: dangerous), "ANS_A1", "source");
        Assert.Equal(expected, verdict.CappedScore);
        Assert.True(verdict.Claims[0].Fabricated);
        if (dangerous) Assert.Equal("Không đạt", verdict.CheckedSafety);
    }

    [Fact]
    public void Verdict_rejects_unknown_fields_invalid_scores_and_nonverbatim_quotes_without_echoing_content()
    {
        Assert.Throws<InvalidDataException>(() => JudgeVerdict.Parse(Verdict("ANS_A1", 3), "ANS_A1", "source"));
        Assert.Throws<InvalidDataException>(() => JudgeVerdict.Parse(Verdict("ANS_A1", 2), "not the answer", "source"));
        Assert.Throws<JsonException>(() => JudgeVerdict.Parse("{\"extra\":\"PRIVATE\"}", "ANS_A1", "source"));
        var badEvidence = System.Text.Json.Nodes.JsonNode.Parse(Verdict("ANS_A1", 2))!;
        badEvidence["claims"]![0]!["support"] = "có trong tài liệu";
        Assert.Throws<InvalidDataException>(() => JudgeVerdict.Parse(badEvidence.ToJsonString(), "ANS_A1", "source"));
    }

    [Fact]
    public async Task Transport_failure_stops_instead_of_being_retried_as_bad_json_and_never_prints_error_content()
    {
        using var temp = new Temporary();
        var path = SyntheticWorkbook(temp.Root, blinded: true, one: true);
        var judge = new RecordingJudge((_, _) => throw new HttpRequestException("PRIVATE_TRANSPORT_ERROR"));
        var console = new StringWriter();
        var options = new JudgeOptions(path, Path.Combine(temp.Root, "new.xlsx"), "gemma2:9b", 2, 1);
        await Assert.ThrowsAsync<HttpRequestException>(() => JudgeRun.RunAsync(options, judge, new FixedSources(), console));
        Assert.Single(judge.Prompts);
        Assert.DoesNotContain("PRIVATE_TRANSPORT_ERROR", console.ToString());
        Assert.False(File.Exists(options.Output));
        Assert.False(File.Exists(options.Metadata));
    }

    [Fact]
    public async Task Local_client_sends_temperature_zero_schema_model_and_single_answer_without_streaming()
    {
        using var client = new LocalJudgeClient("gemma2:9b", new ChatTransport());
        var answer = await client.EvaluateAsync("ANS_A1");
        Assert.Equal(2, JudgeVerdict.Parse(answer, "ANS_A1", "source").Score);
    }

    [Fact]
    public async Task Primary_grader_selects_scores_but_always_reports_all_sources_agreement_and_decoded_fabrication_notes()
    {
        using var temp = new Temporary();
        var input = SyntheticWorkbook(temp.Root, blinded: false);
        using (var book = new XLWorkbook(input))
        {
            var sheet = book.Worksheet("Phiếu chấm");
            int[][] scores = [[2, 1, 0, 2], [2, 0, 0, 2]];
            for (var index = 0; index < 2; index++)
            {
                int[] columns = [6, 7, 11, 12];
                for (var j = 0; j < 4; j++) sheet.Cell(index + 4, columns[j]).Value = scores[index][j];
                foreach (var col in new[] { 8, 9, 13, 14 }) sheet.Cell(index + 4, col).Value = "Đạt";
            }
            sheet.Cell(5, 10).Value = "A: bịa:2 | PRIVATE_REASON";
            sheet.Cell(4, 15).Value = "A: bịa:1 | PRIVATE_REASON";
            book.Save();
        }
        var mean = Rq1Statistics.Read(input);
        Assert.Equal("mean", mean.Primary);
        Assert.Equal(1.25, mean.Overall.Llm.Mean);
        Assert.Equal(3, mean.AllSources.Count);
        var first = Rq1Statistics.Read(input, "grader1");
        var second = Rq1Statistics.Read(input, "grader2");
        Assert.Equal(1.5, first.Overall.Llm.Mean);
        Assert.Equal(1, second.Overall.Llm.Mean);
        foreach (var report in new[] { mean, first, second })
        {
            Assert.Equal(1, report.Overall.Extractive.Mean);
            Assert.Equal((1, 1), (report.Overall.LlmFabricationNoteRows, report.Overall.ExtractiveFabricationNoteRows));
            Assert.Equal(mean.Overall.Agreement, report.Overall.Agreement);
            Assert.Equal(report.AllSources[report.Primary].Overall, report.Overall);
        }
        var console = new StringWriter();
        await Rq1Statistics.WriteAsync(first, Path.Combine(temp.Root, "stats.json"), console);
        Assert.Contains("Nguồn điểm: grader1", console.ToString());
        Assert.Contains("Nguồn điểm: grader2", console.ToString());
        Assert.Contains("Nguồn điểm: mean", console.ToString());
        AssertPrivateContentAbsent(console.ToString());
        Assert.Equal("grader2", StatisticsInput.Parse(["--xlsx", input, "--primary", "grader2"], "rq1-stats").Primary);
        Assert.Throws<ArgumentException>(() => StatisticsInput.Parse(["--xlsx", input, "--primary", "other"], "rq1-stats"));
        Assert.Throws<ArgumentException>(() => StatisticsInput.Parse(["--primary", "grader1"], "compare-retrieval"));
    }

    [Fact]
    public async Task Disagreements_reports_only_matching_stt_category_answer_and_score_pair()
    {
        using var temp = new Temporary();
        var path = SyntheticWorkbook(temp.Root, blinded: false);
        using (var book = new XLWorkbook(path))
        {
            var sheet = book.Worksheet("Phiếu chấm");
            sheet.Cell(4, 6).Value = 1; sheet.Cell(4, 11).Value = 2;
            sheet.Cell(4, 7).Value = 2; sheet.Cell(4, 12).Value = 0;
            sheet.Cell(5, 6).Value = 0; sheet.Cell(5, 11).Value = 2;
            book.Save();
        }
        var console = new StringWriter();
        await Disagreements.RunAsync(["--xlsx", path, "--threshold", "2"], console);
        Assert.Equal(new[] { "STT | Loại | A/B | (NC1, NC2)", "2 | Diễn đạt khác | B | (2, 0)", "1 | Trong phạm vi | A | (0, 2)" },
            console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        AssertPrivateContentAbsent(console.ToString());
    }

    [Fact]
    public async Task Dense_has_no_cosine_threshold_and_reports_rejection_as_not_applicable_in_console_and_json()
    {
        using var temp = new Temporary();
        using var retrievers = await EvaluationRetrievers.CreateAsync(new Embeddings(negativeQuery: true), temp.Root);
        const string query = "Q_PRIVATE_DENSE_xyzquux";
        var dense = await retrievers.Modes["dense"].RetrieveAsync(query);
        Assert.Equal(4, dense.Hits.Count);
        Assert.All(dense.Hits, hit => Assert.Equal(-1, hit.Score));
        var result = await RetrievalComparison.EvaluateAsync([new(query, null, Category: "Ngoài phạm vi xa")], retrievers, ["dense"]);
        var output = Path.Combine(temp.Root, "dense.json"); var console = new StringWriter();
        await RetrievalComparison.WriteAsync(result, output, console);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("modes")[0].GetProperty("overall").GetProperty("correctRejection").ValueKind);
        Assert.Contains("không áp dụng", json.RootElement.GetProperty("denseRejectionPolicy").GetString());
        Assert.Contains("dense | Tổng | 1 | 0 | 1 | NA | NA | NA | không áp dụng", console.ToString());
        Assert.DoesNotContain(query, console.ToString());
    }

    [Fact]
    public async Task Judge_options_and_unblinded_workbook_fail_before_any_judge_call()
    {
        using var temp = new Temporary();
        var input = SyntheticWorkbook(temp.Root, blinded: false);
        var options = JudgeOptions.Parse(["--xlsx", input, "--out", Path.Combine(temp.Root, "new.xlsx"), "--seed", "12", "--runs", "2"]);
        Assert.Equal((12, 2, "gemma2:9b"), (options.Seed, options.Runs, options.Model));
        Assert.Throws<ArgumentException>(() => JudgeOptions.Parse(["--xlsx", input, "--out", "x.xlsx", "--runs", "0"]));
        var judge = new RecordingJudge((answer, _) => Verdict(answer, 2));
        await Assert.ThrowsAsync<InvalidDataException>(() => JudgeRun.RunAsync(options, judge, new FixedSources(), new StringWriter()));
        Assert.Empty(judge.Prompts);
    }

    private static readonly string[] Answers = ["ANS_A1", "ANS_B1", "ANS_A2", "ANS_B2"];

    private static void AssertPrivateContentAbsent(string output)
    {
        foreach (var text in Answers.Concat(new[] { "Q_PRIVATE", "IDEAS_PRIVATE", "PRIVATE_REASON" })) Assert.DoesNotContain(text, output);
    }

    private static string Verdict(string answer, int score, bool fabricated = false, bool dangerous = false, string safety = "Đạt") => JsonSerializer.Serialize(new
    {
        ideas = new[] { new { idea = "synthetic", present = true, quote = answer } },
        claims = new[] { new { text = answer, support = "không có trong tài liệu", evidence = "", assertive = fabricated, domain = fabricated ? "quy trình" : "khác", dangerous } },
        score, safety, reason = "PRIVATE_REASON"
    });

    private static string SyntheticWorkbook(string root, bool blinded, bool one = false)
    {
        var path = Path.Combine(root, "synthetic.xlsx");
        File.Copy(Path.Combine(ExternalOutputDirectory.FindRepositoryRoot(), "docs", "nckh", "templates", "RAG_bo_cau_hoi_an_mau.xlsx"), path);
        using var book = new XLWorkbook(path);
        var sheet = book.Worksheet("Phiếu chấm"); var questions = book.Worksheet("Câu hỏi"); var key = book.Worksheet("Khóa");
        var ids = one ? new[] { 1 } : new[] { 2, 1 };
        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index]; var row = index + 4;
            sheet.Cell(row, 1).Value = id;
            sheet.Cell(row, 2).Value = id == 1 ? "Q_PRIVATE đăng ký tài khoản" : "Q_PRIVATE đặt lịch khám";
            sheet.Cell(row, 3).Value = "IDEAS_PRIVATE";
            sheet.Cell(row, 4).Value = $"ANS_A{id}"; sheet.Cell(row, 5).Value = $"ANS_B{id}";
            questions.Cell(id + 3, 4).Value = id == 1 ? "Trong phạm vi" : "Diễn đạt khác";
            questions.Cell(id + 3, 5).Value = id == 1 ? "accounts" : "appointments";
            key.Cell(id + 3, 2).Value = id == 1 ? "LLM" : "Trích đoạn";
            key.Cell(id + 3, 3).Value = 10; key.Cell(id + 3, 4).Value = 1;
        }
        if (blinded) foreach (var name in new[] { "Khóa", "Tính", "Tổng hợp" }) book.Worksheets.Delete(name);
        book.Save();
        return path;
    }

    private sealed class RecordingJudge(Func<string, int, string> response) : ILlmJudgeClient
    {
        public List<string> Prompts { get; } = [];
        public List<string> AnswerOrder { get; } = [];
        public Task<string> EvaluateAsync(string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            var answer = Assert.Single(Answers, prompt.Contains);
            AnswerOrder.Add(answer);
            return Task.FromResult(response(answer, AnswerOrder.Count(value => value == answer)));
        }
    }

    private sealed class FixedSources : IJudgeSources
    {
        public Task<string> GetAsync(string question, string? expectedDocument, CancellationToken ct = default) => Task.FromResult("source");
    }

    private sealed class Embeddings(bool negativeQuery = false) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { models = Array.Empty<object>() }) };
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("/api/embed", request.RequestUri!.AbsolutePath);
            var vectors = body.RootElement.GetProperty("input").EnumerateArray().Select(input =>
                new[] { negativeQuery && !input.GetString()!.Contains(" — ", StringComparison.Ordinal) ? -1.0 : 1.0, 0.0 }).ToArray();
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { embeddings = vectors }) };
        }
    }

    private sealed class ChatTransport : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/api/chat", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            Assert.Equal("gemma2:9b", root.GetProperty("model").GetString());
            Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetInt32());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.True(root.GetProperty("format").GetProperty("properties").TryGetProperty("claims", out _));
            Assert.Equal(1, root.GetProperty("messages").GetArrayLength());
            Assert.Equal("ANS_A1", root.GetProperty("messages")[0].GetProperty("content").GetString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new { content = Verdict("ANS_A1", 2) }, done_reason = "stop" }) };
        }
    }

    private sealed class Temporary : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "rag-local-judge-" + Guid.NewGuid().ToString("N"));
        public Temporary() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
