using System.Text;
using System.Text.Json;

namespace ClinicManagement.AI.RagEval;

public sealed record EvaluationQuestion(
    [property: System.Text.Json.Serialization.JsonPropertyName("question")] string Text, string? ExpectedDocId);

public sealed record EvaluationInput(string? OutputPath, IReadOnlyList<string> QuestionFiles, bool IsCsv = false)
{
    public static EvaluationInput Parse(string[] args)
    {
        string? output = null;
        var files = new List<string>();
        var isCsv = false;
        var hasQuestionsFlag = false;
        var directory = Path.Combine(AppContext.BaseDirectory, "AI", "Knowledge", "Eval");
        var baseline = Path.Combine(directory, "rag-dev-questions.json");
        var v2 = Path.Combine(directory, "rag-dev-questions-v2.json");
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--questions")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--questions requires baseline, v2, combined or a JSON file path.");
                hasQuestionsFlag = true;
                files.AddRange(args[i] switch
                {
                    "baseline" => [baseline],
                    "v2" => [v2],
                    "combined" => [baseline, v2],
                    _ => [Path.GetFullPath(args[i])]
                });
            }
            else if (args[i] == "--csv")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--csv requires a CSV file path.");
                isCsv = true;
                files.Add(Path.GetFullPath(args[i]));
            }
            else if (args[i] == "--output")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || output is not null)
                    throw new ArgumentException("--output requires a single output JSON path.");
                output = args[i];
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal) || output is not null)
                throw new ArgumentException("Unknown argument: " + args[i]);
            else output = args[i];
        }
        if (isCsv && hasQuestionsFlag)
            throw new ArgumentException("Cannot combine --csv with --questions.");
        if (files.Count == 0) files.Add(baseline);
        if (files.Any(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
            isCsv = true;
        if (isCsv && files.Any(f => !f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("CSV evaluation mode only accepts .csv files.");
        if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new ArgumentException("A question file may only be selected once.");
        if (output is not null && files.Any(file =>
            string.Equals(Path.GetFileName(file), Path.GetFileName(output), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Output must use a different filename from the question files, including their source copies.");
        return new(output, files, isCsv);
    }

    public async Task<EvaluationQuestion[]> LoadAsync()
    {
        var questions = new List<EvaluationQuestion>();
        foreach (var file in QuestionFiles)
        {
            if (IsCsv || file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var content = await File.ReadAllTextAsync(file, Encoding.UTF8);
                questions.AddRange(ParseCsvQuestions(content, file));
                continue;
            }
            var rows = JsonSerializer.Deserialize<EvaluationQuestion[]>(await File.ReadAllTextAsync(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (rows is null || rows.Length == 0 || rows.Any(q => q is null || string.IsNullOrWhiteSpace(q.Text)
                || q.ExpectedDocId is not null && string.IsNullOrWhiteSpace(q.ExpectedDocId)))
                throw new InvalidDataException("Invalid question set: " + file);
            questions.AddRange(rows);
        }
        if (!questions.Any(q => q.ExpectedDocId is null) || !questions.Any(q => q.ExpectedDocId is not null))
            throw new InvalidDataException("Evaluation requires both in-scope and out-of-scope questions.");
        return questions.ToArray();
    }

    public static EvaluationQuestion[] ParseCsvQuestions(string csvContent, string sourceName = "csv")
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            throw new InvalidDataException("Invalid CSV question set: " + sourceName);

        var text = csvContent[0] == '\uFEFF' ? csvContent[1..] : csvContent;
        var rows = ParseCsvRows(text);
        var headerIndex = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].All(string.IsNullOrWhiteSpace)) continue;
            headerIndex = i;
            break;
        }
        if (headerIndex < 0)
            throw new InvalidDataException("CSV question set has no header row: " + sourceName);

        var header = rows[headerIndex].Select(c => c.Trim()).ToArray();
        var sttCol = FindColumnIndex(header, "STT");
        var authorCol = FindColumnIndex(header, "Người viết");
        var questionCol = FindColumnIndex(header, "Câu hỏi");
        var typeCol = FindColumnIndex(header, "Loại");
        var docCol = FindColumnIndex(header, "Tài liệu đúng");
        if (sttCol < 0 || authorCol < 0 || questionCol < 0 || typeCol < 0 || docCol < 0)
            throw new InvalidDataException("CSV header must contain STT, Người viết, Câu hỏi, Loại, Tài liệu đúng: " + sourceName);

        var questions = new List<EvaluationQuestion>();
        for (var r = headerIndex + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            var stt = GetCell(row, sttCol);
            var questionText = GetCell(row, questionCol);
            var category = GetCell(row, typeCol);
            var rawDoc = GetCell(row, docCol);

            if (string.IsNullOrWhiteSpace(questionText) && string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(rawDoc))
                continue;
            if (string.Equals(stt, "VD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stt, "VÍ DỤ", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(questionText))
                throw new InvalidDataException($"Row {r + 1} in {sourceName} is missing question text.");

            var normalizedCategory = NormalizeCategory(category);
            var normalizedDoc = NormalizeDocId(rawDoc);
            var expectedDocId = normalizedCategory switch
            {
                "trong phạm vi" => !string.IsNullOrWhiteSpace(normalizedDoc)
                    ? normalizedDoc
                    : throw new InvalidDataException($"Row {r + 1} in {sourceName} is in-scope but missing expected document."),
                "ngoài phạm vi" => null,
                "khẩn cấp / an toàn" => string.IsNullOrWhiteSpace(normalizedDoc) ? null : normalizedDoc,
                _ => throw new InvalidDataException($"Row {r + 1} in {sourceName} has unsupported category: {category}")
            };

            questions.Add(new EvaluationQuestion(questionText, expectedDocId));
        }

        if (questions.Count == 0)
            throw new InvalidDataException("CSV question set has no valid rows: " + sourceName);
        return questions.ToArray();
    }

    private static int FindColumnIndex(string[] header, string expectedName)
    {
        var expectedNormalized = expectedName.Normalize(NormalizationForm.FormC);
        for (var i = 0; i < header.Length; i++)
        {
            var candidate = header[i].Trim('\uFEFF', ' ', '\t').Normalize(NormalizationForm.FormC);
            if (string.Equals(candidate, expectedNormalized, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index].Trim().Normalize(NormalizationForm.FormC) : string.Empty;

    private static string NormalizeCategory(string category)
    {
        var collapsed = string.Join(' ', category.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        collapsed = collapsed.Replace("/", " / ", StringComparison.Ordinal);
        collapsed = string.Join(' ', collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.ToLowerInvariant();
    }

    private static string? NormalizeDocId(string rawDoc)
    {
        var trimmed = rawDoc.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        if (trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static List<List<string>> ParseCsvRows(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        currentCell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentCell.Append(ch);
                }
            }
            else
            {
                if (ch == '"' && currentCell.Length == 0)
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                }
                else if (ch is '\r' or '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    rows.Add(currentRow);
                    currentRow = [];
                }
                else
                {
                    currentCell.Append(ch);
                }
            }
        }

        if (inQuotes)
            throw new InvalidDataException("Unterminated quoted cell in CSV input.");

        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}
