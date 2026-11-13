namespace ClinicManagement.AI.RagEval;

public sealed record DevGroupAllocation(string Group, int Target, int Check, int Main, int Shortage);
public sealed record DevSplitResult(EvaluationQuestion[] Check, EvaluationQuestion[] Main, DevGroupAllocation[] Groups);

public static class DevSplit
{
    private static readonly int[] Weights = [55, 35, 25, 15, 20];

    public static DevSplitResult Create(IReadOnlyList<EvaluationQuestion> questions, int seed)
    {
        var groups = DevQuestions.Groups.Select(group => questions.Where(q => q.Category == group)
            .OrderBy(q => q.Stt, StringComparer.Ordinal).ToArray()).ToArray();
        if (groups.Sum(g => g.Length) != questions.Count || questions.Any(q => string.IsNullOrWhiteSpace(q.Stt)) ||
            questions.Select(q => q.Stt).Distinct(StringComparer.Ordinal).Count() != questions.Count)
            throw new InvalidDataException("Dev split requires a known group and a unique code per question.");

        var ideal = Weights.Select(weight => weight * DevQuestions.CheckSize / (double)Weights.Sum()).ToArray();
        var target = ideal.Select(value => (int)Math.Floor(value)).ToArray();
        foreach (var i in Enumerable.Range(0, target.Length).OrderByDescending(i => ideal[i] - target[i])
                     .Take(DevQuestions.CheckSize - target.Sum()))
            target[i]++;
        var counts = groups.Select((rows, i) => Math.Min(rows.Length, target[i])).ToArray();
        var size = Math.Min(DevQuestions.CheckSize, questions.Count);
        for (var assigned = counts.Sum(); assigned < size; assigned++)
        {
            var next = Enumerable.Range(0, groups.Length).Where(i => counts[i] < groups[i].Length)
                .MaxBy(i => ideal[i] - counts[i]);
            counts[next]++;
        }

        var random = new Random(seed);
        var check = new List<EvaluationQuestion>();
        var main = new List<EvaluationQuestion>();
        var allocations = new DevGroupAllocation[groups.Length];
        for (var i = 0; i < groups.Length; i++)
        {
            random.Shuffle(groups[i]);
            check.AddRange(groups[i].Take(counts[i]));
            main.AddRange(groups[i].Skip(counts[i]));
            allocations[i] = new(DevQuestions.Groups[i], target[i], counts[i], groups[i].Length - counts[i],
                Math.Max(0, target[i] - counts[i]));
        }
        return new(check.OrderBy(q => q.Stt, StringComparer.Ordinal).ToArray(),
            main.OrderBy(q => q.Stt, StringComparer.Ordinal).ToArray(), allocations);
    }
}
