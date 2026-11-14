using ClosedXML.Excel;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed record KeyIdeasResult(IReadOnlyDictionary<string, int> ByGroup, int WithDocument, int WithoutDocument);

internal sealed class KeyIdeasAlreadyExistsException() : IOException("y-chinh.xlsx đã tồn tại.");

public static class KeyIdeasTemplate
{
    public const string FileName = "y-chinh.xlsx";

    public static KeyIdeasResult Write(string input, string output, string? documentsDirectory = null)
    {
        if (File.Exists(output) || Directory.Exists(output)) throw new KeyIdeasAlreadyExistsException();
        var questions = BlindAnswerWorkbook.LoadQuestions(input)
            .OrderBy(q => q.Category, StringComparer.Ordinal)
            .ThenBy(q => int.TryParse(q.Stt, out var code) ? code : int.MaxValue)
            .ThenBy(q => q.Stt, StringComparer.Ordinal).ToArray();
        var documents = Directory.EnumerateFiles(documentsDirectory ?? MarkdownKnowledgeCatalog.DefaultDirectory, "*.md")
            .Select(File.ReadAllText)
            .ToDictionary(text => MarkdownKnowledgeCatalog.Parse(text)[0].Source.DocId, StringComparer.Ordinal);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(Rq1Workbook.IdeasSheet);
        Rq1Workbook.Headings(sheet, 1, ["Mã câu", "Nhóm", "Câu hỏi", "Mã tài liệu đúng", "Tài liệu đúng (tham khảo)", "Ý chính", "DB"]);
        sheet.Column(1).Width = 12;
        Rq1Workbook.Wrap(sheet, 2, 23);
        Rq1Workbook.Wrap(sheet, 3, 55);
        sheet.Column(4).Width = 22;
        Rq1Workbook.Wrap(sheet, 5, 100);
        Rq1Workbook.Wrap(sheet, 6, 45);
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        for (var i = 0; i < questions.Length; i++)
        {
            var question = questions[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = question.Stt!;
            sheet.Cell(row, 2).Value = question.Category!;
            sheet.Cell(row, 3).Value = question.Text;
            if (question.ExpectedDocId is { } id)
            {
                if (!documents.TryGetValue(id, out var document))
                    throw new InvalidDataException("A referenced document was not found in Knowledge/Documents.");
                sheet.Cell(row, 4).Value = id;
                sheet.Cell(row, 5).Value = document;
            }
            if (question.Category is "Ngoài phạm vi gần" or "Ngoài phạm vi xa")
                sheet.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.LightGray;
            Rq1Workbook.FitTextRow(sheet, row, 2, 3, 5);
        }
        sheet.Range(1, 1, questions.Length + 1, 7).SetAutoFilter();
        Rq1Workbook.Dropdown(sheet.Range(2, 7, questions.Length + 1, 7), Rq1Checklist.Yes);
        try { BlindAnswerWorkbook.SaveNew(workbook, output); }
        catch (IOException) when (File.Exists(output) || Directory.Exists(output))
        {
            throw new KeyIdeasAlreadyExistsException();
        }
        var withDocument = questions.Count(q => q.ExpectedDocId is not null);
        return new(questions.GroupBy(q => q.Category!).ToDictionary(group => group.Key, group => group.Count()),
            withDocument, questions.Length - withDocument);
    }

    internal static Dictionary<string, (string Ideas, bool Db)> Read(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(Rq1Workbook.IdeasSheet);
        if (Rq1Workbook.Text(sheet.Cell(1, 1)) != "Mã câu" ||
            Rq1Workbook.Text(sheet.Cell(1, 6)) != "Ý chính" || Rq1Workbook.Text(sheet.Cell(1, 7)) != "DB")
            throw new InvalidDataException("The key-ideas workbook has unexpected headers.");
        var values = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
        foreach (var row in sheet.Column(1).CellsUsed().Skip(1))
            if (!values.TryAdd(Rq1Workbook.Text(row),
                    (Rq1Workbook.Text(sheet.Cell(row.Address.RowNumber, 6)), Rq1Workbook.DbFlag(sheet.Cell(row.Address.RowNumber, 7)))))
                throw new InvalidDataException("Duplicate question code in the key-ideas workbook.");
        return values;
    }
}
