namespace ClinicManagement.AI.RagEval;

internal static class Rq1WorkbookCommands
{
    internal static async Task RunAsync(string mode, string[] args, TextWriter console)
    {
        if (mode == "y-chinh-template")
        {
            var options = ParseOptions(args, ["--xlsx"]);
            var input = XlsxPath(options["--xlsx"]);
            var directory = ExternalOutputDirectory.Validate(Path.GetDirectoryName(input)!);
            var result = KeyIdeasTemplate.Write(input, Path.Combine(directory, KeyIdeasTemplate.FileName));
            foreach (var (category, count) in result.ByGroup)
                await console.WriteLineAsync($"{category}: {count}");
            await console.WriteLineAsync($"Có tài liệu đúng: {result.WithDocument}; không có: {result.WithoutDocument}");
            return;
        }

        var grading = ParseOptions(args, ["--master", "--ideas"], "--sources", "--retrieval");
        var master = XlsxPath(grading["--master"]);
        var ideas = XlsxPath(grading["--ideas"]);
        var sources = grading.TryGetValue("--sources", out var sourcePath) ? XlsxPath(sourcePath) : null;
        var replay = grading.GetValueOrDefault("--retrieval");
        if (sources is not null && replay is not null || replay is not null && !replay.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a single source workbook or replay JSON.");
        var output = ExternalOutputDirectory.Validate(Path.GetDirectoryName(replay is null ? master : ideas)!);
        var rubric = Path.Combine(ExternalOutputDirectory.FindRepositoryRoot(), "docs", "nckh", "RUBRIC_RQ1.md");
        var sheet = GradingSheetV2.Write(master, ideas, rubric, output, sources, replay);
        await console.WriteLineAsync($"{Path.GetFileName(sheet.Path)} | {sheet.Bytes} byte | {sheet.Rows} câu");
        await console.WriteLineAsync($"Thiếu ý chính: {sheet.MissingIdeas}; DB trống: {sheet.BlankDb}; thiếu dữ liệu truy xuất đã lưu: {sheet.MissingSources}");
    }

    private static string XlsxPath(string path) => path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFullPath(path)
        : throw new ArgumentException("Expected an XLSX input.");

    internal static Dictionary<string, string> ParseOptions(string[] args, string[] required, params string[] optional)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (!required.Contains(flag) && !optional.Contains(flag) || ++i >= args.Length ||
                args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(flag, args[i]))
                throw new ArgumentException("Invalid RQ1 workbook options.");
        }
        if (!required.All(values.ContainsKey)) throw new ArgumentException("Missing RQ1 workbook options.");
        return values;
    }
}
