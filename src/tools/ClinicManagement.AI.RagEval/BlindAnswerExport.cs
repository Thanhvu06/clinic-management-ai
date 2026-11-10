using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.AI.DTOs;

namespace ClinicManagement.AI.RagEval;

public sealed record BlindAnswerOptions(string CsvPath, string OutputDirectory, int Seed, string Retrieval = "hybrid")
{
    public static BlindAnswerOptions Parse(string[] args)
    {
        string? csv = null, output = null;
        string? retrieval = null;
        int? seed = null;
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (++i >= args.Length) throw new ArgumentException("Missing option value.");
            switch (flag)
            {
                case "--csv" when csv is null: csv = args[i]; break;
                case "--out-dir" when output is null: output = args[i]; break;
                case "--seed" when seed is null:
                    seed = int.Parse(args[i], CultureInfo.InvariantCulture); break;
                case "--retrieval" when retrieval is null:
                    retrieval = args[i]; break;
                default: throw new ArgumentException("Unsupported or duplicate option.");
            }
        }
        if (csv is null || output is null || !csv.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("blind-answers requires --csv and --out-dir.");
        if (retrieval is not (null or "hybrid" or "bm25"))
            throw new ArgumentException("--retrieval must be hybrid or bm25.");
        return new(Path.GetFullPath(csv), ExternalOutputDirectory.Validate(output),
            seed ?? RandomNumberGenerator.GetInt32(int.MaxValue), retrieval ?? "hybrid");
    }
}

public static class ExternalOutputDirectory
{
    public static string Validate(string path)
    {
        var repo = FindRepositoryRoot();
        var output = ResolveDirectory(path);
        if (IsInside(output, ResolveDirectory(repo)))
            throw new ArgumentException("--out-dir must be outside the repository.");
        return output;
    }

    public static string FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                    return directory.FullName;
        throw new InvalidOperationException("Cannot locate repository to validate --out-dir.");
    }

    private static bool IsInside(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string ResolveDirectory(string path)
    {
        var absolute = Path.GetFullPath(path);
        var current = Path.GetPathRoot(absolute)!;
        foreach (var part in absolute[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var directory = new DirectoryInfo(current);
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                current = directory.ResolveLinkTarget(true)?.FullName
                    ?? throw new ArgumentException("Cannot resolve output directory link.");
        }
        return Path.TrimEndingDirectorySeparator(current);
    }
}

public sealed record BlindExportResult(int Count, int Seed, string GradingPath, string KeyPath);

public static class BlindAnswerExport
{
    public static async Task<BlindExportResult> RunAsync(BlindAnswerOptions options,
        Func<string, bool, CancellationToken, Task<AiChatResponseDto>> answer,
        TextWriter console, CancellationToken ct = default)
    {
        var directory = ExternalOutputDirectory.Validate(options.OutputDirectory);
        var gradingPath = Path.Combine(directory, "phieu-cham.csv");
        var keyPath = Path.Combine(directory, "khoa.csv");
        if (File.Exists(gradingPath) || File.Exists(keyPath))
            throw new IOException("Output files already exist; choose a new --out-dir.");
        var input = EvaluationInput.Parse(["--csv", options.CsvPath]);
        var questions = await input.LoadAsync(requireBothScopes: false);
        if (questions.Any(q => string.IsNullOrWhiteSpace(q.Stt)) ||
            questions.Select(q => q.Stt).Distinct(StringComparer.Ordinal).Count() != questions.Length)
            throw new InvalidDataException("Blind export requires unique, nonempty STT values.");
        var grading = new StringBuilder("STT,Trả lời A,Trả lời B\r\n");
        var key = new StringBuilder("STT,A là,Độ trễ LLM (ms),Độ trễ trích đoạn (ms)\r\n");
        var random = new Random(options.Seed);
        var count = 0;
        foreach (var question in questions)
        {
            ct.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            var llm = await answer(question.Text, true, ct);
            var llmMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var extractive = await answer(question.Text, false, ct);
            var extractiveMs = watch.Elapsed.TotalMilliseconds;
            var llmIsA = random.Next(2) == 0;
            grading.AppendLine(string.Join(',', Cell(question.Stt!),
                Cell(VisibleText(llmIsA ? llm : extractive)), Cell(VisibleText(llmIsA ? extractive : llm))));
            key.AppendLine(string.Join(',', Cell(question.Stt!), Cell(llmIsA ? "LLM" : "Trích đoạn"),
                llmMs.ToString("F3", CultureInfo.InvariantCulture), extractiveMs.ToString("F3", CultureInfo.InvariantCulture)));
            await console.WriteLineAsync($"{++count}/{questions.Length}");
        }
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(gradingPath, grading.ToString(), new UTF8Encoding(true), ct);
        await File.WriteAllTextAsync(keyPath, key.ToString(), new UTF8Encoding(true), ct);
        await console.WriteLineAsync($"Count: {count}; seed: {options.Seed}; retrieval: {options.Retrieval}");
        await console.WriteLineAsync(gradingPath);
        await console.WriteLineAsync(keyPath);
        return new(count, options.Seed, gradingPath, keyPath);
    }

    private static string Cell(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string VisibleText(AiChatResponseDto response) =>
        string.IsNullOrEmpty(response.Message) ? response.Reply ?? string.Empty : response.Message;
}
