using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record GradingSheetV2Result(string Path, long Bytes, int Rows, int MissingIdeas, int BlankDb, int MissingSources);

public static class GradingSheetV2
{
    public const string FileName = "RAG_phieu_cham_NguoiCham2_v2.xlsx";
    internal const string Marker = "RQ1 Checklist v2";
    private const string SourcesAHeader = "Đoạn truy xuất A";
    private const string SourcesBHeader = "Đoạn truy xuất B";
    private const string RetrievedHeader = "Truy xuất thành công";

    public static GradingSheetV2Result Write(string masterPath, string ideasPath, string rubricPath, string outputDirectory,
        string? sourcesPath = null)
    {
        using var master = new XLWorkbook(masterPath);
        using var externalSources = sourcesPath is null ? null : new XLWorkbook(sourcesPath);
        var sources = ReadSources(externalSources ?? master, sourcesPath is not null);
        var grades = master.Worksheet(Rq1Workbook.Sheet);
        var roster = master.Worksheet("Câu hỏi");
        var questionRows = BlindAnswerWorkbook.SttRows(roster);
        var ideas = KeyIdeasTemplate.Read(ideasPath);
        var masterRows = BlindAnswerWorkbook.SttRows(grades);
        if (masterRows.Count == 0) throw new InvalidDataException("The master has no grading rows.");

        using var output = new XLWorkbook();
        var sheet = output.AddWorksheet(Rq1Workbook.Sheet);
        var rubric = output.AddWorksheet("Rubric");
        sheet.Cell(1, 1).Value = Marker;
        sheet.Cell(2, 1).Value = "Từ chối: chọn Có nếu câu trả lời từ chối, Không nếu có trả lời nội dung.";
        sheet.Range(2, 1, 2, 5).Merge();
        Rq1Workbook.Headings(sheet, Rq1Workbook.Header, Headers());
        FormatRubric(rubric, File.ReadAllText(rubricPath));
        var missingIdeas = 0;
        var blankDb = 0;
        var missingSources = 0;
        var row = Rq1Workbook.Header;
        foreach (var (code, masterRow) in masterRows)
        {
            row++;
            if (!questionRows.TryGetValue(code, out var questionRow))
                throw new InvalidDataException("A grading row has no matching question code.");
            var category = QuestionCategories.Canonical(roster.Cell(questionRow, 4).GetString());
            var answerA = grades.Cell(masterRow, Rq1Workbook.AnswerA).GetString();
            var answerB = grades.Cell(masterRow, Rq1Workbook.AnswerB).GetString();
            if (string.IsNullOrWhiteSpace(answerA) || string.IsNullOrWhiteSpace(answerB))
                throw new InvalidDataException("A master grading row is missing an A/B answer.");
            var (keyIdeas, db) = ideas.GetValueOrDefault(code, ("", false));
            var (sourcesA, sourcesB, retrieved) = sources.GetValueOrDefault(code, ("", "", null));
            if (keyIdeas.Length == 0) missingIdeas++;
            if (!db) blankDb++;
            if (!sources.ContainsKey(code))
            {
                missingSources++;
                sourcesA = sourcesB = "Thiếu dữ liệu truy xuất đã lưu";
            }

            sheet.Cell(row, 1).Value = code;
            sheet.Cell(row, 2).Value = category;
            sheet.Cell(row, 3).Value = roster.Cell(questionRow, 3).GetString();
            sheet.Cell(row, Rq1Workbook.AnswerA).Value = answerA;
            sheet.Cell(row, Rq1Workbook.AnswerB).Value = answerB;
            sheet.Cell(row, Rq1Workbook.Ideas).Value = keyIdeas;
            sheet.Cell(row, Rq1Workbook.Db).Value = db ? Rq1Checklist.Yes : "";
            sheet.Cell(row, Rq1Workbook.SourcesA).Value = sourcesA;
            sheet.Cell(row, Rq1Workbook.SourcesB).Value = sourcesB;
            if (retrieved is { } success)
                sheet.Cell(row, Rq1Workbook.RetrievalSuccess).Value = success ? Rq1Checklist.Yes : Rq1Checklist.No;
            foreach (var start in new[] { Rq1Workbook.ChecklistA, Rq1Workbook.ChecklistB })
                Rq1Checklist.WriteFormulas(sheet, row, start, Rq1Workbook.ExpectsAnswer(category, db));
        }
        FormatGradingSheet(sheet, row);

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, FileName);
        var timestamp = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMdd_HHmmss_fffffff", System.Globalization.CultureInfo.InvariantCulture);
        var serial = 0;
        while (true)
        {
            try { BlindAnswerWorkbook.SaveNew(output, path); break; }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path))
            {
                var suffix = serial++ == 0 ? timestamp : $"{timestamp}_{serial}";
                path = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(FileName)}_{suffix}.xlsx");
            }
        }
        return new(path, new FileInfo(path).Length, masterRows.Count, missingIdeas, blankDb, missingSources);
    }

    private static Dictionary<string, (string A, string B, bool? Retrieved)> ReadSources(XLWorkbook workbook, bool required)
    {
        if (!workbook.Worksheets.TryGetWorksheet(Rq1Workbook.SourcesSheet, out var sheet))
            return required ? throw new InvalidDataException("The source workbook must include the Truy xuất worksheet.") : [];
        if (Rq1Workbook.Text(sheet.Cell(3, 1)) != "STT" ||
            Rq1Workbook.Text(sheet.Cell(3, 2)) != SourcesAHeader || Rq1Workbook.Text(sheet.Cell(3, 3)) != SourcesBHeader)
            throw new InvalidDataException("The recorded source worksheet has unexpected headers.");
        var hasResult = Rq1Workbook.Text(sheet.Cell(3, 4)) == RetrievedHeader;
        return BlindAnswerWorkbook.SttRows(sheet).ToDictionary(entry => entry.Key, entry =>
            (sheet.Cell(entry.Value, 2).GetString(), sheet.Cell(entry.Value, 3).GetString(),
                hasResult ? Rq1Workbook.YesNo(sheet.Cell(entry.Value, 4)) : null), StringComparer.Ordinal);
    }

    private static string[] Headers()
    {
        string[] common = ["STT", "Nhóm", "Câu hỏi", "Trả lời A", "Trả lời B", "Ý chính", "DB", SourcesAHeader, SourcesBHeader];
        string[] sideHeaders = ["C1", "C2", "C3", "C4", "C5", "Từ chối", "Ghi chú", "Điểm", "Mã"];
        return [..common, ..sideHeaders.Select(label => $"{label} A"), ..sideHeaders.Select(label => $"{label} B"), RetrievedHeader];
    }

    private static void FormatGradingSheet(IXLWorksheet sheet, int lastRow)
    {
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Column(1).Width = 10;
        Rq1Workbook.Wrap(sheet, 2, 24);
        Rq1Workbook.Wrap(sheet, 3, 44);
        Rq1Workbook.Wrap(sheet, 4, 60);
        Rq1Workbook.Wrap(sheet, 5, 60);
        Rq1Workbook.Wrap(sheet, 6, 40);
        sheet.Column(7).Width = 8;
        Rq1Workbook.Wrap(sheet, 8, 70);
        Rq1Workbook.Wrap(sheet, 9, 70);
        foreach (var start in new[] { Rq1Workbook.ChecklistA, Rq1Workbook.ChecklistB })
        {
            for (var i = 0; i < Rq1Checklist.Options.Length; i++)
            {
                Rq1Workbook.Wrap(sheet, start + i, i == 1 ? 22 : 12);
                Rq1Workbook.Dropdown(sheet.Range(4, start + i, lastRow, start + i), Rq1Checklist.Options[i]);
            }
            Rq1Workbook.Wrap(sheet, start + 5, 12);
            Rq1Workbook.Wrap(sheet, start + 6, 30);
            Rq1Workbook.Wrap(sheet, start + 7, 9);
            Rq1Workbook.Wrap(sheet, start + 8, 18);
            Rq1Workbook.Dropdown(sheet.Range(4, start + 5, lastRow, start + 5), Rq1Checklist.Yes, Rq1Checklist.No);
            sheet.Range(4, start, lastRow, start + 6).Style.Protection.Locked = false;
            sheet.Range(4, start + 7, lastRow, start + 8).Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        for (var row = 4; row <= lastRow; row++)
            Rq1Workbook.FitTextRow(sheet, row, 2, 3, 4, 5, 6, 8, 9);
        sheet.Column(Rq1Workbook.RetrievalSuccess).Hide();
        sheet.SheetView.Freeze(3, 2);
        sheet.Protect();
    }

    private static void FormatRubric(IXLWorksheet sheet, string markdown)
    {
        Rq1Workbook.Wrap(sheet, 1, 120);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            sheet.Cell(i + 1, 1).Value = lines[i];
            sheet.Cell(i + 1, 1).Style.Font.Bold = lines[i].StartsWith('#');
            if (lines[i].Length > 0) Rq1Workbook.FitTextRow(sheet, i + 1, 1);
        }
        sheet.Protect();
    }
}
