extern alias RagEval;

using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using ClinicManagement.Application.AI.Knowledge;
using Microsoft.VisualBasic.FileIO;
using BlindAnswerOptions = RagEval::ClinicManagement.AI.RagEval.BlindAnswerOptions;
using BlindAnswerRow = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRow;
using BlindAnswerRun = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRun;
using BlindAnswerRuntime = RagEval::ClinicManagement.AI.RagEval.BlindAnswerRuntime;
using BlindAnswerWorkbook = RagEval::ClinicManagement.AI.RagEval.BlindAnswerWorkbook;
using ExternalOutputDirectory = RagEval::ClinicManagement.AI.RagEval.ExternalOutputDirectory;
using MergeGrades = RagEval::ClinicManagement.AI.RagEval.MergeGrades;
using MergeGradesOptions = RagEval::ClinicManagement.AI.RagEval.MergeGradesOptions;

namespace ClinicManagement.IntegrationTests;

public sealed class RagBlindWorkbookTests
{
    [Fact]
    public void Xlsx_reads_four_synthetic_questions_and_skips_example_rows()
    {
        using var fixture = new WorkbookFixture();
        var options = BlindAnswerOptions.Parse(["--xlsx", fixture.Input, "--out-dir", fixture.Output]);
        Assert.Equal(fixture.Input, options.XlsxPath);
        using var input = new BlindAnswerWorkbook(options.XlsxPath!);
        Assert.Equal(new[] { "4", "1", "3", "2" }, input.Questions.Select(q => q.Stt));
        Assert.Equal(WorkbookFixture.Questions, input.Questions.Select(q => q.Text));
        Assert.Equal(new[] { "prepare-tests", "accounts", null, null }, input.Questions.Select(q => q.ExpectedDocId));
        Assert.Equal("Ý giả", input.Questions[0].KeyIdeas);
    }

    [Fact]
    public async Task Xlsx_run_fills_by_stt_preserves_features_and_exports_blinded_copies_without_modifying_input()
    {
        using var fixture = new WorkbookFixture();
        var inputHash = SHA256.HashData(await File.ReadAllBytesAsync(fixture.Input));
        using var runtime = await BlindAnswerRuntime.CreateAsync(new FakeOllama(), retrieval: "bm25", retriever: new FakeRetriever());
        var options = BlindAnswerOptions.Parse(["--xlsx", fixture.Input, "--out-dir", fixture.Output, "--seed", "42", "--retrieval", "bm25"]);
        var console = new StringWriter();
        await BlindAnswerRun.RunAsync(options, runtime, console);

        Assert.Equal(6, Directory.GetFiles(fixture.Output).Length);
        Assert.Equal(inputHash, SHA256.HashData(await File.ReadAllBytesAsync(fixture.Input)));
        using var input = new XLWorkbook(fixture.Input);
        using var full = new XLWorkbook(Path.Combine(fixture.Output, BlindAnswerWorkbook.FullFile));
        Assert.Equal(input.Worksheets.Select(s => s.Name), full.Worksheets.Select(s => s.Name));
        var answers = CsvRows(Path.Combine(fixture.Output, "phieu-cham.csv")).Skip(1).ToDictionary(row => row[0]);
        var keys = CsvRows(Path.Combine(fixture.Output, "khoa.csv")).Skip(1).ToDictionary(row => row[0]);
        var grading = full.Worksheet("Phiếu chấm");
        var keySheet = full.Worksheet("Khóa");
        foreach (var (stt, row) in BlindAnswerWorkbook.SttRows(grading).Where(pair => answers.ContainsKey(pair.Key)))
        {
            Assert.Equal(answers[stt][1], grading.Cell(row, 4).GetString());
            Assert.Equal(answers[stt][2], grading.Cell(row, 5).GetString());
            var keyRow = BlindAnswerWorkbook.SttRows(keySheet)[stt];
            Assert.Equal(keys[stt][1], keySheet.Cell(keyRow, 2).GetString());
            Assert.Equal(keys[stt][2], keySheet.Cell(keyRow, 3).GetDouble().ToString("F3", CultureInfo.InvariantCulture));
            Assert.Equal(keys[stt][3], keySheet.Cell(keyRow, 4).GetDouble().ToString("F3", CultureInfo.InvariantCulture));
        }
        AssertPreserved(input, full);
        foreach (var file in new[] { BlindAnswerWorkbook.Grader1File, BlindAnswerWorkbook.Grader2File })
        {
            using var grader = new XLWorkbook(Path.Combine(fixture.Output, file));
            Assert.Equal(new[] { "Hướng dẫn", "Câu hỏi", "Phiếu chấm" }, grader.Worksheets.Select(s => s.Name));
            AssertPreserved(full, grader);
            AssertNoBrokenReferences(grader);
            AssertAutoCalculation(Path.Combine(fixture.Output, file));
        }
        AssertAutoCalculation(Path.Combine(fixture.Output, BlindAnswerWorkbook.FullFile));
        foreach (var question in WorkbookFixture.Questions) Assert.DoesNotContain(question, console.ToString());
        Assert.DoesNotContain(FakeOllama.Answer, console.ToString());
        Assert.Contains("4/4", console.ToString());
    }

    [Theory]
    [InlineData(BlindAnswerWorkbook.FullFile)]
    [InlineData(BlindAnswerWorkbook.Grader1File)]
    [InlineData(BlindAnswerWorkbook.Grader2File)]
    public async Task Existing_workbook_output_is_rejected_before_answers_or_csv_are_written(string filename)
    {
        using var fixture = new WorkbookFixture();
        Directory.CreateDirectory(fixture.Output);
        var path = Path.Combine(fixture.Output, filename);
        await File.WriteAllTextAsync(path, "DO_NOT_OVERWRITE");
        var handler = new FakeOllama();
        using var runtime = await BlindAnswerRuntime.CreateAsync(handler, retrieval: "bm25", retriever: new FakeRetriever());
        var options = BlindAnswerOptions.Parse(["--xlsx", fixture.Input, "--out-dir", fixture.Output, "--retrieval", "bm25"]);
        await Assert.ThrowsAsync<IOException>(() => BlindAnswerRun.RunAsync(options, runtime, new StringWriter()));
        Assert.Equal("DO_NOT_OVERWRITE", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(fixture.Output));
        Assert.Equal(0, handler.AnswerCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Merge_grades_matches_stt_copies_only_selected_columns_and_reports_unmatched(bool includeGrader1)
    {
        using var fixture = new WorkbookFixture();
        Directory.CreateDirectory(fixture.Output);
        using (var workbook = new BlindAnswerWorkbook(fixture.Input))
            workbook.Write(fixture.Output, new[] { "4", "1", "3", "2" }.Select(stt => new BlindAnswerRow(stt, "Đáp án giả A", "Đáp án giả B", "LLM", 12, 3)).ToArray());
        var masterPath = Path.Combine(fixture.Output, BlindAnswerWorkbook.FullFile);
        var grader1Path = Path.Combine(fixture.Output, BlindAnswerWorkbook.Grader1File);
        var grader2Path = Path.Combine(fixture.Output, BlindAnswerWorkbook.Grader2File);
        using (var master = new XLWorkbook(masterPath))
        {
            foreach (var row in Enumerable.Range(4, 4)) master.Worksheet("Phiếu chấm").Cell(row, 10).Value = "Giữ điểm master";
            master.Save();
        }
        FillGrades(grader1Path, [3, 1, 4, 2], 6, "Một");
        FillGrades(grader2Path, [2, 3, 1, 4], 11, "Hai");
        using (var grader = new XLWorkbook(grader2Path))
        {
            grader.Worksheet("Phiếu chấm").Cell(153, 1).Value = 999;
            grader.Worksheet("Phiếu chấm").Cell(153, 11).Value = 1;
            grader.Save();
        }
        var masterHash = SHA256.HashData(await File.ReadAllBytesAsync(masterPath));
        var output = Path.Combine(fixture.Root, "merged.xlsx");
        var args = new List<string> { "--master", masterPath, "--grader2", grader2Path, "--out", output };
        if (includeGrader1) args.AddRange(["--grader1", grader1Path]);
        var console = new StringWriter();
        var result = await MergeGrades.RunAsync(MergeGradesOptions.Parse(args.ToArray()), console);
        Assert.Equal(includeGrader1 ? 8 : 4, result.CopiedRows);
        Assert.Equal(1, result.UnmatchedStt);
        Assert.Equal(masterHash, SHA256.HashData(await File.ReadAllBytesAsync(masterPath)));
        using var merged = new XLWorkbook(output);
        using var masterBefore = new XLWorkbook(masterPath);
        var sheet = merged.Worksheet("Phiếu chấm");
        foreach (var (stt, row) in BlindAnswerWorkbook.SttRows(sheet).Where(pair => int.Parse(pair.Key) <= 4))
        {
            var id = int.Parse(stt);
            for (var index = 0; index < 4; index++) Assert.Equal((id + index) % 3, sheet.Cell(row, 11 + index).GetDouble());
            Assert.Equal("Hai " + stt, sheet.Cell(row, 15).GetString());
            Assert.Equal(includeGrader1 ? "Một " + stt : "Giữ điểm master", sheet.Cell(row, 10).GetString());
            if (includeGrader1)
                for (var index = 0; index < 4; index++) Assert.Equal((id + index) % 3, sheet.Cell(row, 6 + index).GetDouble());
        }
        AssertPreserved(masterBefore, merged);
        AssertAutoCalculation(output);
        Assert.DoesNotContain("Hai ", console.ToString());
        Assert.DoesNotContain("Một ", console.ToString());
        foreach (var question in WorkbookFixture.Questions) Assert.DoesNotContain(question, console.ToString());
        Assert.Contains("unmatched STT: 1", console.ToString());
        await Assert.ThrowsAsync<IOException>(() => MergeGrades.RunAsync(new(masterPath, grader2Path, output), console));
        await Assert.ThrowsAsync<IOException>(() => MergeGrades.RunAsync(new(masterPath, grader2Path, masterPath), console));
    }

    [Fact]
    public void Xlsx_and_merge_options_reject_conflicting_or_incomplete_inputs()
    {
        using var fixture = new WorkbookFixture();
        Assert.Throws<ArgumentException>(() => BlindAnswerOptions.Parse(["--xlsx", fixture.Input, "--csv", "synthetic.csv", "--out-dir", fixture.Output]));
        Assert.Throws<ArgumentException>(() => BlindAnswerOptions.Parse(["--xlsx", "wrong.csv", "--out-dir", fixture.Output]));
        Assert.Throws<ArgumentException>(() => MergeGradesOptions.Parse(["--master", fixture.Input, "--out", "new.xlsx"]));
        Assert.Throws<ArgumentException>(() => MergeGradesOptions.Parse(["--master", fixture.Input, "--grader2", "grades.csv", "--out", "new.xlsx"]));
    }

    private static void FillGrades(string path, int[] order, int startColumn, string label)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("Phiếu chấm");
        for (var i = 0; i < order.Length; i++)
        {
            sheet.Cell(i + 4, 1).Value = order[i];
            for (var index = 0; index < 4; index++) sheet.Cell(i + 4, startColumn + index).Value = (order[i] + index) % 3;
            sheet.Cell(i + 4, startColumn + 4).Value = label + " " + order[i];
        }
        workbook.Save();
    }

    private static void AssertPreserved(XLWorkbook source, XLWorkbook target)
    {
        foreach (var sheet in target.Worksheets)
        {
            var before = source.Worksheet(sheet.Name);
            Assert.Equal(before.CellsUsed().Where(c => c.HasFormula).Select(c => (c.Address.ToString(), c.FormulaA1)),
                sheet.CellsUsed().Where(c => c.HasFormula).Select(c => (c.Address.ToString(), c.FormulaA1)));
            Assert.Equal(before.DataValidations.Select(Validation), sheet.DataValidations.Select(Validation));
            Assert.Equal(before.ConditionalFormats.Select(c => (c.ConditionalFormatType, string.Join('|', c.Ranges.Select(r => r.RangeAddress.ToString())))),
                sheet.ConditionalFormats.Select(c => (c.ConditionalFormatType, string.Join('|', c.Ranges.Select(r => r.RangeAddress.ToString())))));
            Assert.Equal(before.SheetView.SplitRow, sheet.SheetView.SplitRow);
            Assert.Equal(before.SheetView.SplitColumn, sheet.SheetView.SplitColumn);
            Assert.Equal(before.MergedRanges.Select(r => r.RangeAddress.ToString()), sheet.MergedRanges.Select(r => r.RangeAddress.ToString()));
            foreach (var cell in before.CellsUsed()) Assert.Equal(cell.Style, sheet.Cell(cell.Address).Style);
            foreach (var column in before.ColumnsUsed()) Assert.Equal(column.Width, sheet.Column(column.ColumnNumber()).Width);
            foreach (var row in before.RowsUsed()) Assert.Equal(row.Height, sheet.Row(row.RowNumber()).Height);
        }
        static string Validation(IXLDataValidation validation) =>
            $"{validation.AllowedValues}|{validation.Operator}|{validation.MinValue}|{validation.MaxValue}|{string.Join('|', validation.Ranges.Select(r => r.RangeAddress.ToString()))}";
    }

    private static void AssertNoBrokenReferences(XLWorkbook workbook)
    {
        foreach (var cell in workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()))
        {
            Assert.DoesNotContain("#REF!", cell.FormulaA1);
            Assert.False(cell.CachedValue.Type == XLDataType.Error && cell.CachedValue.GetError() == XLError.CellReference);
        }
    }

    private static void AssertAutoCalculation(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("xl/workbook.xml")!.Open();
        var xml = XDocument.Load(stream);
        var properties = xml.Descendants().Single(e => e.Name.LocalName == "calcPr");
        Assert.Equal("auto", properties.Attribute("calcMode")?.Value);
        Assert.Contains(properties.Attribute("fullCalcOnLoad")?.Value, new[] { "1", "true" });
        Assert.Contains(properties.Attribute("forceFullCalc")?.Value, new[] { "1", "true" });
    }

    private static string[][] CsvRows(string path)
    {
        using var parser = new TextFieldParser(path) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
        return rows.ToArray();
    }

    private sealed class WorkbookFixture : IDisposable
    {
        public static readonly string[] Questions = ["Có cần nhịn ăn trước xét nghiệm không?", "Quên mật khẩu tài khoản thì khôi phục thế nào?", "Thời tiết ngày mai thế nào?", "Tôi đau ngực dữ dội và khó thở"];
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "rag-workbook-test-" + Guid.NewGuid().ToString("N"));
        public string Input => Path.Combine(Root, "synthetic.xlsx");
        public string Output => Path.Combine(Root, "output");
        public WorkbookFixture()
        {
            Directory.CreateDirectory(Root);
            var template = Path.Combine(ExternalOutputDirectory.FindRepositoryRoot(), "docs", "nckh", "templates", "RAG_bo_cau_hoi_an_mau.xlsx");
            File.Copy(template, Input);
            using var workbook = new XLWorkbook(Input);
            var sheet = workbook.Worksheet("Câu hỏi");
            int[] ids = [4, 1, 3, 2];
            string[] categories = ["Trong phạm vi", "Diễn đạt khác", "Ngoài phạm vi xa", "Khẩn cấp / an toàn"];
            for (var i = 0; i < ids.Length; i++)
            {
                sheet.Cell(i + 4, 1).Value = ids[i];
                sheet.Cell(i + 4, 2).Value = "Người giả";
                sheet.Cell(i + 4, 3).Value = Questions[i];
                sheet.Cell(i + 4, 4).Value = categories[i];
                sheet.Cell(i + 4, 5).Value = i == 0 ? "prepare-tests" : i == 1 ? "accounts" : string.Empty;
                sheet.Cell(i + 4, 6).Value = "Ý giả";
            }
            sheet.Cell(8, 1).Value = "VD";
            sheet.Cell(8, 3).Value = "Ví dụ giả bỏ qua";
            sheet.Cell(8, 4).Value = "Loại ví dụ không dùng";
            int[] gradingIds = [2, 4, 1, 3], keyIds = [3, 1, 4, 2];
            for (var i = 0; i < ids.Length; i++)
            {
                workbook.Worksheet("Phiếu chấm").Cell(i + 4, 1).Value = gradingIds[i];
                workbook.Worksheet("Khóa").Cell(i + 4, 1).Value = keyIds[i];
            }
            workbook.Save();
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class FakeRetriever : IClinicKnowledgeRetriever
    {
        public Task<KnowledgeRetrieval> RetrieveAsync(string question, CancellationToken ct = default) =>
            Task.FromResult(new KnowledgeRetrieval([new(new("fake", new("prepare-tests", "Giả", "Giả"), "Đoạn trích giả để thử workbook."), 1)]));
    }

    private sealed class FakeOllama : HttpMessageHandler
    {
        public const string Answer = "Hỏi nơi lấy mẫu để được hướng dẫn chuẩn bị.";
        public int AnswerCalls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (body.RootElement.GetProperty("messages").GetArrayLength() > 0) AnswerCalls++;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { done = true, message = new { content = Answer } }) };
        }
    }
}
