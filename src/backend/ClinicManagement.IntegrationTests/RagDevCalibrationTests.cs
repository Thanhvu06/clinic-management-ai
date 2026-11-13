extern alias RagEval;

using RagEval::ClinicManagement.AI.RagEval;

namespace ClinicManagement.IntegrationTests;

public class RagDevCalibrationTests
{
    [Fact]
    public async Task FrozenRq2InputsMapAll262QuestionsWithoutChangingScope()
    {
        var questions = await DevQuestions.LoadRq2Async();
        Assert.Equal(new[] { 130, 47, 10, 60, 15 },
            DevQuestions.Groups.Select(group => questions.Count(q => q.Category == group)));
        Assert.Equal(192, questions.Count(q => q.ExpectedDocId is not null));
        Assert.Equal(262, questions.Select(q => q.Stt).Distinct().Count());
    }

    [Theory]
    [InlineData(0.90, 0.00, true)]
    [InlineData(0.00, 0.60, true)]
    [InlineData(0.8999, 0.5999, false)]
    public void OverlapUsesInclusiveIndependentThresholds(double cosine, double jaccard, bool expected)
    {
        Assert.Equal(expected, DevOverlap.IsOverlap(cosine, jaccard));
    }

    [Fact]
    public async Task OverlapComparesAgainstAllHiddenQuestionsAndPrintsOnlyAggregates()
    {
        EvaluationQuestion[] dev =
        [
            new("one two three four five", "doc", "dev-1", DevQuestions.InScope),
            new("six seven", null, "dev-2", DevQuestions.Near),
            new("eight nine", null, "dev-3", DevQuestions.Far)
        ];
        var result = DevOverlap.Compare(dev, ["private-marker", "one two three", "different"],
            [[0, 0, 1], [1, 0, 0], [0, 0.6f, 0.8f]], [[1, 0, 0], [0, 1, 0], [-1, 0, 0]]);

        Assert.Equal("dev-3", Assert.Single(result.Retained).Stt);
        Assert.Equal(2, result.Removed);
        Assert.Equal(new[] { 1, 0, 1, 0, 0 }, result.RemovedByGroup);
        Assert.Equal(3, result.Histogram.Sum());
        Assert.Equal(1, result.Histogram[39]);
        using var output = new StringWriter();
        await DevOverlap.WriteSummaryAsync(result, output);
        Assert.DoesNotContain("private-marker", output.ToString());
        Assert.DoesNotContain("dev-", output.ToString());
    }

    [Fact]
    public void SplitMatchesTargetQuotasAndIsReproducible()
    {
        var questions = Questions([60, 40, 30, 20, 25]);
        var split = DevSplit.Create(questions, DevQuestions.Seed);
        var repeat = DevSplit.Create(questions.Reverse().ToArray(), DevQuestions.Seed);

        Assert.Equal(70, split.Check.Length);
        Assert.Equal(105, split.Main.Length);
        Assert.Equal(new[] { 26, 16, 12, 7, 9 }, split.Groups.Select(group => group.Check));
        Assert.All(split.Groups, group => Assert.Equal(0, group.Shortage));
        Assert.Equal(split.Check.Select(q => q.Stt), repeat.Check.Select(q => q.Stt));
        Assert.Equal(split.Main.Select(q => q.Stt), repeat.Main.Select(q => q.Stt));
        Assert.Empty(split.Check.Intersect(split.Main));
    }

    [Fact]
    public void SplitTakesAllFromShortGroupsAndKeepsCheckSizeWhenPossible()
    {
        var questions = Questions([90, 30, 2, 30, 4]);
        var split = DevSplit.Create(questions, DevQuestions.Seed);

        Assert.Equal(70, split.Check.Length);
        Assert.Equal(86, split.Main.Length);
        Assert.Equal(new[] { 0, 0, 10, 0, 5 }, split.Groups.Select(group => group.Shortage));
        Assert.Equal(2, split.Groups[2].Check);
        Assert.Equal(4, split.Groups[4].Check);
        Assert.Empty(split.Check.Intersect(split.Main));
    }

    [Fact]
    public void NumericEditChangesOnlyOneDigit()
    {
        const string original = "Nhịn ăn 8 giờ, dùng 5 mg và gọi 115 nếu cần.";
        var edits = DevEdits.Numbers(original);

        Assert.Equal(new[] { "Nhịn ăn 9 giờ, dùng 5 mg và gọi 115 nếu cần.",
            "Nhịn ăn 8 giờ, dùng 6 mg và gọi 115 nếu cần.",
            "Nhịn ăn 8 giờ, dùng 5 mg và gọi 116 nếu cần." }, edits.Select(edit => edit.Apply(original)));
        Assert.All(edits, edit => Assert.Equal(1, edit.Length));
    }

    [Fact]
    public void NegationEditChangesExactlyOnePhrase()
    {
        const string original = "Không được tự đổi lịch. Nên gọi lễ tân.";
        var edits = DevEdits.Negations(original);
        Assert.Equal(new[] { "Được tự đổi lịch. Nên gọi lễ tân.", "Không được tự đổi lịch. Không nên gọi lễ tân." },
            edits.Select(edit => edit.Apply(original)));
    }

    [Fact]
    public void PlantSelectionAllowsOneMutationPerAnswerSide()
    {
        var selection = new DevPlantSelection();
        var answer = new DevAnswer("dev-1", "A", "LLM", "Nên nhịn 8 giờ.", [], null);
        Assert.True(selection.TryAdd(answer, Assert.Single(DevEdits.Numbers(answer.Text)), "Sai số liệu"));
        Assert.False(selection.TryAdd(answer with { Text = "Nên nhịn 9 giờ." },
            Assert.Single(DevEdits.Negations(answer.Text)), "Phủ định"));
        Assert.True(selection.TryAdd(answer with { Side = "B", Mode = "Trích đoạn" },
            Assert.Single(DevEdits.Negations(answer.Text)), "Phủ định"));
        Assert.Equal(2, selection.Variants.Count);
    }

    private static EvaluationQuestion[] Questions(int[] counts) => DevQuestions.Groups.SelectMany((group, index) =>
        Enumerable.Range(0, counts[index]).Select(i => new EvaluationQuestion("question", null, $"{index}-{i:D3}", group))).ToArray();
}
