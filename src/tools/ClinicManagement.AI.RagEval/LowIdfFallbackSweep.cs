namespace ClinicManagement.AI.RagEval;

internal sealed record LowIdfFallbackSweep(EvaluationInput Input, IReadOnlyList<string> Probes)
{
    public static IReadOnlyList<double?> Thresholds { get; } = [null, 0.50, 0.55, 0.60, 0.65, 0.70];
    private const string Usage =
        "Usage: RagEval sweep --low-idf-fallback --csv dev-chinh.csv [--output report.json] [--probe question]...";

    public static LowIdfFallbackSweep Parse(string[] args)
    {
        var inputArgs = new List<string>();
        var probes = new List<string>();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException(Usage);

            switch (args[i])
            {
                case "--probe":
                    probes.Add(args[i + 1]);
                    break;
                case "--csv":
                case "--output":
                    inputArgs.Add(args[i]);
                    inputArgs.Add(args[i + 1]);
                    break;
                default:
                    throw new ArgumentException(Usage);
            }
        }

        var input = EvaluationInput.Parse(inputArgs.ToArray());
        if (!input.IsCsv || input.QuestionFiles.Count != 1)
            throw new ArgumentException(Usage);
        return new(input, probes);
    }
}
