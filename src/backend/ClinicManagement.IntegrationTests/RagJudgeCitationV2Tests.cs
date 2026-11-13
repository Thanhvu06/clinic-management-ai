extern alias RagEval;

using System.Text.Json;
using ClosedXML.Excel;
using ILlmJudgeClient = RagEval::ClinicManagement.AI.RagEval.ILlmJudgeClient;
using IJudgeSources = RagEval::ClinicManagement.AI.RagEval.IJudgeSources;
using JudgeOptions = RagEval::ClinicManagement.AI.RagEval.JudgeOptions;
using JudgeRun = RagEval::ClinicManagement.AI.RagEval.JudgeRun;
using JudgeVerdict = RagEval::ClinicManagement.AI.RagEval.JudgeVerdict;
using MergeGrades = RagEval::ClinicManagement.AI.RagEval.MergeGrades;
using MergeGradesOptions = RagEval::ClinicManagement.AI.RagEval.MergeGradesOptions;
using Rq1Statistics = RagEval::ClinicManagement.AI.RagEval.Rq1Statistics;
using JudgeDiagnose = RagEval::ClinicManagement.AI.RagEval.JudgeDiagnose;

namespace ClinicManagement.IntegrationTests;

public sealed class RagJudgeCitationV2Tests
{
    [Theory]
    [InlineData("- **ĐĂNG KÝ** “TÀI KHOẢN” — TẠI QUẦY.", "\"đăng ký \"tài khoản\" - tại quầy!\"", true)]
    [InlineData("1. Bệnh nhân   đến quầy\n2. Sau đó lấy số thứ tự.", "bệnh nhân đến quầy … lấy số thứ tự", true)]
    [InlineData("Bệnh nhân đến quầy rồi lấy số thứ tự.", "lấy số thứ tự... bệnh nhân đến quầy", false)]
    [InlineData("Bệnh nhân đến quầy rồi lấy số thứ tự.", "bệnh nhân đến quầy … số", false)]
    [InlineData("Bệnh nhân đến quầy.", "Bệnh nhân thanh toán trực tuyến", false)]
    [InlineData("Đăng ký tại quầy.", "Đăng ký, tại quầy", false)]
    public void Citation_matches_only_normalized_ordered_substrings(string source, string evidence, bool matches)
    {
        var json = Verdict(evidence: evidence);
        if (matches) Assert.Equal(2, JudgeVerdict.Parse(json, "Từ chối", source).Score);
        else Assert.Throws<InvalidDataException>(() => JudgeVerdict.Parse(json, "Từ chối", source));
    }

    [Fact]
    public void Supported_claim_with_no_source_has_distinct_error()
    {
        var error = Assert.Throws<InvalidDataException>(() => JudgeVerdict.Parse(Verdict(evidence: "Đến quầy"), "Từ chối", ""));
        Assert.Equal(JudgeVerdict.NoSourceEvidenceMessage, error.Message);
    }

    [Fact]
    public async Task Pilot_retains_schema_valid_score_and_flag_but_leaves_json_failure_blank_then_resumes()
    {
        using var temp = new Fixture();
        var options = temp.Options with { Limit = 1 };
        var judge = new FakeJudge(prompt => prompt.Contains("PRIVATE_A1", StringComparison.Ordinal)
            ? Verdict(evidence: "PRIVATE_WRONG_EVIDENCE") : "PRIVATE_INVALID_JSON");
        var console = new StringWriter();
        var report = await JudgeRun.RunAsync(options, judge, new EmptySources(), console);
        Assert.Equal(1, report.Questions);
        Assert.Equal(1, report.GradingErrors);
        Assert.Equal(1, report.CitationUnverifiedAnswers);
        Assert.All(judge.Prompts, prompt => Assert.Contains("KHÔNG CÓ TÀI LIỆU NGUỒN", prompt));
        Assert.Contains(judge.Prompts, prompt => prompt.Contains("\"Field\":\"claims[0].evidence\"", StringComparison.Ordinal) &&
            prompt.Contains("PRIVATE_WRONG_EVIDENCE", StringComparison.Ordinal));
        Assert.DoesNotContain("PRIVATE_", console.ToString());
        using (var book = new XLWorkbook(options.Output))
        {
            var sheet = book.Worksheet("Phiếu chấm");
            Assert.Equal(2, sheet.Cell(4, 6).GetValue<int>());
            Assert.True(sheet.Cell(4, 16).GetBoolean());
            Assert.True(sheet.Cell(4, 7).IsEmpty());
            Assert.True(sheet.Cell(5, 16).IsEmpty());
        }
        Assert.Equal(2, File.ReadLines(options.Checkpoint).Count());
        var continuation = new FakeJudge(_ => Verdict());
        var full = await JudgeRun.RunAsync(options with { Limit = null, Resume = true }, continuation, new EmptySources(), new StringWriter());
        Assert.Equal(2, full.Questions);
        Assert.Equal(1, full.GradingErrors);
        Assert.Equal(1, full.CitationUnverifiedAnswers);
        Assert.Equal(4, continuation.Prompts.Count);
        Assert.All(continuation.Prompts, prompt =>
        {
            Assert.DoesNotContain("PRIVATE_A1", prompt);
            Assert.DoesNotContain("PRIVATE_B1", prompt);
        });

        var diagnosis = new StringWriter();
        await JudgeDiagnose.RunAsync(["--checkpoint", options.Checkpoint, "--master", temp.Master], diagnosis);
        Assert.Contains("LLM | 2 | 0 | 1", diagnosis.ToString());
        Assert.Contains("Không có tài liệu để trích | 0 | 1 | 1 | 1 | 4", diagnosis.ToString());
        Assert.DoesNotContain("PRIVATE_", diagnosis.ToString());
    }

    [Fact]
    public async Task Merge_keeps_human_primary_and_rq1_reports_both_ai_sensitivity_cohorts()
    {
        using var temp = new Fixture();
        var judge = new FakeJudge(prompt => prompt.Contains("PRIVATE_A1", StringComparison.Ordinal)
            ? Verdict(evidence: "PRIVATE_WRONG_EVIDENCE") : Verdict());
        await JudgeRun.RunAsync(temp.Options, judge, new EmptySources(), TextWriter.Null);
        var merged = Path.Combine(temp.Root, "merged.xlsx");
        var result = await MergeGrades.RunAsync(new MergeGradesOptions(temp.Master, temp.Master, merged, Ai: temp.Options.Output), TextWriter.Null);
        Assert.NotNull(result.AiSensitivity);
        Assert.Equal(8, result.AiSensitivity.AllScored.Pairs);
        Assert.Equal(6, result.AiSensitivity.VerifiedOnly.Pairs);
        Assert.Equal(4, result.AiSensitivity.AllScored.AiHigher);
        Assert.Equal(2, result.AiSensitivity.VerifiedOnly.AiHigher);
        var baseline = Rq1Statistics.Read(temp.Master);
        var combined = Rq1Statistics.Read(merged);
        Assert.Equal(baseline.Overall, combined.Overall);
        Assert.Equal(result.AiSensitivity, combined.AiSensitivity);
    }

    private static string Verdict(string? evidence = null) => JsonSerializer.Serialize(new
    {
        ideas = Array.Empty<object>(),
        claims = new[] { new { text = "Mệnh đề giả", support = evidence is null ? "không có trong tài liệu" : "có trong tài liệu",
            evidence = evidence ?? "", assertive = false, domain = "khác", dangerous = false } },
        score = 2, safety = "Đạt", reason = "PRIVATE_REASON"
    });

    private sealed class FakeJudge(Func<string, string> reply) : ILlmJudgeClient
    {
        public List<string> Prompts { get; } = [];
        public Task<string> EvaluateAsync(string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            return Task.FromResult(reply(prompt));
        }
    }

    private sealed class EmptySources : IJudgeSources
    {
        public Task<string> GetAsync(string question, string? expectedDocument, CancellationToken ct = default) => Task.FromResult("");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("judge-citation-v2-").FullName;
        public string Master => Path.Combine(Root, "master.xlsx");
        public JudgeOptions Options { get; }

        public Fixture()
        {
            using var book = new XLWorkbook();
            var questions = book.AddWorksheet("Câu hỏi");
            var grades = book.AddWorksheet("Phiếu chấm");
            var guide = book.AddWorksheet("Hướng dẫn");
            var key = book.AddWorksheet("Khóa");
            guide.Cell(1, 1).Value = "4. Rubric giả";
            guide.Cell(2, 1).Value = "5. An toàn";
            guide.Cell(3, 1).Value = "6. Kết thúc";
            for (var i = 1; i <= 2; i++)
            {
                var row = i + 3;
                questions.Cell(row, 1).Value = i;
                questions.Cell(row, 4).Value = "Ngoài phạm vi xa";
                grades.Cell(row, 1).Value = i;
                grades.Cell(row, 2).Value = "PRIVATE_QUESTION";
                grades.Cell(row, 4).Value = $"PRIVATE_A{i}";
                grades.Cell(row, 5).Value = $"PRIVATE_B{i}";
                foreach (var col in new[] { 6, 7, 11, 12 }) grades.Cell(row, col).Value = col is 6 or 11 ? 1 : 2;
                foreach (var col in new[] { 8, 9, 13, 14 }) grades.Cell(row, col).Value = "Đạt";
                key.Cell(row, 1).Value = i;
                key.Cell(row, 2).Value = "LLM";
                key.Cell(row, 3).Value = 1;
                key.Cell(row, 4).Value = 1;
            }
            book.SaveAs(Master);
            book.Worksheets.Delete("Khóa");
            var input = Path.Combine(Root, "input.xlsx");
            book.SaveAs(input);
            Options = new(input, Path.Combine(Root, "grades-v2.xlsx"), "gemma2:9b", 2, 1) { V2 = true };
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
