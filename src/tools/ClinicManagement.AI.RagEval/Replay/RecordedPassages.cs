using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

internal static class RecordedPassages
{
    private const int Start = 5;
    private static readonly string[] Headers = ["Mã đoạn A", "Đoạn truy xuất A", "Mã đoạn B", "Đoạn truy xuất B", "Truy xuất thành công"];

    internal static void WriteHeaders(IXLWorksheet key)
    {
        var range = key.Range(3, Start, Math.Max(3, key.LastRowUsed()?.RowNumber() ?? 3), Start + Headers.Length - 1);
        if (!range.IsEmpty()) throw new InvalidDataException("The master key's retrieval columns are occupied.");
        for (var i = 0; i < Headers.Length; i++)
        {
            key.Cell(3, Start + i).Value = Headers[i];
            Rq1Workbook.Wrap(key, Start + i, i is 1 or 3 ? 70 : 24);
        }
    }

    internal static void WriteRow(IXLWorksheet key, int row, BlindAnswerRow answer)
    {
        if (answer.PassagesA is null || answer.PassagesB is null) return;
        WriteSide(Start, answer.PassagesA);
        WriteSide(Start + 2, answer.PassagesB);
        key.Cell(row, Start + 4).Value = answer.PassagesA.Length + answer.PassagesB.Length > 0 ? Rq1Checklist.Yes : Rq1Checklist.No;

        void WriteSide(int column, ReplayPassage[] passages)
        {
            key.Cell(row, column).Value = string.Join("\n", passages.Select(passage => passage.Id));
            key.Cell(row, column + 1).Value = ReplayPassage.Format(passages);
        }
    }

    internal static Dictionary<string, (string A, string B, bool? Retrieved)> Read(XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet("Khóa", out var key) ||
            !Headers.Select((header, index) => Rq1Workbook.Text(key.Cell(3, Start + index)) == header).All(value => value))
            return [];
        var sources = new Dictionary<string, (string, string, bool?)>(StringComparer.Ordinal);
        foreach (var (code, row) in BlindAnswerWorkbook.SttRows(key))
        {
            if (Rq1Workbook.YesNo(key.Cell(row, Start + 4)) is { } retrieved)
                sources.Add(code, (key.Cell(row, Start + 1).GetString(), key.Cell(row, Start + 3).GetString(), retrieved));
        }
        return sources;
    }
}
