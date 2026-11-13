using System.Globalization;
using System.Security.Cryptography;

namespace ClinicManagement.AI.RagEval;

public sealed record JudgeOptions(string Xlsx, string Output, string Model, int Runs, int Seed)
{
    public string Metadata => Path.Combine(Path.GetDirectoryName(Output)!, "judge-run.json");
    public string Checkpoint => Path.Combine(Path.GetDirectoryName(Output)!, "judge-checkpoint.jsonl");
    public bool Resume { get; init; }
    public bool SeedSpecified { get; init; } = true;

    public static JudgeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var resume = false;
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (flag == "--resume" && !resume)
            {
                resume = true;
                continue;
            }
            if (flag is not ("--xlsx" or "--out" or "--model" or "--runs" or "--seed") || ++i >= args.Length ||
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
        if (runs < 1 || string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Invalid judge model or runs.");
        return new(Path.GetFullPath(input), Path.GetFullPath(output), model, runs, seed)
        {
            Resume = resume,
            SeedSpecified = seedSpecified
        };
    }

    public void EnsureNewOutputs()
    {
        foreach (var path in new[] { Output, Metadata }) StatisticsReport.EnsureNew(path);
    }
}
