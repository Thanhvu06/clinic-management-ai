extern alias RagEval;

using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class RagRq1AgreementTests
{
    private const string InScope = "Trong phạm vi";
    private const string Paraphrase = "Diễn đạt khác";
    private const string OutOfScope = "Ngoài phạm vi gần";
    private static readonly Rq1Checklist Full = new(Rq1Checklist.Correct, Rq1Checklist.Full,
        Rq1Checklist.No, Rq1Checklist.No, Rq1Checklist.Safe);
    private static readonly Rq1Checklist Partial = Full with { C2 = Rq1Checklist.Partial };
    private static readonly Rq1Checklist Wrong = Full with { C1 = Rq1Checklist.Incorrect };

    [Fact]
    public void Agreement_pools_both_sides_by_stt_and_matches_hand_calculated_kappas()
    {
        using var workbook = new XLWorkbook();
        var codex = workbook.AddWorksheet("Checklist NguoiCham1");
        var human = workbook.AddWorksheet("Checklist NguoiCham2");
        WriteRatings(codex, 4, "01", InScope, Wrong, Partial);
        WriteRatings(codex, 5, "02", Paraphrase, Full, Full);
        WriteRatings(codex, 6, "03", OutOfScope, Full, Full);
        WriteRatings(codex, 7, "04", OutOfScope, Full, Full);
        WriteRatings(human, 4, "02", Paraphrase, Partial, Full);
        WriteRatings(human, 5, "01", InScope, Wrong, Full);
        WriteRatings(human, 6, "03", OutOfScope, Full, Full);
        human.Cell(6, Rq1Workbook.ChecklistB).Clear();
        human.Cell(4, Rq1Workbook.ChecklistB + 5).Value = Rq1Checklist.Yes;

        var agreement = Assert.IsType<Rq1AgreementReport>(Rq1Agreement.Read(workbook));
        Assert.Equal(4, agreement.Overall.N);
        Assert.Equal(50, agreement.Overall.PercentAgreement);
        Assert.Equal(0.2, agreement.Overall.Kappa!.Value, 12);
        Assert.Equal(3.0 / 7, agreement.Overall.LinearWeightedKappa!.Value, 12);
        Assert.Equal(new[] { InScope, Paraphrase }, agreement.ByCategory.Keys);
        Assert.All(agreement.ByCategory.Values, group =>
        {
            Assert.Equal(2, group.N);
            Assert.Equal(50, group.PercentAgreement);
        });
        Assert.Equal(new[] { "C1", "C2", "C3", "C4", "C5", "Từ chối" }, agreement.Items.Keys);
        Assert.Equal(new Rq1ItemAgreement(4, 100, 1), agreement.Items["C1"]);
        Assert.Equal(-1.0 / 3, agreement.Items["C2"].Kappa!.Value, 12);
        Assert.Equal(new Rq1ItemAgreement(4, 75, 0), agreement.Items["Từ chối"]);
    }

    [Fact]
    public async Task Merge_copies_ideas_and_c2_only_collapses_not_applicable_on_rows_without_ideas()
    {
        var directory = Directory.CreateTempSubdirectory("rq1-agreement-");
        try
        {
            var masterPath = Path.Combine(directory.FullName, "master.xlsx");
            var codexPath = Path.Combine(directory.FullName, "codex.xlsx");
            var humanPath = Path.Combine(directory.FullName, "human.xlsx");
            var mergedPath = Path.Combine(directory.FullName, "merged.xlsx");
            var reviewedPath = Path.Combine(directory.FullName, "reviewed.xlsx");
            using (var master = new XLWorkbook())
            {
                var grades = master.AddWorksheet("Phiếu chấm");
                var questions = master.AddWorksheet("Câu hỏi");
                var key = master.AddWorksheet("Khóa");
                for (var row = 4; row <= 5; row++)
                {
                    var stt = row.ToString();
                    grades.Cell(row, 1).Value = stt;
                    questions.Cell(row, 1).Value = stt;
                    questions.Cell(row, 4).Value = InScope;
                    WriteKey(key, row, stt);
                }
                master.SaveAs(masterPath);
            }
            var notApplicable = Full with { C2 = Rq1Checklist.NotApplicable };
            WriteGrader(codexPath, Full, notApplicable);
            WriteGrader(humanPath, notApplicable, Full);

            await MergeGrades.RunAsync(new(masterPath, humanPath, mergedPath, codexPath), TextWriter.Null);
            using (var merged = new XLWorkbook(mergedPath))
            {
                foreach (var name in new[] { "Checklist NguoiCham1", "Checklist NguoiCham2" })
                    Assert.Equal("Ý chính giả", merged.Worksheet(name).Cell(5, Rq1Workbook.Ideas).GetString());
                var reviewed = merged.Worksheet("Checklist NguoiCham2");
                WriteRatings(reviewed, 5, "5", InScope, Full, notApplicable);
                merged.SaveAs(reviewedPath);
            }

            var stats = Rq1Statistics.Read(mergedPath, "grader2", reviewedPath);
            var agreement = Assert.IsType<Rq1AgreementReport>(stats.Agreement);
            var c2 = agreement.Items["C2"];
            Assert.Equal(4, c2.N);
            Assert.Equal(50, c2.PercentAgreement);
            Assert.Equal(-1.0 / 3, c2.Kappa!.Value, 12);
            Assert.Equal(new Rq1ScoreAgreement(4, 100, null, null), agreement.Overall);
            Assert.NotNull(stats.ReviewedChecklist);
            using var console = new StringWriter();
            await Rq1Statistics.WriteAsync(stats, Path.Combine(directory.FullName, "stats.json"), console);
            Assert.Contains("Đồng thuận Người chấm (grader2) – Bộ chấm tự động Codex (grader1)", console.ToString());
            Assert.Contains("C2 | N=4 | Đồng thuận (%)=50 | Kappa=-0.3333333333", console.ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Agreement_requires_two_checklists_and_constant_categories_have_undefined_kappa()
    {
        using var workbook = new XLWorkbook();
        var human = workbook.AddWorksheet("Checklist NguoiCham2");
        WriteRatings(human, 4, "01", InScope, Full, Full);
        Assert.Null(Rq1Agreement.Read(workbook));
        var codex = workbook.AddWorksheet("Checklist NguoiCham1");
        WriteRatings(codex, 4, "01", InScope, Full, Full);
        var agreement = Assert.IsType<Rq1AgreementReport>(Rq1Agreement.Read(workbook));
        Assert.Equal(new Rq1ScoreAgreement(2, 100, null, null), agreement.Overall);
        Assert.All(agreement.Items.Values, item => Assert.Equal(new Rq1ItemAgreement(2, 100, null), item));
    }

    [Fact]
    public async Task Level_two_keeps_only_retrieved_questions_that_expect_an_answer()
    {
        using var workbook = new XLWorkbook();
        var master = workbook.AddWorksheet("Phiếu chấm");
        var key = workbook.AddWorksheet("Khóa");
        var human = workbook.AddWorksheet("Checklist NguoiCham2");
        (string Category, string Retrieved, string Db)[] examples =
        [
            (InScope, Rq1Checklist.Yes, ""),
            (Paraphrase, Rq1Checklist.Yes, ""),
            (OutOfScope, Rq1Checklist.Yes, ""),
            (InScope, Rq1Checklist.No, ""),
            (InScope, Rq1Checklist.Yes, Rq1Checklist.Yes)
        ];
        for (var i = 0; i < examples.Length; i++)
        {
            var row = i + 4;
            var stt = row.ToString();
            var (category, retrieved, db) = examples[i];
            master.Cell(row, 1).Value = stt;
            WriteKey(key, row, stt);
            WriteRatings(human, row, stt, category, i < 2 ? Full : Wrong, Partial);
            human.Cell(row, Rq1Workbook.RetrievalSuccess).Value = retrieved;
            human.Cell(row, Rq1Workbook.Db).Value = db;
        }

        var report = Assert.IsType<Rq1DecisionReport>(Rq1DecisionStatistics.Read(workbook, "grader2"));
        Assert.Equal(5, report.Overall.Pairs);
        Assert.Equal(2, report.SuccessfulRetrieval.Pairs);
        Assert.Equal(2, report.SuccessfulRetrieval.T1.Wins);
        using var console = new StringWriter();
        await Rq1DecisionStatistics.WriteAsync(report, console);
        Assert.Contains("Truy xuất thành công (trong phạm vi/diễn đạt khác, ≥1 đoạn) | N=2", console.ToString());
    }

    private static void WriteGrader(string path, Rq1Checklist a, Rq1Checklist b)
    {
        using var workbook = new XLWorkbook();
        var grades = workbook.AddWorksheet("Phiếu chấm");
        grades.Cell(1, 1).Value = GradingSheetV2.Marker;
        WriteRatings(grades, 4, "4", InScope, a, b);
        WriteRatings(grades, 5, "5", InScope, a, b);
        grades.Cell(4, Rq1Workbook.Ideas).Value = "   ";
        grades.Cell(5, Rq1Workbook.Ideas).Value = "Ý chính giả";
        workbook.SaveAs(path);
    }

    private static void WriteKey(IXLWorksheet key, int row, string stt)
    {
        key.Cell(row, 1).Value = stt;
        key.Cell(row, 2).Value = "LLM";
        key.Cell(row, 3).Value = 10;
        key.Cell(row, 4).Value = 20;
    }

    private static void WriteRatings(IXLWorksheet sheet, int row, string stt, string category, Rq1Checklist a, Rq1Checklist b)
    {
        sheet.Cell(row, 1).Value = stt;
        sheet.Cell(row, 2).Value = category;
        WriteSide(Rq1Workbook.ChecklistA, a);
        WriteSide(Rq1Workbook.ChecklistB, b);

        void WriteSide(int column, Rq1Checklist checklist)
        {
            string[] fields = [checklist.C1, checklist.C2, checklist.C3, checklist.C4, checklist.C5, Rq1Checklist.No];
            for (var i = 0; i < fields.Length; i++) sheet.Cell(row, column + i).Value = fields[i];
        }
    }
}
