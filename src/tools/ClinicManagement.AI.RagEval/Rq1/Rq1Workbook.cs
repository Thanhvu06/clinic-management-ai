using System.Text;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

internal static class Rq1Workbook
{
    internal const string Sheet = "Phiếu chấm";
    internal const string IdeasSheet = "Ý chính";
    internal const string SourcesSheet = "Truy xuất";
    internal const int Header = 3;
    internal const int AnswerA = 4;
    internal const int AnswerB = 5;
    internal const int Ideas = 6;
    internal const int Db = 7;
    internal const int SourcesA = 8;
    internal const int SourcesB = 9;
    internal const int ChecklistA = 10;
    internal const int ChecklistB = 19;
    internal const int RetrievalSuccess = 28;

    internal static string Text(IXLCell cell) => cell.GetString().Trim().Normalize(NormalizationForm.FormC);
    internal static bool ExpectsAnswer(string category, bool db) => !db && category is ("Trong phạm vi" or "Diễn đạt khác");

    internal static void Headings(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) sheet.Cell(row, i + 1).Value = headers[i];
        var band = sheet.Range(row, 1, row, headers.Count);
        band.Style.Font.Bold = true;
        band.Style.Font.FontColor = XLColor.White;
        band.Style.Fill.BackgroundColor = XLColor.DarkBlue;
        band.Style.Alignment.WrapText = true;
        sheet.Row(row).Height = 32;
        sheet.SheetView.FreezeRows(row);
    }

    internal static void Dropdown(IXLRange range, params string[] options)
    {
        var validation = range.CreateDataValidation();
        validation.List("\"" + string.Join(",", options) + "\"", inCellDropdown: true);
        validation.IgnoreBlanks = true;
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Stop;
    }

    internal static bool? YesNo(IXLCell cell) => Text(cell) switch
    {
        Rq1Checklist.Yes => true,
        Rq1Checklist.No => false,
        "" => null,
        _ => throw new InvalidDataException("A Có/Không field has an invalid value.")
    };

    internal static bool DbFlag(IXLCell cell) => Text(cell) switch
    {
        Rq1Checklist.Yes => true,
        "" => false,
        _ => throw new InvalidDataException("DB flags must be Có or blank.")
    };

    internal static void Wrap(IXLWorksheet sheet, int column, double width)
    {
        sheet.Column(column).Width = width;
        sheet.Column(column).Style.Alignment.WrapText = true;
    }

    internal static void FitTextRow(IXLWorksheet sheet, int row, params int[] columns)
    {
        var lines = columns.Max(column =>
        {
            var width = Math.Max(1, (int)(sheet.Column(column).Width * 0.9));
            return sheet.Cell(row, column).GetString().Split('\n')
                .Sum(line => Math.Max(1, (line.Length + width - 1) / width));
        });
        sheet.Row(row).Height = Math.Clamp(14d * (lines + 1), 36, 400);
    }
}
