extern alias RagEval;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using ClinicManagement.Application.AI.Knowledge;
using BlindAnswerOptions = RagEval::ClinicManagement.AI.RagEval.BlindAnswerOptions;
using BlindAnswerRun = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRun;
using BlindAnswerRuntime = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRuntime;
using BlindAnswerWorkbook = RagEval::ClinicManagement.AI.RagEval.BlindAnswerWorkbook;
using EvaluationQuestion = RagEval::ClinicManagement.AI.RagEval.EvaluationQuestion;
using ExternalOutputDirectory = RagEval::ClinicManagement.AI.RagEval.ExternalOutputDirectory;
using GradingSheetV2 = RagEval::ClinicManagement.AI.RagEval.GradingSheetV2;
using KeyIdeasTemplate = RagEval::ClinicManagement.AI.RagEval.KeyIdeasTemplate;
using ReplayComparison = RagEval::ClinicManagement.AI.RagEval.ReplayComparison;
using ReplayPassage = RagEval::ClinicManagement.AI.RagEval.ReplayPassage;
using ReplayVerification = RagEval::ClinicManagement.AI.RagEval.ReplayVerification;
using RetrievalReplay = RagEval::ClinicManagement.AI.RagEval.RetrievalReplay;
using RetrievalReplayResult = RagEval::ClinicManagement.AI.RagEval.RetrievalReplayResult;
using StatisticsReport = RagEval::ClinicManagement.AI.RagEval.StatisticsReport;

namespace ClinicManagement.IntegrationTests;

public sealed class RagRetrievalReplayTests
{
    private const string Refusal = "ClinicCare chỉ hỗ trợ thông tin sức khỏe, lịch khám và hoạt động của phòng khám trong phạm vi quyền của bạn.";
    private const string Passage = "Đến quầy tiếp nhận để kiểm tra giấy tờ trước khi vào phòng khám.";

    [Fact]
    public async Task Replay_preserves_ranking_text_and_scores_on_repeated_inputs()
    {
        EvaluationQuestion[] questions = [new("câu giả", null, "02"), new("rỗng", null, "01")];
        var retriever = new FakeRetriever();
        var first = await RetrievalReplay.CollectAsync(questions, retriever);
        var second = await RetrievalReplay.CollectAsync(questions, retriever);
        Assert.Equal(new[] { "02", "01" }, first.Keys);
        Assert.Equal(first["02"], second["02"]);
        Assert.Equal(new ReplayPassage("fake:0", Passage, 0.9), Assert.Single(first["02"]));
        Assert.Empty(first["01"]);
        Assert.Empty(second["01"]);
        Assert.Equal(4, retriever.Queries.Count);
    }

    [Fact]
    public async Task Replay_rejects_hybrid_fallback()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RetrievalReplay.CollectAsync(
            [new("câu giả", null, "1")], new FakeRetriever(false)));
    }

    [Fact]
    public void Verification_separates_passage_refusal_mismatch_and_unknown()
    {
        ReplayPassage[] passages = [new("a", "Bệnh   viện mở cửa.", 1), new("b", "Mang giấy tờ.", 0.5)];
        const string normalizedAnswer = "Xin lưu ý: Be\u0323\u0302nh viện mở cửa.\nMang giấy tờ.";
        Assert.Equal(ReplayComparison.PassageMatches, ReplayVerification.Compare(normalizedAnswer, passages));
        Assert.Equal(ReplayComparison.PassageMatches, ReplayVerification.Compare(Passage, [new("a", Passage, 1)]));
        Assert.Equal(ReplayComparison.MissingAdditionalPassages,
            ReplayVerification.Compare("Bệnh viện mở cửa.", passages));
        Assert.Equal(ReplayComparison.DifferentPassage, ReplayVerification.Compare("Nội dung khác.", passages));
        Assert.Equal(ReplayComparison.DefaultRefusalMatches, ReplayVerification.Compare(Refusal, []));
        Assert.Equal(ReplayComparison.UnexpectedRefusal, ReplayVerification.Compare(Refusal, passages));
        Assert.Equal(ReplayComparison.EmptyRetrievalWithOtherAnswer, ReplayVerification.Compare("Nội dung khác.", []));
        Assert.Equal(ReplayComparison.MissingAnswerOrKey, ReplayVerification.Compare(null, passages));
        Assert.Equal(ReplayComparison.TruncatedPassage,
            ReplayVerification.Compare("Một. Hai. Ba. Bốn. Năm.", [new("a", "Một. Hai. Ba. Bốn. Năm. Sáu.", 1)]));
    }

    [Fact]
    public void Verification_joins_the_extractive_side_by_question_code()
    {
        using var book = new XLWorkbook();
        var grades = book.AddWorksheet("Phiếu chấm");
        var keys = book.AddWorksheet("Khóa");
        Fill(grades, 4, 1, "1");
        Fill(grades, 4, 4, "Câu LLM", Passage);
        Fill(grades, 5, 1, "2");
        Fill(grades, 5, 4, "Nội dung khác", Passage);
        Fill(grades, 6, 1, "3");
        Fill(keys, 4, 1, "2", "Trích đoạn");
        Fill(keys, 5, 1, "1", "LLM");
        Fill(keys, 6, 1, "3", "LLM");
        var hits = new[] { new ReplayPassage("a", Passage, 1) };
        var counts = ReplayVerification.Verify(book, new Dictionary<string, ReplayPassage[]>
        {
            ["3"] = hits, ["2"] = hits, ["1"] = hits
        });
        Assert.Equal((1, 1, 1), (counts.Matched, counts.Mismatched, counts.Indeterminate));
        Assert.False(counts.IsComplete);
    }

    [Fact]
    public async Task Blind_answers_persist_full_passages_in_master_without_changing_blinded_copies()
    {
        var root = Directory.CreateTempSubdirectory("rag-recorded-test-");
        try
        {
            var input = Path.Combine(root.FullName, "questions.xlsx");
            WriteFixture(input, 1);
            var output = Path.Combine(root.FullName, "result");
            var options = BlindAnswerOptions.Parse(["--xlsx", input, "--out-dir", output, "--seed", "42", "--retrieval", "bm25"]);
            var retriever = new FakeRetriever();
            using var runtime = await BlindAnswerRuntime.CreateAsync(new FakeOllama(), retrieval: "bm25", retriever: retriever);
            await BlindAnswerRun.RunAsync(options, runtime, TextWriter.Null);
            Assert.Single(retriever.Queries);
            var masterPath = Path.Combine(output, BlindAnswerWorkbook.FullFile);
            using var master = new XLWorkbook(masterPath);
            var key = master.Worksheet("Khóa");
            Assert.Equal("Mã đoạn A", key.Cell(3, 5).GetString());
            Assert.Equal("Mã đoạn B", key.Cell(3, 7).GetString());
            Assert.Equal("fake:0", key.Cell(4, 5).GetString());
            Assert.Equal("fake:0", key.Cell(4, 7).GetString());
            Assert.EndsWith(Passage, key.Cell(4, 6).GetString(), StringComparison.Ordinal);
            Assert.EndsWith(Passage, key.Cell(4, 8).GetString(), StringComparison.Ordinal);
            Assert.Equal("Có", key.Cell(4, 9).GetString());
            using var blinded = new XLWorkbook(Path.Combine(output, BlindAnswerWorkbook.Grader2File));
            Assert.False(blinded.Worksheets.Contains("Khóa"));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task Grading_export_rechecks_all_150_rows_against_the_master()
    {
        var root = Directory.CreateTempSubdirectory("rag-replay-grading-test-");
        try
        {
            var master = Path.Combine(root.FullName, "master.xlsx");
            WriteFixture(master, 150);
            var ideas = Path.Combine(root.FullName, "ideas.xlsx");
            KeyIdeasTemplate.Write(master, ideas);
            var rubric = Path.Combine(root.FullName, "rubric.md");
            await File.WriteAllTextAsync(rubric, "# Rubric giả");
            var entries = Enumerable.Range(1, 150).ToDictionary(i => i.ToString(), _ => new[] { new ReplayPassage("fake:0", Passage, 0.9) });
            var replay = Path.Combine(root.FullName, "replay.json");
            var report = new RetrievalReplayResult(new string('a', 40), new string('b', 64), entries);
            entries["150"] = [];
            await StatisticsReport.SaveAsync(report, replay);
            Assert.Throws<InvalidDataException>(() => GradingSheetV2.Write(master, ideas, rubric, root.FullName, replayPath: replay));
            Assert.False(File.Exists(Path.Combine(root.FullName, GradingSheetV2.FileName)));
            entries["150"] = entries["1"];
            var goodReplay = Path.Combine(root.FullName, "matched-replay.json");
            await StatisticsReport.SaveAsync(report, goodReplay);
            var result = GradingSheetV2.Write(master, ideas, rubric, root.FullName, replayPath: goodReplay);
            Assert.Equal(150, result.Rows);
            Assert.Equal(0, result.MissingSources);
            using var grade = new XLWorkbook(result.Path);
            var sheet = grade.Worksheet("Phiếu chấm");
            Assert.Equal(sheet.Cell(4, 8).GetString(), sheet.Cell(4, 9).GetString());
            Assert.EndsWith(Passage, sheet.Cell(4, 8).GetString(), StringComparison.Ordinal);
        }
        finally { root.Delete(recursive: true); }
    }

    private static void WriteFixture(string path, int count)
    {
        using var workbook = new XLWorkbook();
        foreach (var name in new[] { "Hướng dẫn", "Câu hỏi", "Phiếu chấm", "Khóa", "Tính", "Tổng hợp" })
            workbook.AddWorksheet(name);
        for (var i = 1; i <= count; i++)
        {
            var code = i.ToString();
            Fill(workbook.Worksheet("Câu hỏi"), i + 3, 1, code, "Người giả", "Có cần nhịn ăn trước xét nghiệm không?", "Trong phạm vi", "prepare-tests");
            Fill(workbook.Worksheet("Phiếu chấm"), i + 3, 1, code, "", "", "LLM giả", Passage);
            Fill(workbook.Worksheet("Khóa"), i + 3, 1, code, "LLM");
        }
        workbook.SaveAs(path);
    }

    private static void Fill(IXLWorksheet sheet, int row, int col, params string[] values)
    {
        for (var i = 0; i < values.Length; i++) sheet.Cell(row, col + i).Value = values[i];
    }

    private sealed class FakeRetriever(bool hybrid = true) : IClinicKnowledgeRetriever
    {
        public List<string> Queries { get; } = [];
        public Task<KnowledgeRetrieval> RetrieveAsync(string question, CancellationToken ct = default)
        {
            Queries.Add(question);
            return Task.FromResult(question == "rỗng" ? new KnowledgeRetrieval([]) :
                new KnowledgeRetrieval([new(new("fake:0", new("fake", "Giả", "Giả"), Passage), 0.9)], hybrid, hybrid));
        }
    }

    private sealed class FakeOllama : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/chat", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { done = true, message = new { content = "Hãy liên hệ nơi lấy mẫu để hỏi quy định chuẩn bị." } })
            });
        }
    }
}
