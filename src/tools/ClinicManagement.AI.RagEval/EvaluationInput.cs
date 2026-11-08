using System.Text.Json;

namespace ClinicManagement.AI.RagEval;

public sealed record EvaluationQuestion(
    [property: System.Text.Json.Serialization.JsonPropertyName("question")] string Text, string? ExpectedDocId);

public sealed record EvaluationInput(string? OutputPath, IReadOnlyList<string> QuestionFiles)
{
    public static EvaluationInput Parse(string[] args)
    {
        string? output = null;
        var files = new List<string>();
        var directory = Path.Combine(AppContext.BaseDirectory, "AI", "Knowledge", "Eval");
        var baseline = Path.Combine(directory, "rag-dev-questions.json");
        var v2 = Path.Combine(directory, "rag-dev-questions-v2.json");
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--questions")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--questions requires baseline, v2, combined or a JSON file path.");
                files.AddRange(args[i] switch
                {
                    "baseline" => [baseline],
                    "v2" => [v2],
                    "combined" => [baseline, v2],
                    _ => [Path.GetFullPath(args[i])]
                });
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal) || output is not null)
                throw new ArgumentException("Unknown argument: " + args[i]);
            else output = args[i];
        }
        if (files.Count == 0) files.Add(baseline);
        if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new ArgumentException("A question file may only be selected once.");
        if (output is not null && files.Any(file =>
            string.Equals(Path.GetFileName(file), Path.GetFileName(output), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Output must use a different filename from the question files, including their source copies.");
        return new(output, files);
    }

    public async Task<EvaluationQuestion[]> LoadAsync()
    {
        var questions = new List<EvaluationQuestion>();
        foreach (var file in QuestionFiles)
        {
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
}
