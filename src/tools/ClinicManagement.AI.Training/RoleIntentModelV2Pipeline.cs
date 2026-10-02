using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;

namespace ClinicManagement.AI.Training;

/// <summary>Validation-only model selection; v1 artifacts and the application host remain separate.</summary>
public static class RoleIntentModelV2Pipeline
{
    public const string Version = "role-intent-mlnet-v2";
    public const string ModelFile = "role_intent_model_v2.zip";
    public const string MetadataFile = "role_intent_model_meta_v2.json";
    public const string ReportFile = "role_intent_eval_report_v2.json";
    public const double ProbabilityTolerance = 1e-6;
    public const string Provenance = "AI-written supplementary eval (Claude), not an independent human-written blind set. Eval was already seen in v1 and is no longer unseen data. V2 configuration and threshold use validation only; final v2 eval is scored once after freezing.";
    public const string RepetitionRule = "Same seed 20261002, one thread, identical input order, scalar CPU math. Both unfiltered and role-filtered validation top-1 labels must match exactly; maximum absolute per-label probability difference <= 1e-6. Byte-identical model ZIP is not required.";
    public const string SelectionRule = "Highest role-filtered validation top-1 macro-F1 among reproducible trials; exact ties prefer fewer normalization/weighting steps, fewer iterations, then ordinal trainer/name. Eval is not read by training.";

    public static RoleIntentV2Configuration[] Configurations() =>
        (from normalize in new[] { false, true }
         from weighted in new[] { false, true }
         from trainer in new[] { "lbfgs", "sdca" }
         from setting in new[] { (Iterations: 500, L1: 0f, L2: .01f), (Iterations: 1000, L1: 0f, L2: .001f), (Iterations: 1000, L1: .01f, L2: .001f) }
         select new RoleIntentV2Configuration($"{trainer}_{(normalize ? "l2" : "raw")}_{(weighted ? "weighted" : "plain")}_{setting.Iterations}_{(setting.L1 == 0 ? "zeroL1" : "smallL1")}",
             trainer, normalize, weighted, setting.Iterations, setting.L1, setting.L2)).ToArray();

    public static RoleIntentV2Metadata Train(string dataDirectory, string outputDirectory,
        IReadOnlyList<RoleIntentV2Configuration>? configurations = null)
    {
        if (File.Exists(Path.Combine(outputDirectory, ReportFile)))
            throw new InvalidOperationException("V2 evaluation already exists. Do not retrain or tune the frozen experiment.");
        var labels = RoleIntentDatasetGenerator.ReadLabels(dataDirectory).ToArray();
        var train = RoleIntentModelPipeline.ReadRecords(dataDirectory, RoleIntentDatasetGenerator.TrainFile, "train", labels);
        var validation = RoleIntentModelPipeline.ReadRecords(dataDirectory, RoleIntentDatasetGenerator.ValidationFile, "validation", labels);
        if (train.Select(x => x.SeedId).Intersect(validation.Select(x => x.SeedId)).Any())
            throw new InvalidDataException("Seed family crosses train/validation.");
        var candidates = configurations ?? Configurations();
        if (candidates.Count == 0 || candidates.Select(x => x.Name).Distinct().Count() != candidates.Count)
            throw new ArgumentException("Configurations must be nonempty and uniquely named.", nameof(configurations));
        var trials = new List<RoleIntentV2Trial>();
        RoleIntentFittedModel? selected = null;
        RoleIntentV2Trial? winner = null;
        foreach (var configuration in candidates)
        {
            var fitted = Fit(train, labels, configuration);
            var scores = fitted.Predict(validation, 0);
            var repeat = Fit(train, labels, configuration).Predict(validation, 0);
            var reproducibility = ComparePredictions(scores, repeat);
            var trainMetrics = RoleIntentModelPipeline.MetricsPair(train, fitted.Predict(train, 0), fitted.ScoreLabels);
            var validationMetrics = RoleIntentModelPipeline.MetricsPair(validation, scores, fitted.ScoreLabels);
            var trial = new RoleIntentV2Trial(configuration, trainMetrics.Unfiltered.Accuracy, trainMetrics.Filtered.Accuracy,
                validationMetrics.Unfiltered.Accuracy, validationMetrics.Filtered.Accuracy,
                validationMetrics.Unfiltered.MacroF1, validationMetrics.Filtered.MacroF1, reproducibility);
            trials.Add(trial);
            Console.WriteLine($"V2 trial {trials.Count}/{candidates.Count}: " + JsonSerializer.Serialize(trial, RoleIntentModelPipeline.JsonOptions));
            if (reproducibility.Passed && (winner is null || IsBetter(trial, winner)))
            {
                selected = fitted;
                winner = trial;
            }
        }
        if (selected is null || winner is null) throw new InvalidOperationException("No reproducible validation candidate.");
        var threshold = SelectThreshold(validation, selected.Predict(validation, 0).Filtered);
        var finalValidation = selected.Predict(validation, threshold.Threshold);
        Directory.CreateDirectory(outputDirectory);
        RoleIntentModelPipeline.SaveDeterministic(selected, Path.Combine(outputDirectory, ModelFile));
        var metadata = new RoleIntentV2Metadata(Version, RoleIntentModelPipeline.MlPackageVersion, RoleIntentModelPipeline.Seed,
            selected.ScoreLabels, labels, winner.Configuration, trials.ToArray(), SelectionRule, RepetitionRule, ProbabilityTolerance,
            threshold, "Reuse RoleIntentDatasetGenerator.Normalize; word 1-2 + character 2,3,4 grams; optional L2 at each text featurizer and on concatenated features.",
            "Maximum-entropy probabilities, globally normalized and renormalized after catalog role mask. Chung is union of six roles; unknown roles fail closed. Not calibrated on real users.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RoleIntentDatasetGenerator.TrainFile] = Hash(Path.Combine(dataDirectory, RoleIntentDatasetGenerator.TrainFile)),
                [RoleIntentDatasetGenerator.ValidationFile] = Hash(Path.Combine(dataDirectory, RoleIntentDatasetGenerator.ValidationFile)),
                [RoleIntentDatasetGenerator.LabelsFile] = Hash(Path.Combine(dataDirectory, RoleIntentDatasetGenerator.LabelsFile))
            }, Hash(Path.Combine(outputDirectory, ModelFile)), PredictionHash(finalValidation),
            RoleIntentModelPipeline.MetricsPair(train, selected.Predict(train, threshold.Threshold), selected.ScoreLabels),
            RoleIntentModelPipeline.MetricsPair(validation, finalValidation, selected.ScoreLabels));
        WriteJson(Path.Combine(outputDirectory, MetadataFile), metadata);
        return metadata;
    }

    private static bool IsBetter(RoleIntentV2Trial candidate, RoleIntentV2Trial winner)
    {
        if (candidate.ValidationMacroF1Filtered != winner.ValidationMacroF1Filtered)
            return candidate.ValidationMacroF1Filtered > winner.ValidationMacroF1Filtered;
        return new[] { candidate, winner }.OrderBy(x => (x.Configuration.NormalizeL2 ? 1 : 0) + (x.Configuration.ClassWeighted ? 1 : 0))
            .ThenBy(x => x.Configuration.MaximumIterations).ThenBy(x => x.Configuration.Trainer, StringComparer.Ordinal)
            .ThenBy(x => x.Configuration.Name, StringComparer.Ordinal).First() == candidate;
    }

    public static RoleIntentFittedModel Fit(IReadOnlyList<RoleIntentRecord> train, RoleIntentLabel[] labels, RoleIntentV2Configuration configuration)
    {
        if (Vector.IsHardwareAccelerated) throw new InvalidOperationException("Start role-intent training with scalar CPU math.");
        if (train.Count == 0 || train.Select(x => x.Label).Distinct().Count() != labels.Length)
            throw new InvalidDataException("Training must contain every catalog label.");
        if (configuration.MaximumIterations <= 0 || configuration.L1Regularization < 0 || configuration.L2Regularization <= 0)
            throw new ArgumentException("Invalid trainer parameters.", nameof(configuration));
        var counts = train.GroupBy(x => x.Label).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var ml = new MLContext(RoleIntentModelPipeline.Seed);
        var view = ml.Data.LoadFromEnumerable(train.Select(row => RoleIntentModelPipeline.Input(row,
            configuration.ClassWeighted ? (float)train.Count / (labels.Length * counts[row.Label]) : 1)).ToArray());
        var norm = configuration.NormalizeL2 ? TextFeaturizingEstimator.NormFunction.L2 : TextFeaturizingEstimator.NormFunction.None;
        IEstimator<ITransformer> pipeline = ml.Transforms.Text.FeaturizeText("WordFeatures", new TextFeaturizingEstimator.Options
        {
            WordFeatureExtractor = new WordBagEstimator.Options { NgramLength = 2, UseAllLengths = true },
            CharFeatureExtractor = null, KeepDiacritics = true, KeepPunctuations = false, Norm = norm
        }, nameof(RoleIntentModelInput.Text));
        foreach (var length in new[] { 2, 3, 4 })
            pipeline = pipeline.Append(ml.Transforms.Text.FeaturizeText($"Char{length}Features", new TextFeaturizingEstimator.Options
            {
                WordFeatureExtractor = null,
                CharFeatureExtractor = new WordBagEstimator.Options { NgramLength = length, UseAllLengths = false },
                KeepDiacritics = true, KeepPunctuations = false, Norm = norm
            }, nameof(RoleIntentModelInput.Text)));
        pipeline = pipeline.Append(ml.Transforms.Concatenate("CombinedFeatures", "WordFeatures", "Char2Features", "Char3Features", "Char4Features"));
        pipeline = configuration.NormalizeL2 ? pipeline.Append(ml.Transforms.NormalizeLpNorm("Features", "CombinedFeatures"))
            : pipeline.Append(ml.Transforms.CopyColumns("Features", "CombinedFeatures"));
        pipeline = pipeline.Append(ml.Transforms.Conversion.MapValueToKey("KeyLabel", nameof(RoleIntentModelInput.Label),
            keyOrdinality: ValueToKeyMappingEstimator.KeyOrdinality.ByValue));
        var weightColumn = configuration.ClassWeighted ? nameof(RoleIntentModelInput.Weight) : null;
        pipeline = pipeline.Append(configuration.Trainer switch
        {
            "sdca" => (IEstimator<ITransformer>)ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(new SdcaMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "KeyLabel", FeatureColumnName = "Features", ExampleWeightColumnName = weightColumn,
                NumberOfThreads = 1, Shuffle = false, MaximumNumberOfIterations = configuration.MaximumIterations,
                L1Regularization = configuration.L1Regularization, L2Regularization = configuration.L2Regularization,
                ConvergenceCheckFrequency = 0
            }),
            "lbfgs" => ml.MulticlassClassification.Trainers.LbfgsMaximumEntropy(new LbfgsMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "KeyLabel", FeatureColumnName = "Features", ExampleWeightColumnName = weightColumn,
                NumberOfThreads = 1, MaximumNumberOfIterations = configuration.MaximumIterations,
                L1Regularization = configuration.L1Regularization, L2Regularization = configuration.L2Regularization
            }),
            _ => throw new ArgumentException("Unknown trainer.", nameof(configuration))
        }).Append(ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var transformer = pipeline.Fit(view);
        return new(ml, transformer, view.Schema, RoleIntentModelPipeline.ScoreLabels(transformer.Transform(view)), labels);
    }

    public static RoleIntentV2Reproducibility ComparePredictions(RoleIntentPredictions first, RoleIntentPredictions second)
    {
        var labelsMatch = first.Unfiltered.Length == second.Unfiltered.Length && first.Filtered.Length == second.Filtered.Length;
        var maximum = 0d;
        foreach (var (left, right) in first.Unfiltered.Zip(second.Unfiltered).Concat(first.Filtered.Zip(second.Filtered)))
        {
            labelsMatch &= left.TopLabel == right.TopLabel && left.DecisionLabel == right.DecisionLabel && left.IsUncertain == right.IsUncertain;
            if (!left.Probabilities.Keys.SequenceEqual(right.Probabilities.Keys, StringComparer.Ordinal))
                return new(false, false, double.MaxValue, ProbabilityTolerance);
            foreach (var label in left.Probabilities.Keys)
                maximum = Math.Max(maximum, Math.Abs(left.Probabilities[label] - right.Probabilities[label]));
        }
        return new(labelsMatch && maximum <= ProbabilityTolerance, labelsMatch, maximum, ProbabilityTolerance);
    }

    public static RoleIntentThresholdPolicy SelectThreshold(IReadOnlyList<RoleIntentRecord> expected, RoleIntentPrediction[] predictions)
    {
        if (expected.Count == 0 || expected.Count != predictions.Length) throw new InvalidDataException("Invalid threshold denominator.");
        var trials = Enumerable.Range(0, 20).Select(index =>
        {
            var threshold = Math.Round(index * .05, 2);
            var accepted = predictions.Select((prediction, row) => (prediction, row)).Where(x => x.prediction.Confidence >= threshold).ToArray();
            return new RoleIntentThresholdTrial(threshold, accepted.Length,
                accepted.Length == 0 ? 0 : (double)accepted.Count(x => x.prediction.TopLabel == expected[x.row].Label) / accepted.Length,
                (double)accepted.Length / expected.Count);
        }).ToArray();
        var valid = trials.Where(x => x.Accepted > 0 && x.AcceptedAccuracy >= .9).OrderByDescending(x => x.Coverage).ThenBy(x => x.Threshold).FirstOrDefault();
        var chosen = valid ?? trials.Where(x => x.Accepted > 0).OrderByDescending(x => x.AcceptedAccuracy)
            .ThenByDescending(x => x.Coverage).ThenBy(x => x.Threshold).First();
        return new(chosen.Threshold, .9, 1, trials, valid is null
            ? "No nonempty fixed-grid threshold reached 90% accepted accuracy; choose highest accepted accuracy, then coverage, then lower threshold."
            : "Highest coverage among nonempty fixed 0.00-0.95 (step 0.05) thresholds with accepted accuracy >=90%; tie chooses lower threshold. No additional minimum-support restriction; report accepted count explicitly.");
    }

    public static RoleIntentFittedModel Load(string directory, out RoleIntentV2Metadata metadata)
    {
        metadata = RoleIntentModelPipeline.Read<RoleIntentV2Metadata>(Path.Combine(directory, MetadataFile));
        if (metadata.ModelVersion != Version || Hash(Path.Combine(directory, ModelFile)) != metadata.ModelSha256)
            throw new InvalidDataException("V2 artifact/metadata checksum mismatch.");
        var ml = new MLContext(metadata.Seed);
        var transformer = ml.Model.Load(Path.Combine(directory, ModelFile), out var schema);
        var probe = ml.Data.LoadFromEnumerable(new[] { new RoleIntentModelInput() });
        var scoreLabels = RoleIntentModelPipeline.ScoreLabels(transformer.Transform(probe));
        if (!scoreLabels.SequenceEqual(metadata.ScoreLabels, StringComparer.Ordinal)) throw new InvalidDataException("V2 score labels mismatch.");
        return new(ml, transformer, schema, scoreLabels, metadata.LabelCatalog);
    }

    public static RoleIntentV2EvaluationReport EvaluateOnce(string dataDirectory, string outputDirectory, string v1Directory)
    {
        var path = Path.Combine(outputDirectory, ReportFile);
        if (File.Exists(path)) throw new InvalidOperationException("Final V2 eval was already scored. Do not rescore.");
        var model = Load(outputDirectory, out var metadata);
        foreach (var (file, hash) in metadata.DataSha256)
            if (Hash(Path.Combine(dataDirectory, file)) != hash) throw new InvalidDataException($"Frozen input changed: {file}");
        var evalPath = Path.Combine(dataDirectory, RoleIntentModelPipeline.EvalFile);
        if (Hash(evalPath) != RoleIntentModelPipeline.EvalSha256) throw new InvalidDataException("Eval checksum mismatch.");
        // Read the historical report only here, after v2 selection is frozen, never inside Train.
        RoleIntentModelPipeline.Load(v1Directory, out var v1Metadata);
        var v1 = RoleIntentModelPipeline.Read<RoleIntentEvaluationReport>(Path.Combine(v1Directory, RoleIntentModelPipeline.ReportFile));
        if (v1.ModelSha256 != v1Metadata.ModelSha256 || v1.EvalSha256 != RoleIntentModelPipeline.EvalSha256)
            throw new InvalidDataException("Historical V1 report mismatch.");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var lines = File.ReadAllLines(evalPath, Encoding.UTF8);
        if (lines.Length != 241 || lines[0] != "vai_tro\tcau\tnhan\tnguon") throw new InvalidDataException("Unexpected eval header/count.");
        var eval = lines.Skip(1).Select((line, index) =>
        {
            var columns = line.Split('\t');
            if (columns.Length != 4) throw new InvalidDataException("Invalid eval row.");
            return new RoleIntentRecord($"RI-EVAL-{index + 1:D3}", "", columns[0], columns[2], columns[1], "eval", "original", columns[3]);
        }).ToArray();
        var predictions = model.Predict(eval, metadata.ThresholdPolicy.Threshold);
        var metrics = RoleIntentModelPipeline.MetricsPair(eval, predictions, model.ScoreLabels);
        var labelComparison = metadata.ScoreLabels.Select(label => new RoleIntentV2ValidationLabelComparison(label,
            v1.Validation.Unfiltered.PerLabel.Single(x => x.Label == label).F1, v1.Validation.Filtered.PerLabel.Single(x => x.Label == label).F1,
            metadata.Validation.Unfiltered.PerLabel.Single(x => x.Label == label).F1, metadata.Validation.Filtered.PerLabel.Single(x => x.Label == label).F1)).ToArray();
        var report = new RoleIntentV2EvaluationReport(Version, Provenance, RoleIntentModelPipeline.EvalSha256, metadata.ModelSha256,
            metadata.Configuration, metadata.ThresholdPolicy, metadata.Train, metadata.Validation, metrics,
            RoleIntentModelPipeline.Errors(eval, predictions.Unfiltered), RoleIntentModelPipeline.Errors(eval, predictions.Filtered),
            "Top-1 accuracy/P/R/F1/confusions use all rows before abstention; accepted accuracy and uncertainty are separate. No runtime fallback is executed.",
            new[] { new RoleIntentV2Comparison("v1", v1.Train, v1.Validation, v1.Eval), new RoleIntentV2Comparison("v2", metadata.Train, metadata.Validation, metrics) }, labelComparison);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, RoleIntentModelPipeline.JsonOptions).Replace("\r\n", "\n") + "\n");
        output.Write(bytes);
        return report;
    }

    private static string Hash(string path) => RoleIntentDatasetGenerator.FileSha256(path);
    private static string PredictionHash(RoleIntentPredictions predictions) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(predictions, RoleIntentModelPipeline.JsonOptions)))).ToLowerInvariant();
    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path,
        JsonSerializer.Serialize(value, RoleIntentModelPipeline.JsonOptions).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
}

public sealed record RoleIntentV2Configuration(string Name, string Trainer, bool NormalizeL2, bool ClassWeighted,
    int MaximumIterations, float L1Regularization, float L2Regularization);
public sealed record RoleIntentV2Reproducibility(bool Passed, bool LabelsMatchExactly, double MaximumProbabilityDifference, double Tolerance);
public sealed record RoleIntentV2Trial(RoleIntentV2Configuration Configuration, double TrainAccuracyUnfiltered, double TrainAccuracyFiltered,
    double ValidationAccuracyUnfiltered, double ValidationAccuracyFiltered, double ValidationMacroF1Unfiltered,
    double ValidationMacroF1Filtered, RoleIntentV2Reproducibility Reproducibility);
public sealed record RoleIntentV2Metadata(string ModelVersion, string MicrosoftMlPackageVersion, int Seed, string[] ScoreLabels,
    RoleIntentLabel[] LabelCatalog, RoleIntentV2Configuration Configuration, RoleIntentV2Trial[] ConfigurationTrials, string SelectionRule,
    string RepetitionRule, double ProbabilityTolerance, RoleIntentThresholdPolicy ThresholdPolicy, string Features, string ScoreSemantics,
    Dictionary<string, string> DataSha256, string ModelSha256, string ValidationPredictionSha256,
    RoleIntentMetricsPair Train, RoleIntentMetricsPair Validation);
public sealed record RoleIntentV2Comparison(string Version, RoleIntentMetricsPair Train, RoleIntentMetricsPair Validation, RoleIntentMetricsPair Eval);
public sealed record RoleIntentV2ValidationLabelComparison(string Label, double V1UnfilteredF1, double V1FilteredF1, double V2UnfilteredF1, double V2FilteredF1);
public sealed record RoleIntentV2EvaluationReport(string ModelVersion, string Provenance, string EvalSha256, string ModelSha256,
    RoleIntentV2Configuration Configuration, RoleIntentThresholdPolicy ThresholdPolicy, RoleIntentMetricsPair Train,
    RoleIntentMetricsPair Validation, RoleIntentMetricsPair Eval, RoleIntentError[] UnfilteredErrors, RoleIntentError[] FilteredErrors,
    string MetricSemantics, RoleIntentV2Comparison[] Comparison, RoleIntentV2ValidationLabelComparison[] ValidationLabelComparison);
