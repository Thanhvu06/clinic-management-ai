using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.AI.RagEval;

public sealed record StatisticsInput(EvaluationInput? Evaluation, string? Xlsx, string Output)
{
    public string Primary { get; init; } = "mean";
    public static StatisticsInput Parse(string[] args, string command)
    {
        string? xlsx = null, output = null, primary = null;
        var selectors = new List<string>();
        var hasCsv = false;
        var hasQuestions = false;
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Missing statistics option value.");
            switch (flag)
            {
                case "--primary" when command == "rq1-stats" && primary is null && args[i] is "grader1" or "grader2" or "mean":
                    primary = args[i]; break;
                case "--xlsx" when xlsx is null: xlsx = Path.GetFullPath(args[i]); break;
                case "--output" when output is null: output = Path.GetFullPath(args[i]); break;
                case "--csv" when !hasCsv:
                    hasCsv = true; selectors.AddRange([flag, args[i]]); break;
                case "--questions":
                    hasQuestions = true; selectors.AddRange([flag, args[i]]); break;
                default: throw new ArgumentException("Unsupported or duplicate statistics option.");
            }
        }
        if (xlsx is not null && (hasCsv || hasQuestions) || hasCsv && hasQuestions ||
            xlsx is not null && !xlsx.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            command == "rq1-stats" && (xlsx is null || selectors.Count != 0))
            throw new ArgumentException("Use one question source; rq1-stats requires --xlsx.");
        output ??= Path.GetFullPath(command + ".json");
        if (!output.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Statistics output must be a JSON file.");
        return new(xlsx is null ? EvaluationInput.Parse(selectors.ToArray()) : null, xlsx, output) { Primary = primary ?? "mean" };
    }

    public async Task<IReadOnlyList<EvaluationQuestion>> LoadQuestionsAsync() => Xlsx is null
        ? await Evaluation!.LoadAsync(requireBothScopes: false)
        : BlindAnswerWorkbook.LoadQuestions(Xlsx);
}

public static class StatisticsReport
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void EnsureNew(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Statistics output already exists; use a new --output.");
    }

    public static async Task SaveAsync(object report, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, ct);
    }

    public static string Number(double? value) => value?.ToString("G10", CultureInfo.InvariantCulture) ?? "NA";
}

public static class QuestionCategories
{
    public static readonly string[] All = ["Trong phạm vi", "Diễn đạt khác", "Ngoài phạm vi gần", "Ngoài phạm vi xa", "Khẩn cấp / an toàn", "Ngoài phạm vi"];

    public static string Canonical(string? category, bool? hasDocument = null)
    {
        if (category is null && hasDocument.HasValue) return hasDocument.Value ? All[0] : All[5];
        var normalized = string.Join(' ', (category ?? "").Normalize(NormalizationForm.FormC).Replace("/", " / ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return All.FirstOrDefault(value => value.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Unsupported or missing question category.");
    }
}
