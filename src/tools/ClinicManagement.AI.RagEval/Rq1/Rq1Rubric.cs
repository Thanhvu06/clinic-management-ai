namespace ClinicManagement.AI.RagEval;

internal static class Rq1Rubric
{
    public static string PathInRepository() =>
        Path.Combine(ExternalOutputDirectory.FindRepositoryRoot(), "docs", "nckh", "RUBRIC_RQ1.md");

    public static Rq1JudgeRubric ReadForJudge()
    {
        var lines = File.ReadAllLines(PathInRepository());
        var start = Find(lines, "## G. ");
        var end = Find(lines, "G6. ", start + 1);
        var amendmentLines = lines[start..end];
        for (var rule = 1; rule <= 5; rule++)
            if (!amendmentLines.Any(line => line.StartsWith($"G{rule}. ", StringComparison.Ordinal)))
                throw new InvalidDataException("RQ1 rubric is missing a judging amendment rule.");
        return new(string.Join('\n', amendmentLines).TrimEnd(), Section(lines, "## D. ", "## E. ") + "\n\n" +
            Section(lines, "## F. ", "## G. "));
    }

    private static string Section(string[] lines, string heading, string next)
    {
        var start = Find(lines, heading);
        return string.Join('\n', lines[start..Find(lines, next, start + 1)]).TrimEnd();
    }

    private static int Find(string[] lines, string prefix, int start = 0)
    {
        var index = Array.FindIndex(lines, start, line => line.StartsWith(prefix, StringComparison.Ordinal));
        return index >= 0 ? index : throw new InvalidDataException("RQ1 rubric is missing a required section.");
    }
}

internal sealed record Rq1JudgeRubric(string Amendment, string EmergencyRules);
