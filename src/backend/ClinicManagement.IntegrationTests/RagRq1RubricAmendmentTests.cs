extern alias RagEval;

using ClosedXML.Excel;
using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public class RagRq1RubricAmendmentTests
{
    [Fact]
    public async Task GradingSheetV2ExportsTheAmendmentAfterTheExistingRubric()
    {
        using var fixture = new WorkbookFixture();
        await Rq1WorkbookCommands.RunAsync("grading-sheet-v2",
            ["--master", fixture.Master, "--ideas", fixture.Ideas], TextWriter.Null);

        using var workbook = new XLWorkbook(Path.Combine(fixture.Root, GradingSheetV2.FileName));
        var rubric = workbook.Worksheet("Rubric");
        var lines = rubric.Column(1).CellsUsed().Select(cell => cell.GetString()).ToArray();
        var amendment = Array.IndexOf(lines, "## G. Sửa đổi v1.0.2 – 14/11/2026");
        var prior = Array.FindIndex(lines, line => line.StartsWith("## F. ", StringComparison.Ordinal));
        Assert.True(prior >= 0 && amendment > prior);
        Assert.Equal("Sửa đổi sau khi người chấm đã xem câu trả lời; chỉ diễn giải chỗ rubric chưa nói rõ, không đổi công thức điểm.",
            lines.Skip(amendment + 1).First(line => line.Length > 0));
        var rules = lines.Skip(amendment + 1)
            .Where(line => line.Length >= 4 && line[0] == 'G' && char.IsAsciiDigit(line[1]) && line[2] == '.');
        Assert.Equal(new[] { "G1", "G2", "G3", "G4", "G5", "G6" }, rules.Select(line => line[..2]));
    }

    private sealed class WorkbookFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("rq1-amendment-").FullName;
        public string Master => Path.Combine(Root, "master.xlsx");
        public string Ideas => Path.Combine(Root, "ideas.xlsx");

        public WorkbookFixture()
        {
            using var master = new XLWorkbook();
            var questions = master.AddWorksheet("Câu hỏi");
            questions.Cell(4, 1).Value = "01";
            questions.Cell(4, 3).Value = "Câu hỏi giả";
            questions.Cell(4, 4).Value = "Trong phạm vi";
            var grades = master.AddWorksheet("Phiếu chấm");
            grades.Cell(4, 1).Value = "01";
            grades.Cell(4, 4).Value = "Câu trả lời giả A";
            grades.Cell(4, 5).Value = "Câu trả lời giả B";
            var sources = master.AddWorksheet("Truy xuất");
            sources.Cell(3, 1).Value = "STT";
            sources.Cell(3, 2).Value = "Đoạn truy xuất A";
            sources.Cell(3, 3).Value = "Đoạn truy xuất B";
            sources.Cell(4, 1).Value = "01";
            master.SaveAs(Master);

            using var ideas = new XLWorkbook();
            var sheet = ideas.AddWorksheet("Ý chính");
            sheet.Cell(1, 1).Value = "Mã câu";
            sheet.Cell(1, 6).Value = "Ý chính";
            sheet.Cell(1, 7).Value = "DB";
            sheet.Cell(2, 1).Value = "01";
            ideas.SaveAs(Ideas);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
