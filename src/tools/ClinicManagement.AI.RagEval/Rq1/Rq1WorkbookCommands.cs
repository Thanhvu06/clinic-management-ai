namespace ClinicManagement.AI.RagEval;

internal static class Rq1WorkbookCommands
{
    internal static async Task RunAsync(string mode, string[] args, TextWriter console)
    {
        if (mode == "y-chinh-template")
        {
            var options = Parse(args, ["--xlsx"]);
            var input = Path.GetFullPath(options["--xlsx"]);
            var directory = ExternalOutputDirectory.Validate(Path.GetDirectoryName(input)!);
            var result = KeyIdeasTemplate.Write(input, Path.Combine(directory, KeyIdeasTemplate.FileName));
            foreach (var (category, count) in result.ByGroup)
                await console.WriteLineAsync($"{category}: {count}");
            await console.WriteLineAsync($"Có tài liệu đúng: {result.WithDocument}; không có: {result.WithoutDocument}");
            return;
        }

        var grading = Parse(args, ["--master", "--ideas"], "--sources");
        var master = Path.GetFullPath(grading["--master"]);
        var output = ExternalOutputDirectory.Validate(Path.GetDirectoryName(master)!);
        var rubric = Path.Combine(ExternalOutputDirectory.FindRepositoryRoot(), "docs", "nckh", "RUBRIC_RQ1.md");
        var sheet = GradingSheetV2.Write(master, grading["--ideas"], rubric, output, grading.GetValueOrDefault("--sources"));
        await console.WriteLineAsync($"{Path.GetFileName(sheet.Path)} | {sheet.Bytes} byte | {sheet.Rows} câu");
        await console.WriteLineAsync($"Thiếu ý chính: {sheet.MissingIdeas}; DB trống: {sheet.BlankDb}; thiếu dữ liệu truy xuất đã lưu: {sheet.MissingSources}");
    }

    private static Dictionary<string, string> Parse(string[] args, string[] required, params string[] optional)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (!required.Contains(flag) && !optional.Contains(flag) || ++i >= args.Length ||
                !args[i].EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(flag, args[i]))
                throw new ArgumentException("Invalid RQ1 workbook options.");
        }
        if (!required.All(values.ContainsKey)) throw new ArgumentException("Missing RQ1 workbook options.");
        return values;
    }
}
