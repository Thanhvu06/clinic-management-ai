extern alias RagEval;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using EvaluationInput = RagEval::ClinicManagement.AI.RagEval.EvaluationInput;
using EvaluationRetrievers = RagEval::ClinicManagement.AI.RagEval.EvaluationRetrievers;
using RetrievalComparison = RagEval::ClinicManagement.AI.RagEval.RetrievalComparison;
using Rq1Statistics = RagEval::ClinicManagement.AI.RagEval.Rq1Statistics;
using StatisticsInput = RagEval::ClinicManagement.AI.RagEval.StatisticsInput;
using StatisticsReport = RagEval::ClinicManagement.AI.RagEval.StatisticsReport;
using BlindAnswerWorkbook = RagEval::ClinicManagement.AI.RagEval.BlindAnswerWorkbook;
using ExternalOutputDirectory = RagEval::ClinicManagement.AI.RagEval.ExternalOutputDirectory;

namespace ClinicManagement.IntegrationTests;

public sealed class RagEvaluationStatisticsTests
{
    [Fact]
    public async Task Compare_runs_all_modes_on_dev_combined_and_reuses_runtime_embedding_cache_without_printing_questions()
    {
        using var temporary = new Temporary();
        var handler = new FakeEmbedding();
        using (var retrievers = await EvaluationRetrievers.CreateAsync(handler, temporary.Root))
        {
            var queries = await EvaluationInput.Parse(["--questions", "combined"]).LoadAsync();
            var report = await RetrievalComparison.EvaluateAsync(queries, retrievers);
            Assert.Equal(12, report.Documents);
            Assert.Equal(4, report.TopKChunks);
            Assert.Equal(3, report.Modes.Count);
            Assert.Equal(3, report.PairwiseHitAt1.Count);
            foreach (var mode in report.Modes)
            {
                Assert.Equal(262, mode.Overall.N);
                Assert.Equal(192, mode.Overall.InScope);
                Assert.Equal(70, mode.Overall.Outside);
                Assert.Equal(262, mode.ByCategory.Values.Sum(group => group.N));
            }
            foreach (var pair in report.PairwiseHitAt1) Assert.Equal(192, pair.N);
            var console = new StringWriter();
            var output = Path.Combine(temporary.Root, "compare.json");
            await RetrievalComparison.WriteAsync(report, output, console);
            var json = await File.ReadAllTextAsync(output);
            foreach (var question in queries)
            {
                Assert.DoesNotContain(question.Text, console.ToString());
                Assert.DoesNotContain(question.Text, json);
            }
            Assert.Contains("12 tài liệu", console.ToString());
            Assert.True(handler.EmbeddingCalls > 0);
        }
        var cachedHandler = new FakeEmbedding();
        using var cached = await EvaluationRetrievers.CreateAsync(cachedHandler, temporary.Root);
        Assert.Equal(0, cachedHandler.EmbeddingCalls);
    }

    [Fact]
    public async Task Dense_searches_all_chunks_without_bm25_gate_and_standalone_mode_has_no_pairwise_tests()
    {
        using var temporary = new Temporary();
        using var retrievers = await EvaluationRetrievers.CreateAsync(new FakeEmbedding(), temporary.Root);
        const string synthetic = "xyzquux synthetic private input";
        Assert.Empty((await retrievers.Modes["bm25"].RetrieveAsync(synthetic)).Hits);
        Assert.Empty((await retrievers.Modes["hybrid"].RetrieveAsync(synthetic)).Hits);
        Assert.Equal(4, (await retrievers.Modes["dense"].RetrieveAsync(synthetic)).Hits.Count);
        var report = await RetrievalComparison.EvaluateAsync([new(synthetic, null, Category: "Ngoài phạm vi xa")], retrievers, ["dense"]);
        Assert.Empty(report.PairwiseHitAt1);
        Assert.Equal(0, report.Modes[0].Overall.CorrectRejection);
    }

    [Fact]
    public async Task Rq1_reads_synthetic_template_by_stt_decodes_key_and_reports_known_paired_statistics_without_content()
    {
        using var temporary = new Temporary();
        var path = BuildSyntheticGrades(temporary.Root);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var report = Rq1Statistics.Read(path);
        var overall = report.Overall;
        Assert.Equal(4, overall.N);
        Assert.Equal(1, report.IncompleteRows);
        Assert.Equal(1, overall.Llm.Mean);
        Assert.Equal(0.75, overall.Llm.Median);
        Assert.Equal(0.875, overall.Extractive.Mean);
        Assert.Equal(1, overall.Extractive.Median);
        Assert.Equal((1, 1, 2), (overall.LlmBetter, overall.Equal, overall.LlmWorse));
        Assert.Equal(1, overall.Wilcoxon!.PValue);
        Assert.Equal(3, overall.Wilcoxon.NonZeroN);
        Assert.Equal((2, 3), (overall.LlmUnsafe, overall.ExtractiveUnsafe));
        Assert.Equal((1, 2), (overall.UnsafeMcNemar!.B, overall.UnsafeMcNemar.C));
        Assert.Equal(7.0 / 47, overall.Agreement.Kappa!.Value, 12);
        Assert.Equal(3.0 / 31, overall.Agreement.LinearWeightedKappa!.Value, 12);
        Assert.Equal(25, overall.LlmLatency.MedianMs);
        Assert.Equal(40, overall.LlmLatency.P95Ms);
        Assert.Equal(2.5, overall.ExtractiveLatency.MedianMs);
        Assert.Equal(4, overall.ExtractiveLatency.P95Ms);
        Assert.Equal(4, report.ByCategory.Values.Sum(group => group.N));
        Assert.Equal(1, report.ByCategory["Khẩn cấp / an toàn"].N);
        var output = Path.Combine(temporary.Root, "rq1.json");
        var console = new StringWriter();
        await Rq1Statistics.WriteAsync(report, output, console);
        var json = await File.ReadAllTextAsync(output);
        foreach (var privateText in new[] { "SYNTHETIC_QUESTION_PRIVATE", "SYNTHETIC_ANSWER_PRIVATE", "SYNTHETIC_NOTE_PRIVATE" })
        {
            Assert.DoesNotContain(privateText, console.ToString());
            Assert.DoesNotContain(privateText, json);
        }
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        Assert.Throws<IOException>(() => StatisticsReport.EnsureNew(output));
    }

    [Fact]
    public async Task Statistics_input_accepts_csv_xlsx_and_old_question_selectors_without_combining_sources()
    {
        using var temporary = new Temporary();
        var csv = Path.Combine(temporary.Root, "synthetic.csv");
        await File.WriteAllTextAsync(csv, "STT,Người viết,Câu hỏi,Loại,Tài liệu đúng\n1,A,Câu giả,Trong phạm vi,accounts\n2,A,Câu giả khác,Ngoài phạm vi xa,\n");
        var selected = StatisticsInput.Parse(["--csv", csv, "--output", Path.Combine(temporary.Root, "out.json")], "compare-retrieval");
        Assert.Equal(2, (await selected.LoadQuestionsAsync()).Count);
        var xlsx = BuildSyntheticGrades(temporary.Root);
        using (var workbook = new XLWorkbook(xlsx))
        {
            var questions = workbook.Worksheet("Câu hỏi");
            for (var row = 4; row <= 7; row++)
            {
                questions.Cell(row, 3).Value = "SYNTHETIC_QUESTION_PRIVATE";
                questions.Cell(row, 5).Value = row <= 5 ? "accounts" : string.Empty;
            }
            workbook.Save();
        }
        var input = StatisticsInput.Parse(["--xlsx", xlsx], "compare-retrieval");
        Assert.Equal(4, (await input.LoadQuestionsAsync()).Count);
        Assert.Equal(262, (await StatisticsInput.Parse(["--questions", "combined"], "compare-retrieval").LoadQuestionsAsync()).Count);
        Assert.Throws<ArgumentException>(() => StatisticsInput.Parse(["--csv", csv, "--xlsx", xlsx], "compare-retrieval"));
        Assert.Throws<ArgumentException>(() => StatisticsInput.Parse(["--questions", "combined"], "rq1-stats"));
    }

    [Fact]
    public void Rq1_invalid_scores_and_safety_marks_are_rejected_without_cell_values_in_error()
    {
        using var temporary = new Temporary();
        var path = BuildSyntheticGrades(temporary.Root);
        using (var workbook = new XLWorkbook(path))
        {
            workbook.Worksheet("Phiếu chấm").Cell(4, 8).Value = "SYNTHETIC_INVALID_PRIVATE";
            workbook.Save();
        }
        var error = Assert.Throws<InvalidDataException>(() => Rq1Statistics.Read(path));
        Assert.DoesNotContain("SYNTHETIC_INVALID_PRIVATE", error.Message);
    }

    private static string BuildSyntheticGrades(string directory)
    {
        var path = Path.Combine(directory, "synthetic-grades.xlsx");
        var template = Path.Combine(ExternalOutputDirectory.FindRepositoryRoot(), "docs", "nckh", "templates", "RAG_bo_cau_hoi_an_mau.xlsx");
        File.Copy(template, path);
        using var book = new XLWorkbook(path);
        var sheet = book.Worksheet("Phiếu chấm");
        var key = book.Worksheet("Khóa");
        var questionSheet = book.Worksheet("Câu hỏi");
        int[] order = [4, 2, 1, 3], keyOrder = [3, 1, 4, 2];
        int[][] scores = [[2, 0, 2, 0], [1, 2, 1, 0], [0, 2, 1, 1], [2, 0, 0, 1]];
        bool[][] unsafeMarks = [[false, true, false, false], [false, true, false, false], [false, false, true, true], [false, false, true, false]];
        string[] categories = ["Trong phạm vi", "Diễn đạt khác", "Ngoài phạm vi gần", "Khẩn cấp / an toàn"];
        for (var index = 0; index < 4; index++)
        {
            var id = order[index]; var row = index + 4;
            sheet.Cell(row, 1).Value = id;
            sheet.Cell(row, 2).Value = "SYNTHETIC_QUESTION_PRIVATE";
            sheet.Cell(row, 4).Value = "SYNTHETIC_ANSWER_PRIVATE";
            sheet.Cell(row, 10).Value = "SYNTHETIC_NOTE_PRIVATE";
            int[] scoreColumns = [6, 7, 11, 12], safetyColumns = [8, 9, 13, 14];
            for (var j = 0; j < 4; j++)
            {
                sheet.Cell(row, scoreColumns[j]).Value = scores[id - 1][j];
                sheet.Cell(row, safetyColumns[j]).Value = unsafeMarks[id - 1][j] ? "Không đạt" : "Đạt";
            }
            questionSheet.Cell(index + 4, 4).Value = categories[index];
            key.Cell(row, 1).Value = keyOrder[index];
            key.Cell(row, 2).Value = keyOrder[index] is 1 or 3 ? "LLM" : "Trích đoạn";
            key.Cell(row, 3).Value = keyOrder[index] * 10;
            key.Cell(row, 4).Value = keyOrder[index];
        }
        sheet.Cell(8, 6).Value = 1;
        key.Cell(8, 2).Value = "LLM";
        book.Save();
        return path;
    }

    private sealed class FakeEmbedding : HttpMessageHandler
    {
        public int EmbeddingCalls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { models = Array.Empty<object>() }) };
            Assert.Equal("/api/embed", request.RequestUri.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("bge-m3", body.RootElement.GetProperty("model").GetString());
            EmbeddingCalls++;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                { embeddings = body.RootElement.GetProperty("input").EnumerateArray().Select(_ => new[] { 1.0, 0.0 }).ToArray() }) };
        }
    }

    private sealed class Temporary : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "rag-stats-test-" + Guid.NewGuid().ToString("N"));
        public Temporary() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
