using System.Globalization;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public static class Disagreements
{
    public static async Task RunAsync(string[] args, TextWriter console)
    {
        string? path = null; int? threshold = null;
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (++i >= args.Length) throw new ArgumentException("Missing disagreement option.");
            if (flag == "--xlsx" && path is null) path = args[i];
            else if (flag == "--threshold" && threshold is null) threshold = int.Parse(args[i], CultureInfo.InvariantCulture);
            else throw new ArgumentException("Invalid disagreement options.");
        }
        if (path is null || threshold is < 1) throw new ArgumentException("Disagreements requires a workbook and positive threshold.");
        threshold ??= 2;
        using var book = new XLWorkbook(path);
        var sheet = book.Worksheet("Phiếu chấm"); var questions = book.Worksheet("Câu hỏi");
        var categories = BlindAnswerWorkbook.SttRows(questions);
        await console.WriteLineAsync("STT | Loại | A/B | (NC1, NC2)");
        foreach (var (stt, row) in BlindAnswerWorkbook.SttRows(sheet))
            for (var side = 0; side < 2; side++)
            {
                var left = sheet.Cell(row, 6 + side); var right = sheet.Cell(row, 11 + side);
                if (left.IsEmpty() || right.IsEmpty()) continue;
                var a = GradeCells.Score(left); var b = GradeCells.Score(right);
                if (Math.Abs(a - b) < threshold) continue;
                if (!categories.TryGetValue(stt, out var questionRow)) throw new InvalidDataException("Missing question STT.");
                var category = QuestionCategories.Canonical(questions.Cell(questionRow, 4).GetString());
                await console.WriteLineAsync($"{stt} | {category} | {(side == 0 ? "A" : "B")} | ({a}, {b})");
            }
    }
}

public static class GradeCells
{
    public static int Score(IXLCell cell)
    {
        if (!cell.TryGetValue<double>(out var value) || !double.IsFinite(value) || value is < 0 or > 2 || value != Math.Truncate(value))
            throw new InvalidDataException("Scores must be integers 0/1/2.");
        return (int)value;
    }
}
