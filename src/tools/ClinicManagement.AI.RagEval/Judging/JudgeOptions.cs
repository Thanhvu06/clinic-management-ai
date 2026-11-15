using System.Globalization;
using System.Security.Cryptography;

namespace ClinicManagement.AI.RagEval;

public enum JudgeSourceMode { Recorded, Corpus }

public sealed record JudgeOptions(string Xlsx, string Output, string Model, int Runs, int Seed)
{
    private string Suffix => Version == 1 ? "" : $"-v{Version}";
    public string Metadata => Path.Combine(Path.GetDirectoryName(Output)!, $"judge-run{Suffix}.json");
    public string Checkpoint => Path.Combine(Path.GetDirectoryName(Output)!, $"judge-checkpoint{Suffix}.jsonl");
    public bool Resume { get; init; }
    public bool SeedSpecified { get; init; } = true;
    public bool V2 { get; init; }
    public bool V3 { get; init; }
    public int Version => V3 ? 3 : V2 ? 2 : 1;
    public bool HasCitationFlags => Version >= 2;
    internal bool UsesSentenceIds => V3 && SourceMode == JudgeSourceMode.Recorded;
    public int? PilotStratified { get; init; }
    public string? Master { get; init; }
    public bool MeasurePrompt { get; init; }
    public int? Limit { get; init; }
    public int Context { get; init; } = 8192;
    public string? SafetyClassificationPath { get; init; }
    public JudgeSourceMode SourceMode { get; init; }
    internal string SourceModeName => SourceMode.ToString().ToLowerInvariant();

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
            if (flag is not ("--xlsx" or "--out" or "--model" or "--runs" or "--seed" or "--limit" or "--context" or
                "--safety-classification" or "--judge-sources" or "--judge-version" or "--pilot-stratified" or "--master") || ++i >= args.Length ||
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
        var sourceMode = values.GetValueOrDefault("--judge-sources", "recorded") switch
        {
            "recorded" => JudgeSourceMode.Recorded,
            "corpus" => JudgeSourceMode.Corpus,
            _ => throw new ArgumentException("--judge-sources must be recorded or corpus.")
        };
        int? limit = values.TryGetValue("--limit", out var rawLimit) ? int.Parse(rawLimit, CultureInfo.InvariantCulture) : null;
        var version = int.Parse(values.GetValueOrDefault("--judge-version", v2 ? "2" : "1"), CultureInfo.InvariantCulture);
        int? pilot = values.TryGetValue("--pilot-stratified", out var rawPilot) ? int.Parse(rawPilot, CultureInfo.InvariantCulture) : null;
        var master = values.GetValueOrDefault("--master");
        if (runs < 1 || string.IsNullOrWhiteSpace(model) || version is < 1 or > 3 || v2 && version != 2 || limit is <= 0 ||
            (version == 3 ? context < 1024 || context % 256 != 0 : context is not (4096 or 8192)) ||
            version == 1 && (limit.HasValue || measure || context != 8192) || measure && resume ||
            pilot is <= 0 || pilot.HasValue && (version != 3 || limit.HasValue || master is null) ||
            master is not null && (!pilot.HasValue || !master.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Invalid judge options.");
        if (version == 3 && (Path.GetFileName(output).Equals("RAG_phieu_cham_AI.xlsx", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(output).Equals("RAG_phieu_cham_AI_v2.xlsx", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Use a separate output workbook for judge v3.");
        return new(Path.GetFullPath(input), Path.GetFullPath(output), model, runs, seed)
        {
            Resume = resume,
            SeedSpecified = seedSpecified, V2 = version == 2, V3 = version == 3, Limit = limit, Context = context,
            MeasurePrompt = measure, PilotStratified = pilot, Master = master is null ? null : Path.GetFullPath(master),
            SafetyClassificationPath = values.GetValueOrDefault("--safety-classification"), SourceMode = sourceMode
        };
    }

    public void EnsureNewOutputs()
    {
        foreach (var path in new[] { Output, Metadata }) StatisticsReport.EnsureNew(path);
    }
}
