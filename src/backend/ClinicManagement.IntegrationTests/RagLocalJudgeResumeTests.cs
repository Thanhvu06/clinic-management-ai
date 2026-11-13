extern alias RagEval;

using System.Text.Json;
using ClosedXML.Excel;
using ExternalOutputDirectory = RagEval::ClinicManagement.AI.RagEval.ExternalOutputDirectory;
using IJudgeSources = RagEval::ClinicManagement.AI.RagEval.IJudgeSources;
using ILlmJudgeClient = RagEval::ClinicManagement.AI.RagEval.ILlmJudgeClient;
using JudgeOptions = RagEval::ClinicManagement.AI.RagEval.JudgeOptions;
using JudgeRun = RagEval::ClinicManagement.AI.RagEval.JudgeRun;

namespace ClinicManagement.IntegrationTests;

public sealed class RagLocalJudgeResumeTests
{
    [Fact]
    public async Task Judge_resume_after_cancellation_uses_original_seed_and_matches_uninterrupted_grades()
    {
        using var temp = new Temporary();
        using var cancellation = new CancellationTokenSource();
        var input = SyntheticWorkbook(temp.Root, blinded: true);
        var options = new JudgeOptions(input, Path.Combine(temp.Root, "resumed", "graded.xlsx"), "gemma2:9b", 2, 42);
        string Grade(string answer, int call) => Verdict(answer, answer.StartsWith("ANS_A") && call == 2 ? 1 : 2,
            fabricated: answer == "ANS_B1", dangerous: answer == "ANS_B1");
        var calls = 0;
        var interrupted = new RecordingJudge((answer, call) =>
        {
            if (++calls == 4) cancellation.Cancel();
            return Grade(answer, call);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            JudgeRun.RunAsync(options, interrupted, new FixedSources(), new StringWriter(), cancellation.Token));
        Assert.Equal(2, File.ReadLines(options.Checkpoint).Count());
        Assert.False(File.Exists(options.Output));
        Assert.False(File.Exists(options.Metadata));

        var resume = JudgeOptions.Parse(["--resume", "--xlsx", input, "--out", options.Output, "--runs", "2"]);
        var resumedJudge = new RecordingJudge(Grade);
        var progress = new StringWriter();
        var resumed = await JudgeRun.RunAsync(resume, resumedJudge, new FixedSources(), progress);
        var continuous = new RecordingJudge(Grade);
        var referenceOptions = options with { Output = Path.Combine(temp.Root, "reference", "graded.xlsx") };
        var reference = await JudgeRun.RunAsync(referenceOptions, continuous, new FixedSources(), new StringWriter());

        Assert.Equal(42, resumed.Seed);
        Assert.Equal(4, resumedJudge.AnswerOrder.Count);
        Assert.Equal(continuous.AnswerOrder, interrupted.AnswerOrder.Concat(resumedJudge.AnswerOrder));
        Assert.Contains("1/2 | trung bình", progress.ToString());
        Assert.Contains("2/2 | trung bình", progress.ToString());
        AssertPrivateContentAbsent(progress.ToString());
        Assert.Equal(reference.ScoreCountsA, resumed.ScoreCountsA);
        Assert.Equal(reference.ScoreCountsB, resumed.ScoreCountsB);
        Assert.Equal(reference.FabricatedClaimsB, resumed.FabricatedClaimsB);
        Assert.Equal(reference.QuestionsWithScoreDisagreement, resumed.QuestionsWithScoreDisagreement);
        using (var actual = new XLWorkbook(resume.Output))
        using (var expected = new XLWorkbook(referenceOptions.Output))
        {
            for (var row = 4; row <= 5; row++)
                for (var column = 6; column <= 10; column++)
                    Assert.Equal(expected.Worksheet("Phiếu chấm").Cell(row, column).Value,
                        actual.Worksheet("Phiếu chấm").Cell(row, column).Value);
        }

        // Simulate a stop after publishing the summary and before publishing the workbook.
        File.Delete(resume.Output);
        var unusedJudge = new RecordingJudge((_, _) => throw new InvalidOperationException());
        var regenerated = await JudgeRun.RunAsync(resume, unusedJudge, new FixedSources(), new StringWriter());
        Assert.Empty(unusedJudge.AnswerOrder);
        Assert.True(File.Exists(resume.Output));
        Assert.Equal(resumed.CheckpointId, regenerated.CheckpointId);
        Assert.Equal(resumed.ElapsedMs, regenerated.ElapsedMs);
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("model")]
    [InlineData("runs")]
    [InlineData("input")]
    public async Task Judge_resume_rejects_checkpoint_mismatch_before_calling_judge(string parameter)
    {
        using var temp = new Temporary();
        var input = SyntheticWorkbook(temp.Root, blinded: true);
        var options = new JudgeOptions(input, Path.Combine(temp.Root, "graded.xlsx"), "gemma2:9b", 2, 42);
        var interrupted = new RecordingJudge((_, _) => throw new HttpRequestException());
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            JudgeRun.RunAsync(options, interrupted, new FixedSources(), new StringWriter()));
        if (parameter == "input")
        {
            using var book = new XLWorkbook(input);
            book.Worksheet("Phiếu chấm").Cell(4, 3).Value = "changed synthetic rubric";
            book.Save();
        }

        var resume = options with { Resume = true };
        resume = parameter switch
        {
            "seed" => resume with { Seed = 43 },
            "model" => resume with { Model = "other-model" },
            "runs" => resume with { Runs = 3 },
            _ => resume
        };
        var checkpointBefore = await File.ReadAllBytesAsync(options.Checkpoint);
        var unusedJudge = new RecordingJudge((_, _) => throw new InvalidOperationException());
        var error = await Assert.ThrowsAnyAsync<IOException>(() =>
            JudgeRun.RunAsync(resume, unusedJudge, new FixedSources(), new StringWriter()));
        Assert.Contains("checkpoint parameters differ", error.Message);
        Assert.Empty(unusedJudge.Prompts);
        Assert.Equal(checkpointBefore, await File.ReadAllBytesAsync(options.Checkpoint));
    }

    [Fact]
    public async Task Judge_resume_discards_torn_tail_and_preserves_the_completed_side_of_an_incomplete_row()
    {
        using var temp = new Temporary();
        var input = SyntheticWorkbook(temp.Root, blinded: true, one: true);
        var options = new JudgeOptions(input, Path.Combine(temp.Root, "graded.xlsx"), "gemma2:9b", 2, 7);
        var initial = new RecordingJudge((answer, _) => answer == "ANS_B1" ? "invalid JSON" : Verdict(answer, 2));
        var partial = await JudgeRun.RunAsync(options, initial, new FixedSources(), new StringWriter());
        Assert.Equal(1, partial.QuestionsWithBlanks);
        await File.AppendAllTextAsync(options.Checkpoint, "{\"row\":4,\"a\":");

        var resumedJudge = new RecordingJudge((answer, _) => Verdict(answer, 1));
        var resumed = await JudgeRun.RunAsync(options with { Resume = true }, resumedJudge, new FixedSources(), new StringWriter());
        Assert.Equal(new[] { "ANS_B1", "ANS_B1" }, resumedJudge.AnswerOrder);
        Assert.Equal(1, resumed.FullyGradedQuestions);
        Assert.Equal(new[] { 0, 0, 1 }, resumed.ScoreCountsA);
        Assert.Equal(new[] { 0, 1, 0 }, resumed.ScoreCountsB);
        var records = await File.ReadAllLinesAsync(options.Checkpoint);
        Assert.Equal(3, records.Length);
        foreach (var record in records)
        {
            using var json = JsonDocument.Parse(record);
            Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
        }
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

    private sealed class Temporary : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "rag-local-judge-" + Guid.NewGuid().ToString("N"));
        public Temporary() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
