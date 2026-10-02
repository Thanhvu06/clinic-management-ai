using System.Text.Json;
using ClinicManagement.AI.Training;

namespace ClinicManagement.IntegrationTests;

public sealed class RoleIntentModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<Fixture> Models = new(CreateFixture);

    [Fact]
    public void SameSeedAndClassWeightsRepeatValidationProbabilities()
    {
        var fixture = Models.Value;
        foreach (var configuration in new[] { new RoleIntentConfiguration("plain", false, 20), new RoleIntentConfiguration("weighted", true, 20) })
        {
            var first = RoleIntentModelPipeline.Fit(fixture.Train, fixture.Labels, configuration).Predict(fixture.Validation, .5);
            var second = RoleIntentModelPipeline.Fit(fixture.Train, fixture.Labels, configuration).Predict(fixture.Validation, .5);
            AssertPredictionsEqual(first, second);
        }
    }

    [Fact]
    public void SavedModelReloadMatchesAndMetadataRepeatsWithoutEvalInput()
    {
        var fixture = Models.Value;
        var temporary = Path.Combine(Path.GetTempPath(), "clinic-role-model-" + Path.GetRandomFileName());
        var data = Path.Combine(temporary, "data");
        var output = Path.Combine(temporary, "output");
        Directory.CreateDirectory(data);
        try
        {
            File.Copy(Path.Combine(DataDirectory, RoleIntentDatasetGenerator.LabelsFile), Path.Combine(data, RoleIntentDatasetGenerator.LabelsFile));
            File.WriteAllText(Path.Combine(data, RoleIntentDatasetGenerator.TrainFile), JsonSerializer.Serialize(fixture.Train, JsonOptions));
            File.WriteAllText(Path.Combine(data, RoleIntentDatasetGenerator.ValidationFile), JsonSerializer.Serialize(fixture.Validation, JsonOptions));
            // No eval TSV or eval manifest exists: configuration/threshold selection cannot read it.
            var metadata = RoleIntentModelPipeline.Train(data, output);
            var metadataBytes = File.ReadAllBytes(Path.Combine(output, RoleIntentModelPipeline.MetadataFile));
            var loaded = RoleIntentModelPipeline.Load(output, out var reloadedMetadata);
            var predicted = loaded.Predict(fixture.Validation, metadata.ThresholdPolicy.Threshold);
            var direct = RoleIntentModelPipeline.Fit(fixture.Train, fixture.Labels, metadata.Configuration)
                .Predict(fixture.Validation, metadata.ThresholdPolicy.Threshold);
            AssertPredictionsEqual(direct, predicted);
            Assert.Equal(metadata.ModelSha256, reloadedMetadata.ModelSha256);
            var repeatedMetadata = RoleIntentModelPipeline.Train(data, output);
            Assert.True(metadataBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(output, RoleIntentModelPipeline.MetadataFile))),
                $"Model hashes: {metadata.ModelSha256} / {repeatedMetadata.ModelSha256}; validation hashes: {metadata.ValidationPredictionSha256} / {repeatedMetadata.ValidationPredictionSha256}");
            Assert.False(File.Exists(Path.Combine(data, RoleIntentModelPipeline.EvalFile)));
            Assert.False(File.Exists(Path.Combine(output, RoleIntentModelPipeline.ReportFile)));
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void RoleMaskNeverReturnsUnauthorizedLabelsAndProbabilitiesSumToOne()
    {
        var fixture = Models.Value;
        foreach (var role in RoleIntentDatasetGenerator.RoleCodes.Values)
        {
            // Force a forbidden class to have the largest raw score whenever one exists.
            var raw = fixture.Fitted.ScoreLabels.Select(label => fixture.Labels.Single(x => x.Label == label).Roles.Contains(role) ? .01f : 1f).ToArray();
            var prediction = fixture.Fitted.Decide(raw, role, 0);
            var allowed = fixture.Labels.Where(label => label.Roles.Contains(role)).Select(label => label.Label).ToArray();
            var real = fixture.Fitted.Predict(fixture.Validation.Select(row => row with { Role = role }).ToArray(), 0).Filtered;
            foreach (var result in real.Append(prediction))
            {
                Assert.Contains(result.TopLabel, allowed);
                Assert.Contains(result.DecisionLabel, allowed);
                Assert.All(result.Probabilities.Keys, label => Assert.Contains(label, allowed));
                Assert.Equal(1, result.Probabilities.Values.Sum(), precision: 12);
            }
        }
        Assert.Throws<ArgumentException>(() => fixture.Fitted.Decide(Enumerable.Repeat(1f, fixture.Labels.Length).ToArray(), "UnknownRole", 0));
    }

    [Fact]
    public void LowConfidenceAbstainsWithoutInvokingRuntimeFallback()
    {
        var fixture = Models.Value;
        var prediction = fixture.Fitted.Decide(Enumerable.Repeat(1f, fixture.Labels.Length).ToArray(), "Patient", .9);
        Assert.True(prediction.IsUncertain);
        Assert.Equal("không chắc", prediction.DecisionLabel);
        Assert.True(prediction.Confidence < .9);
        var same = fixture.Fitted.Decide(Enumerable.Repeat(1f, fixture.Labels.Length).ToArray(), "Patient", 0);
        Assert.False(same.IsUncertain);
        Assert.Equal(same.TopLabel, same.DecisionLabel);
    }

    [Fact]
    public void ExistingEvalReportBlocksRescoringAndRetrainingBeforeReadingInputs()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "clinic-role-once-" + Path.GetRandomFileName());
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, RoleIntentModelPipeline.ReportFile), "frozen sentinel");
            var missingData = Path.Combine(temporary, "missing-data");
            Assert.Throws<InvalidOperationException>(() => RoleIntentModelPipeline.EvaluateOnce(missingData, temporary));
            Assert.Throws<InvalidOperationException>(() => RoleIntentModelPipeline.Train(missingData, temporary));
            Assert.Equal("frozen sentinel", File.ReadAllText(Path.Combine(temporary, RoleIntentModelPipeline.ReportFile)));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void ThresholdSelectionUsesValidationCorrectnessAndCoverage()
    {
        var fixture = Models.Value;
        var rows = Enumerable.Range(0, 40).Select(index => fixture.Validation[index % fixture.Validation.Length]).ToArray();
        var predictions = rows.Select((row, index) => new RoleIntentPrediction(index < 30 ? row.Label : "WrongLabel", "", index < 30 ? .8 : .2, false, new())).ToArray();
        var policy = RoleIntentModelPipeline.SelectThreshold(rows, predictions);
        Assert.Equal(.25, policy.Threshold, precision: 12);
        Assert.Equal(30, policy.Trials.Single(x => Math.Abs(x.Threshold - .25) < 1e-12).Accepted);
        Assert.Equal(.75, policy.Trials.Single(x => Math.Abs(x.Threshold - .25) < 1e-12).Coverage);
    }

    private static void AssertPredictionsEqual(RoleIntentPredictions expected, RoleIntentPredictions actual)
    {
        foreach (var (left, right) in expected.Unfiltered.Zip(actual.Unfiltered).Concat(expected.Filtered.Zip(actual.Filtered)))
        {
            Assert.Equal(left.TopLabel, right.TopLabel);
            Assert.Equal(left.DecisionLabel, right.DecisionLabel);
            Assert.Equal(left.IsUncertain, right.IsUncertain);
            Assert.Equal(left.Probabilities.Keys, right.Probabilities.Keys);
            foreach (var label in left.Probabilities.Keys) Assert.Equal(left.Probabilities[label], right.Probabilities[label], precision: 12);
        }
        Assert.Equal(expected.Unfiltered.Length, actual.Unfiltered.Length);
        Assert.Equal(expected.Filtered.Length, actual.Filtered.Length);
    }

    private static Fixture CreateFixture()
    {
        var labels = RoleIntentDatasetGenerator.ReadLabels(DataDirectory).ToArray();
        var train = Read(RoleIntentDatasetGenerator.TrainFile).Where(x => x.Variant == "original").GroupBy(x => x.Label).SelectMany(x => x.Take(3)).ToArray();
        var validation = Read(RoleIntentDatasetGenerator.ValidationFile).Where(x => x.Variant == "original").ToArray();
        return new(labels, train, validation, RoleIntentModelPipeline.Fit(train, labels, new("fixture", false, 20)));
    }
    private static RoleIntentRecord[] Read(string file) => JsonSerializer.Deserialize<RoleIntentRecord[]>(File.ReadAllText(Path.Combine(DataDirectory, file)), JsonOptions)!;
    private sealed record Fixture(RoleIntentLabel[] Labels, RoleIntentRecord[] Train, RoleIntentRecord[] Validation, RoleIntentFittedModel Fitted);
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
