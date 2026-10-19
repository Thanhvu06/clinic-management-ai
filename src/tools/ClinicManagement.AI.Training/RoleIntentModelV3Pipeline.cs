using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.ML;

namespace ClinicManagement.AI.Training;

public static class RoleIntentModelV3Pipeline
{
    public const string Version = "role-intent-mlnet-v3";
    public const string ModelFile = "role_intent_model_v3.zip";
    public const string MetadataFile = "role_intent_model_meta_v3.json";
    public const string ReportFile = "role_intent_eval_report_v3.json";
    public const string SelectionFile = "role_intent_selection_v3.json";
    public const string SelectionRule = "Before hidden evaluation: select v3 iff role-filtered validation macro-F1 >= frozen v2 and accepted accuracy at v3's validation-selected threshold >= 90%; otherwise retain v2. Fixed v2 configuration only; no grid search.";
    public static readonly RoleIntentV2Configuration Configuration = new("lbfgs_l2_plain_500_zeroL1", "lbfgs", true, false, 500, 0, .01f);

    public static RoleIntentV2Metadata Train(string dataDirectory, string outputDirectory)
    {
        if (File.Exists(Path.Combine(outputDirectory, ReportFile))) throw new InvalidOperationException("V3 evaluation is frozen; do not retrain.");
        var labels = RoleIntentDatasetGenerator.ReadLabels(dataDirectory).ToArray();
        var human = RoleIntentHumanData.ReadAll(dataDirectory);
        var original = RoleIntentModelPipeline.ReadRecords(dataDirectory, RoleIntentDatasetGenerator.TrainFile, "train", labels);
        var validation = RoleIntentModelPipeline.ReadRecords(dataDirectory, RoleIntentDatasetGenerator.ValidationFile, "validation", labels);
        if (original.Length != 866 || validation.Length != 283) throw new InvalidDataException("Frozen train/validation count mismatch.");
        var train = original.Concat(human).ToArray();
        var fitted = RoleIntentModelV2Pipeline.Fit(train, labels, Configuration);
        var scores = fitted.Predict(validation, 0);
        var repeat = RoleIntentModelV2Pipeline.Fit(train, labels, Configuration).Predict(validation, 0);
        var reproducibility = RoleIntentModelV2Pipeline.ComparePredictions(scores, repeat);
        if (!reproducibility.Passed) throw new InvalidOperationException("V3 reproducibility failed.");
        var threshold = RoleIntentModelV2Pipeline.SelectThreshold(validation, scores.Filtered);
        var final = fitted.Predict(validation, threshold.Threshold);
        var trainMetrics = RoleIntentModelPipeline.MetricsPair(train, fitted.Predict(train, threshold.Threshold), fitted.ScoreLabels);
        var validationMetrics = RoleIntentModelPipeline.MetricsPair(validation, final, fitted.ScoreLabels);
        var trial = new RoleIntentV2Trial(Configuration, trainMetrics.Unfiltered.Accuracy, trainMetrics.Filtered.Accuracy,
            validationMetrics.Unfiltered.Accuracy, validationMetrics.Filtered.Accuracy, validationMetrics.Unfiltered.MacroF1, validationMetrics.Filtered.MacroF1, reproducibility);
        Directory.CreateDirectory(outputDirectory);
        RoleIntentModelPipeline.SaveDeterministic(fitted, Path.Combine(outputDirectory, ModelFile));
        var hashes = new[] { RoleIntentDatasetGenerator.TrainFile, RoleIntentDatasetGenerator.ValidationFile, RoleIntentDatasetGenerator.LabelsFile,
            RoleIntentHumanData.DataFile, RoleIntentHumanData.ManifestFile, RoleIntentHumanData.SupplementFile, RoleIntentHumanData.SupplementManifestFile }
            .ToDictionary(x => x, x => RoleIntentDatasetGenerator.FileSha256(Path.Combine(dataDirectory, x)), StringComparer.Ordinal);
        var metadata = new RoleIntentV2Metadata(Version, RoleIntentModelPipeline.MlPackageVersion, RoleIntentModelPipeline.Seed,
            fitted.ScoreLabels, labels, Configuration, [trial], SelectionRule, RoleIntentModelV2Pipeline.RepetitionRule,
            RoleIntentModelV2Pipeline.ProbabilityTolerance, threshold,
            "Identical frozen v2 text normalization, word 1-2 + character 2,3,4 grams; L2 at each featurizer and concatenated features.",
            "Globally normalized maximum-entropy probabilities, renormalized after catalog role mask; not calibrated on real users.",
            hashes, RoleIntentDatasetGenerator.FileSha256(Path.Combine(outputDirectory, ModelFile)), PredictionHash(final), trainMetrics, validationMetrics);
        Write(Path.Combine(outputDirectory, MetadataFile), metadata);
        var loaded = Load(outputDirectory, out _).Predict(validation, threshold.Threshold);
        if (!RoleIntentModelV2Pipeline.ComparePredictions(final, loaded).Passed) throw new InvalidOperationException("V3 saved model predictions differ.");
        var v2Directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputDirectory))!, "role-intent-v2");
        RoleIntentModelV2Pipeline.Load(v2Directory, out var v2);
        var selected = validationMetrics.Filtered.MacroF1 >= v2.Validation.Filtered.MacroF1 && validationMetrics.Filtered.AcceptedAccuracy >= .9;
        Write(Path.Combine(outputDirectory, SelectionFile), new RoleIntentV3Selection(SelectionRule, selected, selected ? "v3" : "v2",
            selected ? "chọn v3" : "không chọn", v2.ModelSha256, metadata.ModelSha256, v2.Validation.Filtered.MacroF1,
            metadata.Validation.Filtered.MacroF1, metadata.Validation.Filtered.AcceptedAccuracy, metadata.ThresholdPolicy.Threshold));
        return metadata;
    }

    public static RoleIntentFittedModel Load(string directory, out RoleIntentV2Metadata metadata)
    {
        metadata = RoleIntentModelPipeline.Read<RoleIntentV2Metadata>(Path.Combine(directory, MetadataFile));
        if (metadata.ModelVersion != Version || metadata.Configuration != Configuration || metadata.ModelSha256 != RoleIntentDatasetGenerator.FileSha256(Path.Combine(directory, ModelFile)))
            throw new InvalidDataException("V3 metadata/model mismatch.");
        var ml = new MLContext(metadata.Seed);
        var transformer = ml.Model.Load(Path.Combine(directory, ModelFile), out var schema);
        var probe = ml.Data.LoadFromEnumerable(new[] { new RoleIntentModelInput() });
        var slots = RoleIntentModelPipeline.ScoreLabels(transformer.Transform(probe));
        if (!slots.SequenceEqual(metadata.ScoreLabels, StringComparer.Ordinal)) throw new InvalidDataException("V3 score labels mismatch.");
        return new(ml, transformer, schema, slots, metadata.LabelCatalog);
    }

    public static RoleIntentV3EvaluationReport EvaluateOnce(string dataDirectory, string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, ReportFile);
        if (File.Exists(path)) throw new InvalidOperationException("V3 eval already scored; do not rescore.");
        var model = Load(outputDirectory, out var metadata);
        var selection = RoleIntentModelPipeline.Read<RoleIntentV3Selection>(Path.Combine(outputDirectory, SelectionFile));
        if (selection.V3ModelSha256 != metadata.ModelSha256 || selection.Rule != SelectionRule) throw new InvalidDataException("V3 selection mismatch.");
        foreach (var (file, hash) in metadata.DataSha256)
            if (RoleIntentDatasetGenerator.FileSha256(Path.Combine(dataDirectory, file)) != hash) throw new InvalidDataException("Frozen v3 input mismatch.");
        var evalPath = Path.Combine(dataDirectory, RoleIntentModelPipeline.EvalFile);
        if (RoleIntentDatasetGenerator.FileSha256(evalPath) != RoleIntentModelPipeline.EvalSha256) throw new InvalidDataException("Frozen AI eval mismatch.");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var lines = File.ReadAllLines(evalPath, Encoding.UTF8);
        if (lines.Length != 241 || lines[0] != RoleIntentHumanData.Header) throw new InvalidDataException("AI eval header/count mismatch.");
        var rows = lines.Skip(1).Select((line, i) =>
        {
            var columns = line.Split('\t');
            if (columns.Length != 4) throw new InvalidDataException("AI eval columns mismatch.");
            return new RoleIntentRecord($"RI-EVAL-{i + 1:D3}", "", columns[0], columns[2], columns[1], "eval", "original", columns[3]);
        }).ToArray();
        var report = new RoleIntentV3EvaluationReport(Version, "Previously seen AI-written eval; not independent blind data; no selection uses eval.",
            metadata.ModelSha256, RoleIntentModelPipeline.EvalSha256, selection, metadata.Train, metadata.Validation,
            RoleIntentModelPipeline.MetricsPair(rows, model.Predict(rows, metadata.ThresholdPolicy.Threshold), model.ScoreLabels));
        output.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, RoleIntentModelPipeline.JsonOptions) + "\n"));
        return report;
    }

    private static string PredictionHash(RoleIntentPredictions predictions) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(predictions, RoleIntentModelPipeline.JsonOptions)))).ToLowerInvariant();
    internal static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, RoleIntentModelPipeline.JsonOptions).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
}

public sealed record RoleIntentV3Selection(string Rule, bool SelectedV3, string RuntimeVersion, string Conclusion,
    string V2ModelSha256, string V3ModelSha256, double V2ValidationMacroF1, double V3ValidationMacroF1, double V3AcceptedAccuracy, double V3Threshold);
public sealed record RoleIntentV3EvaluationReport(string ModelVersion, string Provenance, string ModelSha256, string EvalSha256,
    RoleIntentV3Selection Selection, RoleIntentMetricsPair Train, RoleIntentMetricsPair Validation, RoleIntentMetricsPair Eval);
