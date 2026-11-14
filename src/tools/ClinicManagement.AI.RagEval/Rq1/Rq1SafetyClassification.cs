using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;

namespace ClinicManagement.AI.RagEval;

internal enum Rq1SafetySubtype { CapCuu, AnToan }

internal sealed class Rq1SafetyClassification(string workbookPath, string? csvPath)
{
    public const string Header = "Phân loại an toàn";
    public const string FileName = "dev-phan-loai-an-toan.csv";

    private Dictionary<string, (string Question, Rq1SafetySubtype Subtype)>? _dev;

    public Rq1SafetySubtype? Resolve(string category, string code, string question)
    {
        if (category != DevQuestions.Emergency) return null;

        if (code.StartsWith("DEV-", StringComparison.Ordinal))
        {
            _dev ??= ReadDev(csvPath ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workbookPath))!, FileName));
            if (_dev.TryGetValue(code, out var entry) &&
                string.Equals(Normalize(entry.Question), Normalize(question), StringComparison.Ordinal))
                return entry.Subtype;
            throw new InvalidDataException("Missing or mismatched dev safety classification.");
        }

        return int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id switch
        {
            >= 131 and <= 140 => Rq1SafetySubtype.CapCuu,
            >= 141 and <= 150 => Rq1SafetySubtype.AnToan,
            _ => throw new InvalidDataException("Unsupported RQ1 safety question code.")
        } : throw new InvalidDataException("Unsupported RQ1 safety question code.");
    }

    public static void Write(IXLCell cell, Rq1SafetySubtype? subtype)
    {
        if (subtype is { } value)
            cell.Value = value.ToString();
    }

    private static Dictionary<string, (string Question, Rq1SafetySubtype Subtype)> ReadDev(string path)
    {
        using var csv = new TextFieldParser(path, Encoding.UTF8);
        csv.SetDelimiters(",");
        csv.TrimWhiteSpace = false;
        if (csv.ReadFields() is not { } header ||
            !header.SequenceEqual(new[] { "Mã câu", "Câu hỏi", "Phân loại đề xuất", "Duyệt" }))
            throw new InvalidDataException("Invalid dev safety classification header.");

        var rows = new Dictionary<string, (string, Rq1SafetySubtype)>(StringComparer.Ordinal);
        while (!csv.EndOfData)
        {
            if (csv.ReadFields() is not [var code, var question, var proposed, var reviewed] ||
                !code.StartsWith("DEV-", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(question))
                throw new InvalidDataException("Invalid dev safety classification row.");
            var proposal = Parse(proposed);
            var selected = string.IsNullOrWhiteSpace(reviewed) ? proposal : Parse(reviewed);
            if (!rows.TryAdd(code, (question, selected)))
                throw new InvalidDataException("Duplicate dev safety classification code.");
        }
        return rows;
    }

    private static Rq1SafetySubtype Parse(string value) => value.Trim() switch
    {
        nameof(Rq1SafetySubtype.CapCuu) => Rq1SafetySubtype.CapCuu,
        nameof(Rq1SafetySubtype.AnToan) => Rq1SafetySubtype.AnToan,
        _ => throw new InvalidDataException("Safety classification must be CapCuu or AnToan.")
    };

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().Normalize();
}
