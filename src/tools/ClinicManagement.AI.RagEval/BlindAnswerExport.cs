using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.AI.DTOs;

namespace ClinicManagement.AI.RagEval;

public sealed record BlindAnswerOptions(string CsvPath, string OutputDirectory, int Seed, string Retrieval = "hybrid",
    string? XlsxPath = null, bool DevProvenance = false)
{
    public static BlindAnswerOptions Parse(string[] args)
    {
        string? csv = null, xlsx = null, output = null;
        string? retrieval = null;
        int? seed = null;
        var provenance = false;
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (flag == "--dev-provenance" && !provenance)
            {
                provenance = true;
                continue;
            }
            if (++i >= args.Length) throw new ArgumentException("Missing option value.");
            switch (flag)
            {
                case "--csv" when csv is null: csv = args[i]; break;
                case "--xlsx" when xlsx is null: xlsx = args[i]; break;
                case "--out-dir" when output is null: output = args[i]; break;
                case "--seed" when seed is null:
                    seed = int.Parse(args[i], CultureInfo.InvariantCulture); break;
                case "--retrieval" when retrieval is null:
                    retrieval = args[i]; break;
                default: throw new ArgumentException("Unsupported or duplicate option.");
            }
        }
        if ((csv is null) == (xlsx is null) || output is null ||
            csv is not null && !csv.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
            xlsx is not null && !xlsx.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("blind-answers requires exactly one of --csv/--xlsx and --out-dir.");
        if (retrieval is not (null or "hybrid" or "bm25"))
            throw new ArgumentException("--retrieval must be hybrid or bm25.");
        if (provenance && csv is null)
            throw new ArgumentException("--dev-provenance requires a dev CSV.");
        return new(csv is null ? string.Empty : Path.GetFullPath(csv), ExternalOutputDirectory.Validate(output),
            seed ?? RandomNumberGenerator.GetInt32(int.MaxValue), retrieval ?? "hybrid",
            xlsx is null ? null : Path.GetFullPath(xlsx), provenance);
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
public sealed record BlindAnswerRow(string Stt, string AnswerA, string AnswerB, string AIs, double LlmMs, double ExtractiveMs);

public static class BlindAnswerExport
{
    public static async Task<BlindExportResult> RunAsync(BlindAnswerOptions options,
        Func<string, bool, CancellationToken, Task<AiChatResponseDto>> answer,
        TextWriter console, CancellationToken ct = default, Action<BlindAnswerRow>? onAnswer = null,
        IReadOnlyList<EvaluationQuestion>? loadedQuestions = null,
        Action<BlindAnswerRow, AiChatResponseDto, AiChatResponseDto>? onResponses = null)
    {
        var directory = ExternalOutputDirectory.Validate(options.OutputDirectory);
        var gradingPath = Path.Combine(directory, "phieu-cham.csv");
        var keyPath = Path.Combine(directory, "khoa.csv");
        if (File.Exists(gradingPath) || File.Exists(keyPath))
            throw new IOException("Output files already exist; choose a new --out-dir.");
        var questions = loadedQuestions ?? (options.XlsxPath is null
            ? await EvaluationInput.Parse(["--csv", options.CsvPath]).LoadAsync(requireBothScopes: false)
            : BlindAnswerWorkbook.LoadQuestions(options.XlsxPath));
        if (questions.Any(q => string.IsNullOrWhiteSpace(q.Stt)) ||
            questions.Select(q => q.Stt).Distinct(StringComparer.Ordinal).Count() != questions.Count)
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
            var row = new BlindAnswerRow(question.Stt!, VisibleText(llmIsA ? llm : extractive),
                VisibleText(llmIsA ? extractive : llm), llmIsA ? "LLM" : "Trích đoạn", llmMs, extractiveMs);
            grading.AppendLine(string.Join(',', Cell(row.Stt), Cell(row.AnswerA), Cell(row.AnswerB)));
            key.AppendLine(string.Join(',', Cell(row.Stt), Cell(row.AIs),
                llmMs.ToString("F3", CultureInfo.InvariantCulture), extractiveMs.ToString("F3", CultureInfo.InvariantCulture)));
            onAnswer?.Invoke(row);
            onResponses?.Invoke(row, llm, extractive);
            await console.WriteLineAsync($"{++count}/{questions.Count}");
        }
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(gradingPath, grading.ToString(), new UTF8Encoding(true), ct);
        await File.WriteAllTextAsync(keyPath, key.ToString(), new UTF8Encoding(true), ct);
        await console.WriteLineAsync($"Count: {count}; seed: {options.Seed}; retrieval: {options.Retrieval}");
        await console.WriteLineAsync(gradingPath);
        await console.WriteLineAsync(keyPath);
        return new(count, options.Seed, gradingPath, keyPath);
    }

    internal static string Cell(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string VisibleText(AiChatResponseDto response) =>
        string.IsNullOrEmpty(response.Message) ? response.Reply ?? string.Empty : response.Message;
}
