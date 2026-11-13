using System.Globalization;
using System.Security.Cryptography;

namespace ClinicManagement.AI.RagEval;

public sealed record JudgeOptions(string Xlsx, string Output, string Model, int Runs, int Seed)
{
    public string Metadata => Path.Combine(Path.GetDirectoryName(Output)!, V2 ? "judge-run-v2.json" : "judge-run.json");
    public string Checkpoint => Path.Combine(Path.GetDirectoryName(Output)!, V2 ? "judge-checkpoint-v2.jsonl" : "judge-checkpoint.jsonl");
    public bool Resume { get; init; }
    public bool SeedSpecified { get; init; } = true;
    public bool V2 { get; init; }
    public bool MeasurePrompt { get; init; }
    public int? Limit { get; init; }
    public int Context { get; init; } = 8192;

    public static JudgeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var resume = false;
        var v2 = false;
        var measure = false;
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (flag == "--resume" && !resume)
            {
                resume = true;
                continue;
            }
            if (flag == "--v2" && !v2) { v2 = true; continue; }
            if (flag == "--measure-prompt" && !measure) { measure = true; continue; }
            if (flag is not ("--xlsx" or "--out" or "--model" or "--runs" or "--seed" or "--limit" or "--context") || ++i >= args.Length ||
                args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(flag, args[i]))
                throw new ArgumentException("Invalid judge options.");
        }
        if (!values.TryGetValue("--xlsx", out var input) || !values.TryGetValue("--out", out var output) ||
            !input.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || !output.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Judge requires input and new output workbooks.");
        var runs = int.Parse(values.GetValueOrDefault("--runs", "2"), CultureInfo.InvariantCulture);
        var seedSpecified = values.TryGetValue("--seed", out var text);
        var seed = seedSpecified ? int.Parse(text!, CultureInfo.InvariantCulture) :
            resume ? 0 : RandomNumberGenerator.GetInt32(int.MaxValue);
        var model = values.GetValueOrDefault("--model", "gemma2:9b");
        var context = int.Parse(values.GetValueOrDefault("--context", "8192"), CultureInfo.InvariantCulture);
        int? limit = values.TryGetValue("--limit", out var rawLimit) ? int.Parse(rawLimit, CultureInfo.InvariantCulture) : null;
        if (runs < 1 || string.IsNullOrWhiteSpace(model) || context is not (4096 or 8192) || limit is <= 0 ||
            !v2 && (limit.HasValue || measure || context != 8192) || measure && resume)
            throw new ArgumentException("Invalid judge options.");
        return new(Path.GetFullPath(input), Path.GetFullPath(output), model, runs, seed)
        {
            Resume = resume,
            SeedSpecified = seedSpecified, V2 = v2, Limit = limit, Context = context, MeasurePrompt = measure
        };
    }

    public void EnsureNewOutputs()
    {
        foreach (var path in new[] { Output, Metadata }) StatisticsReport.EnsureNew(path);
    }
}
