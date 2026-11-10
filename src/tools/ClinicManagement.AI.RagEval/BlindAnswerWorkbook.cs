using System.Text;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed class BlindAnswerWorkbook : IDisposable
{
    public const string FullFile = "RAG_ket_qua_day_du.xlsx";
    public const string Grader1File = "RAG_phieu_cham_NguoiCham1.xlsx";
    public const string Grader2File = "RAG_phieu_cham_NguoiCham2.xlsx";
    private readonly XLWorkbook _workbook;
    private readonly Dictionary<string, int> _gradingRows;
    private readonly Dictionary<string, int> _keyRows;
    public IReadOnlyList<EvaluationQuestion> Questions { get; }

    public BlindAnswerWorkbook(string path)
    {
        _workbook = new XLWorkbook(path);
        try
        {
            foreach (var name in new[] { "Hướng dẫn", "Câu hỏi", "Phiếu chấm", "Khóa", "Tính", "Tổng hợp" })
                if (!_workbook.Worksheets.Contains(name)) throw new InvalidDataException("Workbook is missing a required worksheet.");
            Questions = ReadQuestions(_workbook.Worksheet("Câu hỏi"));
            _gradingRows = SttRows(_workbook.Worksheet("Phiếu chấm"));
            _keyRows = SttRows(_workbook.Worksheet("Khóa"));
            if (Questions.Any(q => string.IsNullOrWhiteSpace(q.Stt) || !_gradingRows.ContainsKey(q.Stt) || !_keyRows.ContainsKey(q.Stt)) ||
                Questions.Select(q => q.Stt).Distinct(StringComparer.Ordinal).Count() != Questions.Count)
                throw new InvalidDataException("Question STT values must be unique and present in both grading and key sheets.");
        }
        catch { _workbook.Dispose(); throw; }
    }

    public static IReadOnlyList<EvaluationQuestion> LoadQuestions(string path)
    {
        using var workbook = new BlindAnswerWorkbook(path);
        return workbook.Questions;
    }

    private static EvaluationQuestion[] ReadQuestions(IXLWorksheet sheet)
    {
        var csv = new StringBuilder("STT,Người viết,Câu hỏi,Loại,Tài liệu đúng,Ý chính cần có\n");
        var last = sheet.LastRowUsed()?.RowNumber() ?? 3;
        for (var row = 4; row <= last; row++)
            csv.AppendLine(string.Join(',', Enumerable.Range(1, 6).Select(column =>
                "\"" + sheet.Cell(row, column).GetString().Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")));
        return EvaluationInput.ParseCsvQuestions(csv.ToString(), "workbook");
    }

    public static Dictionary<string, int> SttRows(IXLWorksheet sheet)
    {
        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        var last = sheet.LastRowUsed()?.RowNumber() ?? 3;
        for (var row = 4; row <= last; row++)
        {
            var stt = sheet.Cell(row, 1).GetString().Trim().Normalize(NormalizationForm.FormC);
            if (stt.Length == 0 || stt.Equals("VD", StringComparison.OrdinalIgnoreCase)) continue;
            if (!rows.TryAdd(stt, row)) throw new InvalidDataException("Duplicate STT in worksheet.");
        }
        return rows;
    }

    public void Write(string directory, IReadOnlyList<BlindAnswerRow> answers)
    {
        foreach (var answer in answers)
        {
            var row = _gradingRows[answer.Stt];
            _workbook.Worksheet("Phiếu chấm").Cell(row, 4).Value = answer.AnswerA;
            _workbook.Worksheet("Phiếu chấm").Cell(row, 5).Value = answer.AnswerB;
            var keyRow = _keyRows[answer.Stt];
            _workbook.Worksheet("Khóa").Cell(keyRow, 2).Value = answer.AIs;
            _workbook.Worksheet("Khóa").Cell(keyRow, 3).Value = answer.LlmMs;
            _workbook.Worksheet("Khóa").Cell(keyRow, 4).Value = answer.ExtractiveMs;
        }
        SaveNew(_workbook, Path.Combine(directory, FullFile));
        foreach (var sheet in new[] { "Khóa", "Tính", "Tổng hợp" }) _workbook.Worksheets.Delete(sheet);
        SaveNew(_workbook, Path.Combine(directory, Grader1File));
        SaveNew(_workbook, Path.Combine(directory, Grader2File));
    }

    public static void SaveNew(XLWorkbook workbook, string path)
    {
        workbook.CalculateMode = XLCalculateMode.Auto;
        workbook.FullCalculationOnLoad = true;
        workbook.ForceFullCalculation = true;
        // ClosedXML retains its last SaveAs stream for subsequent saves of this workbook.
        var buffer = new MemoryStream();
        workbook.SaveAs(buffer, new SaveOptions { EvaluateFormulasBeforeSaving = false, GenerateCalculationChain = false });
        buffer.Position = 0;
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        buffer.CopyTo(stream);
    }

    public void Dispose() => _workbook.Dispose();
}
