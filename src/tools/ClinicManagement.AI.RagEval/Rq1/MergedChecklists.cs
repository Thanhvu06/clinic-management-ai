using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

internal sealed record ChecklistPair(string Category, bool Db, bool? Retrieved, Rq1Checklist? A, Rq1Checklist? B, bool? RefusedA, bool? RefusedB);
internal sealed record ChecklistSource(string Name, IReadOnlyDictionary<string, ChecklistPair> Rows);

internal static class MergedChecklists
{
    internal static bool IsV2(IXLWorksheet sheet) => Rq1Workbook.Text(sheet.Cell(1, 1)) == GradingSheetV2.Marker;

    private static string SheetName(int start) => start == 6 ? "Checklist NguoiCham1" : "Checklist NguoiCham2";

    internal static (int Copied, int Unmatched) Copy(IXLWorksheet source, XLWorkbook master,
        IReadOnlyDictionary<string, int> rows, int start)
    {
        var name = SheetName(start);
        if (master.Worksheets.Contains(name)) master.Worksheets.Delete(name);
        var target = master.AddWorksheet(name);
        target.Cell(1, 1).Value = GradingSheetV2.Marker;
        var legacy = master.Worksheet(Rq1Workbook.Sheet);
        var questions = master.Worksheet("Câu hỏi");
        var questionRows = BlindAnswerWorkbook.SttRows(questions);
        var sourceRows = BlindAnswerWorkbook.SttRows(source);
        var copied = 0;
        foreach (var (code, targetRow) in rows)
        {
            legacy.Range(targetRow, start, targetRow, start + 4).Clear(XLClearOptions.Contents);
            if (!sourceRows.TryGetValue(code, out var row)) continue;
            var pair = Read(source, row);
            if (!questionRows.TryGetValue(code, out var questionRow) ||
                pair.Category != QuestionCategories.Canonical(questions.Cell(questionRow, 4).GetString()) ||
                !SameText(source, row, 3, questions, questionRow, 3) ||
                !SameText(source, row, Rq1Workbook.AnswerA, legacy, targetRow, Rq1Workbook.AnswerA) ||
                !SameText(source, row, Rq1Workbook.AnswerB, legacy, targetRow, Rq1Workbook.AnswerB))
                throw new InvalidDataException("A v2 grading row does not match the master's question or A/B answers.");

            foreach (var column in new[] { 1, 2, Rq1Workbook.Db, Rq1Workbook.RetrievalSuccess })
                target.Cell(targetRow, column).Value = source.Cell(row, column).Value;
            CopySide(Rq1Workbook.ChecklistA, pair.A, start);
            CopySide(Rq1Workbook.ChecklistB, pair.B, start + 1);
            var notesA = Rq1Workbook.Text(source.Cell(row, Rq1Workbook.ChecklistA + 6));
            var notesB = Rq1Workbook.Text(source.Cell(row, Rq1Workbook.ChecklistB + 6));
            if (notesA.Length > 0 || notesB.Length > 0)
                legacy.Cell(targetRow, start + 4).Value = $"A: {notesA}\nB: {notesB}";
            if (pair.A is not null || pair.B is not null || notesA.Length > 0 || notesB.Length > 0) copied++;

            void CopySide(int column, Rq1Checklist? checklist, int legacyColumn)
            {
                for (var offset = 0; offset < 7; offset++)
                    target.Cell(targetRow, column + offset).Value = source.Cell(row, column + offset).Value;
                if (checklist is null) return;
                var expects = Rq1Workbook.ExpectsAnswer(pair.Category, pair.Db);
                Rq1Checklist.WriteFormulas(target, targetRow, column, expects);
                legacy.Cell(targetRow, legacyColumn).Value = checklist.Score;
                legacy.Cell(targetRow, legacyColumn + 2).Value = checklist.C5;
            }
        }
        return (copied, sourceRows.Keys.Count(code => !rows.ContainsKey(code)));
    }

    private static bool SameText(IXLWorksheet left, int leftRow, int leftCol, IXLWorksheet right, int rightRow, int rightCol) =>
        Rq1Workbook.Text(left.Cell(leftRow, leftCol)) == Rq1Workbook.Text(right.Cell(rightRow, rightCol));

    internal static void Remove(XLWorkbook workbook, int start)
    {
        var name = SheetName(start);
        if (workbook.Worksheets.Contains(name)) workbook.Worksheets.Delete(name);
    }

    internal static ChecklistSource? Read(XLWorkbook workbook, int start)
    {
        if (!workbook.Worksheets.TryGetWorksheet(SheetName(start), out var sheet)) return null;
        return new(start == 6 ? "grader1" : "grader2",
            BlindAnswerWorkbook.SttRows(sheet).ToDictionary(entry => entry.Key, entry => Read(sheet, entry.Value), StringComparer.Ordinal));
    }

    private static ChecklistPair Read(IXLWorksheet sheet, int row) => new(
        QuestionCategories.Canonical(Rq1Workbook.Text(sheet.Cell(row, 2))),
        Rq1Workbook.DbFlag(sheet.Cell(row, Rq1Workbook.Db)),
        Rq1Workbook.YesNo(sheet.Cell(row, Rq1Workbook.RetrievalSuccess)),
        Rq1Checklist.Read(sheet, row, Rq1Workbook.ChecklistA), Rq1Checklist.Read(sheet, row, Rq1Workbook.ChecklistB),
        Rq1Workbook.YesNo(sheet.Cell(row, Rq1Workbook.ChecklistA + 5)), Rq1Workbook.YesNo(sheet.Cell(row, Rq1Workbook.ChecklistB + 5)));
}
