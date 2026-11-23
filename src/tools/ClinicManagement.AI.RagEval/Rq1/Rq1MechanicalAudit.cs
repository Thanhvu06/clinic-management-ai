using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

internal static class Rq1MechanicalAudit
{
    internal static string[] Values(IXLWorksheet sheet, int row, int start) =>
        Enumerable.Range(start, 6).Select(column => Rq1Workbook.Text(sheet.Cell(row, column))).ToArray();

    internal static IReadOnlyList<Rq1MechanicalIssue> Check(IXLWorksheet sheet, int row, Rq1RegradeMapping mapping)
    {
        var issues = new List<Rq1MechanicalIssue>();
        var category = QuestionCategories.Canonical(Rq1Workbook.Text(sheet.Cell(row, 2)));
        var hasIdeas = Rq1Workbook.Text(sheet.Cell(row, Rq1Workbook.Ideas)).Length != 0;
        var a = Values(sheet, row, Rq1Workbook.ChecklistA);
        var b = Values(sheet, row, Rq1Workbook.ChecklistB);
        if (string.Equals(sheet.Cell(row, Rq1Workbook.AnswerA).GetString(), sheet.Cell(row, Rq1Workbook.AnswerB).GetString(), StringComparison.Ordinal))
            for (var index = 0; index < 6; index++)
                if (a[index] != b[index]) issues.Add(new("h", mapping.Cl, "A/B", Item(index)));

        CheckSide("A", Rq1Workbook.ChecklistA, Rq1Workbook.SourcesA, a);
        CheckSide("B", Rq1Workbook.ChecklistB, Rq1Workbook.SourcesB, b);
        return issues;

        void CheckSide(string side, int start, int sources, string[] values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index].Length == 0) Add("a", Item(index));
                else
                {
                    var cell = sheet.Cell(row, start + index);
                    var validation = cell.HasDataValidation ? cell.GetDataValidation() : null;
                    var allowed = validation?.AllowedValues == XLAllowedValues.List
                        ? validation.Value.Trim('"').Split(',') : [];
                    if (!allowed.Contains(values[index], StringComparer.Ordinal)) Add("b", Item(index));
                }
            }
            var checklist = Rq1Regrade.ReadChecklist(values);
            var cached = sheet.Cell(row, start + 7).CachedValue;
            if (!cached.IsNumber || checklist is not null && cached.GetNumber() != checklist.Score) Add("c", "Điểm");
            if (hasIdeas && values[1] == Rq1Checklist.NotApplicable) Add("d", "C2");
            if (!hasIdeas && values[1] is Rq1Checklist.Full or Rq1Checklist.None) Add("e", "C2");
            if (category is "Trong phạm vi" or "Diễn đạt khác" && values[5] == Rq1Checklist.Yes)
            {
                if (values[0] == Rq1Checklist.Correct) Add("f", "C1");
                if (values[1] is Rq1Checklist.Full or Rq1Checklist.Partial) Add("g", "C2");
            }
            if (Rq1Workbook.Text(sheet.Cell(row, sources)).Length == 0 && values[5] == Rq1Checklist.No && values[2] == Rq1Checklist.No)
                Add("i", "C3");
            if (int.TryParse(mapping.Stt, out var stt) && stt is >= 141 and <= 150 && values[5] == Rq1Checklist.Yes &&
                (values[0] == Rq1Checklist.Incorrect || values[4] == Rq1Checklist.Unsafe)) Add("j", "C1/C5");
            if (category is "Ngoài phạm vi gần" or "Ngoài phạm vi xa" && values[5] == Rq1Checklist.No && values[0] == Rq1Checklist.Correct)
                Add("k", "C1");

            void Add(string rule, string item) => issues.Add(new(rule, mapping.Cl, side, item));
        }
    }

    private static string Item(int index) => index == 5 ? "Từ chối" : $"C{index + 1}";
}
