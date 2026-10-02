using System.Text.Json;
using ClinicManagement.AI.Training;

namespace ClinicManagement.IntegrationTests;

public sealed class RoleIntentModelV2Tests
{
    private static readonly Lazy<Fixture> Models = new(CreateFixture);
    private static RoleIntentV2Configuration[] SmallConfigurations => new[]
    {
        new RoleIntentV2Configuration("sdca_raw_plain", "sdca", false, false, 30, 0, .01f),
        new RoleIntentV2Configuration("sdca_l2_weighted", "sdca", true, true, 30, .01f, .001f),
        new RoleIntentV2Configuration("lbfgs_raw_weighted", "lbfgs", false, true, 30, 0, .01f),
        new RoleIntentV2Configuration("lbfgs_l2_plain", "lbfgs", true, false, 30, .01f, .001f)
    };

    [Fact]
    public void BothTrainersRepeatLabelsAndProbabilitiesWithinTolerance()
    {
        var fixture = Models.Value;
        foreach (var configuration in SmallConfigurations)
        {
            var first = RoleIntentModelV2Pipeline.Fit(fixture.Train, fixture.Labels, configuration).Predict(fixture.Validation, 0);
            var second = RoleIntentModelV2Pipeline.Fit(fixture.Train, fixture.Labels, configuration).Predict(fixture.Validation, 0);
            var comparison = RoleIntentModelV2Pipeline.ComparePredictions(first, second);
            Assert.True(comparison.LabelsMatchExactly);
            Assert.True(comparison.Passed);
            Assert.InRange(comparison.MaximumProbabilityDifference, 0, 1e-6);
        }
    }

    [Fact]
    public void SaveLoadAndRepeatedSelectionMatchWithoutAnyEvalFiles()
    {
        var fixture = Models.Value;
        var temporary = Path.Combine(Path.GetTempPath(), "clinic-role-v2-" + Path.GetRandomFileName());
        var data = Path.Combine(temporary, "data");
        var output = Path.Combine(temporary, "output");
        Directory.CreateDirectory(data);
        try
        {
            File.Copy(Path.Combine(DataDirectory, RoleIntentDatasetGenerator.LabelsFile), Path.Combine(data, RoleIntentDatasetGenerator.LabelsFile));
            File.WriteAllText(Path.Combine(data, RoleIntentDatasetGenerator.TrainFile), JsonSerializer.Serialize(fixture.Train, RoleIntentModelPipeline.JsonOptions));
            File.WriteAllText(Path.Combine(data, RoleIntentDatasetGenerator.ValidationFile), JsonSerializer.Serialize(fixture.Validation, RoleIntentModelPipeline.JsonOptions));
            var metadata = RoleIntentModelV2Pipeline.Train(data, output, SmallConfigurations);
            var loaded = RoleIntentModelV2Pipeline.Load(output, out var loadedMetadata);
            var predictions = loaded.Predict(fixture.Validation, metadata.ThresholdPolicy.Threshold);
            var direct = RoleIntentModelV2Pipeline.Fit(fixture.Train, fixture.Labels, metadata.Configuration).Predict(fixture.Validation, metadata.ThresholdPolicy.Threshold);
            Assert.True(RoleIntentModelV2Pipeline.ComparePredictions(direct, predictions).Passed);
            Assert.Equal(metadata.ModelSha256, loadedMetadata.ModelSha256);
            var repeated = RoleIntentModelV2Pipeline.Train(data, output, SmallConfigurations);
            var repeatedModel = RoleIntentModelV2Pipeline.Load(output, out _);
            Assert.Equal(metadata.Configuration, repeated.Configuration);
            Assert.Equal(metadata.ThresholdPolicy.Threshold, repeated.ThresholdPolicy.Threshold);
            Assert.True(RoleIntentModelV2Pipeline.ComparePredictions(predictions, repeatedModel.Predict(fixture.Validation, repeated.ThresholdPolicy.Threshold)).Passed);
            Assert.All(metadata.ConfigurationTrials, trial => Assert.True(trial.Reproducibility.Passed));
            Assert.False(File.Exists(Path.Combine(data, RoleIntentModelPipeline.EvalFile)));
            Assert.False(File.Exists(Path.Combine(output, RoleIntentModelV2Pipeline.ReportFile)));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void V2RoleMaskKeepsOnlyAuthorizedLabels()
    {
        var fixture = Models.Value;
        foreach (var role in RoleIntentDatasetGenerator.RoleCodes.Values)
        {
            var allowed = fixture.Labels.Where(x => x.Roles.Contains(role)).Select(x => x.Label).ToArray();
            var predictions = fixture.Model.Predict(fixture.Validation.Select(x => x with { Role = role }).ToArray(), 0).Filtered;
            foreach (var prediction in predictions)
            {
                Assert.Contains(prediction.TopLabel, allowed);
                Assert.All(prediction.Probabilities.Keys, label => Assert.Contains(label, allowed));
                Assert.Equal(1, prediction.Probabilities.Values.Sum(), precision: 12);
            }
        }
    }

    [Fact]
    public void RepetitionRequiresMatchingLabelsAndEnforcesProbabilityTolerance()
    {
        static RoleIntentPredictions Scores(string label, double probability) => new(
            new[] { new RoleIntentPrediction(label, label, probability, false, new() { ["A"] = probability, ["B"] = 1 - probability }) },
            new[] { new RoleIntentPrediction(label, label, probability, false, new() { ["A"] = probability, ["B"] = 1 - probability }) });
        Assert.True(RoleIntentModelV2Pipeline.ComparePredictions(Scores("A", .7), Scores("A", .7 + 5e-7)).Passed);
        Assert.False(RoleIntentModelV2Pipeline.ComparePredictions(Scores("A", .7), Scores("A", .7 + 2e-6)).Passed);
        Assert.False(RoleIntentModelV2Pipeline.ComparePredictions(Scores("A", .7), Scores("B", .7)).Passed);
    }

    [Fact]
    public void ThresholdMaximizesCoverageOrReportsUnmetTarget()
    {
        var row = Models.Value.Validation[0];
        var rows = Enumerable.Repeat(row, 40).ToArray();
        var predictions = rows.Select((x, i) => new RoleIntentPrediction(i < 30 ? x.Label : "Wrong", "", i < 30 ? .8 : .2, false, new())).ToArray();
        var selected = RoleIntentModelV2Pipeline.SelectThreshold(rows, predictions);
        Assert.Equal(.25, selected.Threshold);
        Assert.Equal(.75, selected.Trials.Single(x => x.Threshold == .25).Coverage);
        Assert.All(new[] { .15, .20, .25, .30, .40, .50 }, threshold => Assert.Contains(selected.Trials, x => x.Threshold == threshold));
        var failed = RoleIntentModelV2Pipeline.SelectThreshold(rows, predictions.Select(x => x with { TopLabel = "Wrong" }).ToArray());
        Assert.Equal(0, failed.Threshold);
        Assert.Contains("No nonempty", failed.Reason);
    }

    [Fact]
    public void ExistingV2ReportBlocksTrainingAndEvaluationBeforeReadingInputs()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "clinic-role-v2-freeze-" + Path.GetRandomFileName());
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, RoleIntentModelV2Pipeline.ReportFile), "frozen");
            Assert.Throws<InvalidOperationException>(() => RoleIntentModelV2Pipeline.Train("missing", temporary));
            Assert.Throws<InvalidOperationException>(() => RoleIntentModelV2Pipeline.EvaluateOnce("missing", temporary, "missing-v1"));
            Assert.Equal("frozen", File.ReadAllText(Path.Combine(temporary, RoleIntentModelV2Pipeline.ReportFile)));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static Fixture CreateFixture()
    {
        var labels = RoleIntentDatasetGenerator.ReadLabels(DataDirectory).ToArray();
        RoleIntentRecord[] Read(string file) => JsonSerializer.Deserialize<RoleIntentRecord[]>(File.ReadAllText(Path.Combine(DataDirectory, file)), RoleIntentModelPipeline.JsonOptions)!;
        var train = Read(RoleIntentDatasetGenerator.TrainFile).Where(x => x.Variant == "original").GroupBy(x => x.Label).SelectMany(x => x.Take(3)).ToArray();
        var validation = Read(RoleIntentDatasetGenerator.ValidationFile).Where(x => x.Variant == "original").ToArray();
        return new(labels, train, validation, RoleIntentModelV2Pipeline.Fit(train, labels, SmallConfigurations[0]));
    }
    private sealed record Fixture(RoleIntentLabel[] Labels, RoleIntentRecord[] Train, RoleIntentRecord[] Validation, RoleIntentFittedModel Model);
    private static string DataDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, "src", "tools", "ClinicManagement.AI.Training", "data");
                if (File.Exists(Path.Combine(path, RoleIntentDatasetGenerator.TrainFile))) return path;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Role intent data not found.");
        }
    }
}
