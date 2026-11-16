extern alias RagEval;

using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class RagRq1MergeNewlinesTests
{
    private const int Row = 4;
    private const string LfAnswer = "Alpha\nBeta";

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Merge_accepts_different_line_endings_on_either_side(string lineEnding)
    {
        var alternateAnswer = $"Alpha{lineEnding}Beta";
        using var master = new XLWorkbook();
        using var grader = new XLWorkbook();
        var (source, rows) = Prepare(master, grader, alternateAnswer, alternateAnswer);

        Assert.Equal((1, 0), MergedChecklists.Copy(source, master, rows, 11));
        Assert.Equal(2, master.Worksheet(Rq1Workbook.Sheet).Cell(Row, 11).GetValue<int>());
        Assert.Equal(2, master.Worksheet(Rq1Workbook.Sheet).Cell(Row, 12).GetValue<int>());
    }

    [Fact]
    public void Merge_rejects_an_answer_with_one_different_letter()
    {
        using var master = new XLWorkbook();
        using var grader = new XLWorkbook();
        var (source, rows) = Prepare(master, grader, LfAnswer, "Alpha\r\nBeto");

        Assert.Throws<InvalidDataException>(() => MergedChecklists.Copy(source, master, rows, 11));
    }

    private static (IXLWorksheet Source, Dictionary<string, int> Rows) Prepare(
        XLWorkbook master, XLWorkbook grader, string masterB, string graderA)
    {
        var questions = master.AddWorksheet("Câu hỏi");
        questions.Cell(Row, 1).Value = "01";
        questions.Cell(Row, 3).Value = "Câu hỏi giả";
        questions.Cell(Row, 4).Value = "Trong phạm vi";
        var legacy = master.AddWorksheet(Rq1Workbook.Sheet);
        legacy.Cell(Row, 1).Value = "01";
        legacy.Cell(Row, Rq1Workbook.AnswerA).Value = LfAnswer;
        legacy.Cell(Row, Rq1Workbook.AnswerB).Value = masterB;

        var sheet = grader.AddWorksheet(Rq1Workbook.Sheet);
        sheet.Cell(1, 1).Value = GradingSheetV2.Marker;
        sheet.Cell(Row, 1).Value = "01";
        sheet.Cell(Row, 2).Value = "Trong phạm vi";
        sheet.Cell(Row, 3).Value = "Câu hỏi giả";
        sheet.Cell(Row, Rq1Workbook.AnswerA).Value = graderA;
        sheet.Cell(Row, Rq1Workbook.AnswerB).Value = LfAnswer;
        string[] checklist = [Rq1Checklist.Correct, Rq1Checklist.Full, Rq1Checklist.No,
            Rq1Checklist.No, Rq1Checklist.Safe, Rq1Checklist.No];
        foreach (var start in new[] { Rq1Workbook.ChecklistA, Rq1Workbook.ChecklistB })
            for (var i = 0; i < checklist.Length; i++)
                sheet.Cell(Row, start + i).Value = checklist[i];
        return (sheet, BlindAnswerWorkbook.SttRows(legacy));
    }
}
