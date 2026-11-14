extern alias RagEval;

using ClosedXML.Excel;
using ClinicManagement.Application.AI.Knowledge;
using BlindAnswerRuntime = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRuntime;
using ExcerptReplayFile = RagEval::ClinicManagement.AI.RagEval.ExcerptReplayFile;
using ExcerptReplayRow = RagEval::ClinicManagement.AI.RagEval.ExcerptReplayRow;
using ExcerptReplayTransport = RagEval::ClinicManagement.AI.RagEval.ExcerptReplayTransport;
using ExcerptReplayTurn = RagEval::ClinicManagement.AI.RagEval.ExcerptReplayTurn;
using ExcerptReplayVerification = RagEval::ClinicManagement.AI.RagEval.ExcerptReplayVerification;
using ExcerptRoute = RagEval::ClinicManagement.AI.RagEval.ExcerptRoute;
using GradingSheetV2 = RagEval::ClinicManagement.AI.RagEval.GradingSheetV2;
using KeyIdeasTemplate = RagEval::ClinicManagement.AI.RagEval.KeyIdeasTemplate;

namespace ClinicManagement.IntegrationTests;

public sealed class RagExcerptReplayTests
{
    private static readonly KnowledgeChunk Top = new("doc:1", new("doc", "Mẫu", "Mục 1"),
        "Một. Hai. Ba. Bốn. Năm. Sáu.");
    private static readonly KnowledgeChunk SameHeading = new("doc:2", Top.Source, "Nội dung cùng mục.");
    private static readonly KnowledgeChunk Other = new("other:1", new("other", "Mẫu 2", "Mục 2"), "Nội dung mục hai.");
    private static readonly KnowledgeChunk BelowThreshold = new("low:1", new("low", "Mẫu 3", "Mục 3"), "Nội dung điểm thấp.");

    [Fact]
    public async Task Full_extract_mode_compares_the_five_sentence_answer_and_captures_actual_prompt_order()
    {
        var retriever = new SyntheticRetriever();
        var network = new NoNetwork();
        using var transport = new ExcerptReplayTransport([Top, SameHeading, Other, BelowThreshold], network);
        using var runtime = await BlindAnswerRuntime.CreateAsync(transport, retrieval: "bm25", retriever: retriever);
        var entry = await ExcerptReplayTurn.RunAsync(runtime, transport, "Có cần nhịn ăn trước xét nghiệm không?");
        Assert.Equal(ExcerptRoute.DocumentExcerpt, entry.Route);
        Assert.Null(entry.Policy);
        Assert.Equal("Một. Hai. Ba. Bốn. Năm.", entry.Answer);
        Assert.Null(ExcerptReplayVerification.Compare("1", "Mo\u0323\u0302t. Hai.\r\nBa. Bốn.  Năm.", entry.Answer));
        Assert.Equal(new[] { Top.Id, Other.Id }, entry.PromptPassages.Select(passage => passage.Id));
        Assert.Equal(new[] { Top.Text, Other.Text }, entry.PromptPassages.Select(passage => passage.Text));

        var mismatch = ExcerptReplayVerification.Compare("1", entry.Answer, Top.Text)!;
        Assert.Equal("1", mismatch.Stt);
        Assert.Equal(entry.Answer.Length + 1, mismatch.FirstDifferentCharacter);
        Assert.Equal(entry.Answer.Length, mismatch.StoredLength);
        Assert.Equal(Top.Text.Length, mismatch.ReplayedLength);
        Assert.Equal(1, retriever.Calls);
        Assert.Equal(0, network.Calls);
    }

    [Fact]
    public async Task Full_extract_mode_compares_policy_answer_and_clears_previous_prompt()
    {
        var retriever = new SyntheticRetriever();
        var network = new NoNetwork();
        using var transport = new ExcerptReplayTransport([Top, SameHeading, Other, BelowThreshold], network);
        using var runtime = await BlindAnswerRuntime.CreateAsync(transport, retrieval: "bm25", retriever: retriever);
        await ExcerptReplayTurn.RunAsync(runtime, transport, "Có cần nhịn ăn trước xét nghiệm không?");
        var entry = await ExcerptReplayTurn.RunAsync(runtime, transport, "Tôi đau ngực dữ dội và khó thở.");
        Assert.Equal(ExcerptRoute.PolicyResponse, entry.Route);
        Assert.Equal("SafetyEmergency", entry.Policy);
        Assert.Empty(entry.PromptPassages);
        const string expected = "Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi 115 ngay.";
        Assert.Null(ExcerptReplayVerification.Compare("2", expected, entry.Answer));
        Assert.NotNull(ExcerptReplayVerification.Compare("2", "Một câu soạn sẵn khác.", entry.Answer));
        Assert.Equal(1, retriever.Calls);
        Assert.Equal(0, network.Calls);
    }

    [Fact]
    public void Comparison_reports_unicode_positions_and_accepts_empty_on_both_sides()
    {
        Assert.Null(ExcerptReplayVerification.Compare("3", " ", ""));
        var mismatch = ExcerptReplayVerification.Compare("4", "😀 x", "😀 y")!;
        Assert.Equal(3, mismatch.StoredLength);
        Assert.Equal(3, mismatch.ReplayedLength);
        Assert.Equal(3, mismatch.FirstDifferentCharacter);
    }

    [Fact]
    public async Task Grading_sheet_requires_all_historical_answers_and_uses_only_prompt_passages()
    {
        var directory = Directory.CreateTempSubdirectory("rag-excerpt-test-");
        try
        {
            var master = Path.Combine(directory.FullName, "master.xlsx");
            var ideas = Path.Combine(directory.FullName, "ideas.xlsx");
            var rubric = Path.Combine(directory.FullName, "rubric.md");
            var replay = Path.Combine(directory.FullName, "excerpt-replay.json");
            var validReplay = Path.Combine(directory.FullName, "excerpt-replay-ok.json");
            const string answer = "Một. Hai. Ba. Bốn. Năm.";
            using (var book = new XLWorkbook())
            {
                foreach (var name in new[] { "Hướng dẫn", "Câu hỏi", "Phiếu chấm", "Khóa", "Tính", "Tổng hợp" })
                    book.AddWorksheet(name);
                for (var i = 1; i <= 150; i++)
                {
                    var code = i.ToString();
                    Fill(book.Worksheet("Câu hỏi"), i + 3, code, "Người giả", "Câu hỏi giả", "Trong phạm vi", "prepare-tests");
                    Fill(book.Worksheet("Phiếu chấm"), i + 3, code, "", "", i % 2 == 0 ? "LLM" : answer, i % 2 == 0 ? answer : "LLM");
                    Fill(book.Worksheet("Khóa"), 154 - i, code, i % 2 == 0 ? "LLM" : "Trích đoạn");
                }
                book.SaveAs(master);
            }
            KeyIdeasTemplate.Write(master, ideas);
            await File.WriteAllTextAsync(rubric, "# Rubric giả");
            var rows = Enumerable.Range(1, 150).ToDictionary(i => i.ToString(),
                _ => new ExcerptReplayRow(answer, ExcerptRoute.DocumentExcerpt, null, [new(Other.Id, Other.Text)]));
            var data = new ExcerptReplayFile(ExcerptReplayFile.HistoricalCommit, ExcerptReplayFile.PinnedDigest, rows);
            rows["1"] = rows["1"] with { Answer = "Câu không khớp" };
            await data.SaveNewAsync(replay);
            Assert.Throws<InvalidDataException>(() => GradingSheetV2.Write(master, ideas, rubric, directory.FullName, excerptReplayPath: replay));
            Assert.False(File.Exists(Path.Combine(directory.FullName, GradingSheetV2.FileName)));
            rows["1"] = rows["1"] with { Answer = answer };
            await data.SaveNewAsync(validReplay);
            var export = GradingSheetV2.Write(master, ideas, rubric, directory.FullName, excerptReplayPath: validReplay);
            Assert.Equal(150, export.Rows);
            using var result = new XLWorkbook(export.Path);
            var sheet = result.Worksheet("Phiếu chấm");
            Assert.Equal($"[{Other.Id}]\n{Other.Text}", sheet.Cell(4, 8).GetString());
            Assert.Equal(sheet.Cell(4, 8).GetString(), sheet.Cell(4, 9).GetString());
            await Assert.ThrowsAsync<IOException>(() => data.SaveNewAsync(validReplay));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void Fill(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var column = 0; column < values.Length; column++) sheet.Cell(row, column + 1).Value = values[column];
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The replay must never send chat traffic.");
        }
    }

    private sealed class SyntheticRetriever : IClinicKnowledgeRetriever
    {
        public int Calls { get; private set; }
        public Task<KnowledgeRetrieval> RetrieveAsync(string question, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new KnowledgeRetrieval([new(Top, 1), new(SameHeading, 0.9), new(Other, 0.8), new(BelowThreshold, 0.3)]));
        }
    }
}
