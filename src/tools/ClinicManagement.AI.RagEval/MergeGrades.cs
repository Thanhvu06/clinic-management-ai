using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record MergeGradesOptions(string Master, string Grader2, string Output, string? Grader1 = null)
{
    public static MergeGradesOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (flag is not ("--master" or "--grader1" or "--grader2" or "--out") || ++i >= args.Length ||
                args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(flag, Path.GetFullPath(args[i])))
                throw new ArgumentException("Invalid merge-grades options.");
        }
        if (!values.ContainsKey("--master") || !values.ContainsKey("--grader2") || !values.ContainsKey("--out") ||
            values.Values.Any(path => !path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("merge-grades requires --master, --grader2 and --out XLSX paths.");
        return new(values["--master"], values["--grader2"], values["--out"], values.GetValueOrDefault("--grader1"));
    }
}

public sealed record MergeGradesResult(int CopiedRows, int UnmatchedStt);

public static class MergeGrades
{
    public static async Task<MergeGradesResult> RunAsync(MergeGradesOptions options, TextWriter console)
    {
        var output = Path.GetFullPath(options.Output);
        if (File.Exists(output) || Directory.Exists(output) ||
            new[] { options.Master, options.Grader1, options.Grader2 }.Where(path => path is not null)
                .Any(path => string.Equals(Path.GetFullPath(path!), output, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("--out must be a new file, different from the input files.");
        using var master = new XLWorkbook(options.Master);
        var destination = master.Worksheet("Phiếu chấm");
        var rows = BlindAnswerWorkbook.SttRows(destination);
        var copied = 0;
        var unmatched = 0;
        void Copy(string path, int startColumn)
        {
            using var grader = new XLWorkbook(path);
            var source = grader.Worksheet("Phiếu chấm");
            foreach (var (stt, sourceRow) in BlindAnswerWorkbook.SttRows(source))
            {
                if (Enumerable.Range(startColumn, 5).All(column => source.Cell(sourceRow, column).IsEmpty())) continue;
                if (!rows.TryGetValue(stt, out var targetRow)) { unmatched++; continue; }
                foreach (var column in Enumerable.Range(startColumn, 5))
                    destination.Cell(targetRow, column).Value = source.Cell(sourceRow, column).Value;
                copied++;
            }
        }
        if (options.Grader1 is not null) Copy(options.Grader1, 6);
        Copy(options.Grader2, 11);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        BlindAnswerWorkbook.SaveNew(master, output);
        await console.WriteLineAsync($"Copied rows: {copied}; unmatched STT: {unmatched}");
        await console.WriteLineAsync(output);
        return new(copied, unmatched);
    }
}
