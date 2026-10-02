using System.IO.Compression;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;

namespace ClinicManagement.AI.Training;

/// <summary>Offline experiment only. No runtime classifier, provider, or API dependency.</summary>
public static class RoleIntentModelPipeline
{
    public const int Seed = 20261002;
    public const string Version = "role-intent-mlnet-v1";
    public const string MlPackageVersion = "4.0.3";
    public const string ModelFile = "role_intent_model_v1.zip";
    public const string MetadataFile = "role_intent_model_meta_v1.json";
    public const string ReportFile = "role_intent_eval_report_v1.json";
    public const string EvalFile = "role_intent_eval_ai_v1.tsv";
    public const string EvalSha256 = "4104b49e57d2f82758602faa63c255cdddf5a6ec2e895d045c27e04382dd4686";
    public const string EvalProvenance = "AI-written supplementary evaluation set (Claude), not an independent human-written blind set. Used once after configuration and threshold were frozen; never used for training or model selection.";
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // SIMD reductions may vary with memory alignment. Re-execute only these offline
    // commands with scalar CPU math before ML.NET is used; do not change the API host.
    public static int? RunScalarCommand(string[] args)
    {
        if (!Vector.IsHardwareAccelerated) return null;
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate training process.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(RoleIntentModelPipeline).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_EnableHWIntrinsic"] = "0";
        start.Environment["DOTNET_TieredCompilation"] = "0";
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start scalar training process.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        child.WaitForExit();
        Console.Out.Write(stdout.GetAwaiter().GetResult());
        Console.Error.Write(stderr.GetAwaiter().GetResult());
        return child.ExitCode;
    }

    public static RoleIntentModelMetadata Train(string dataDirectory, string outputDirectory)
    {
        // A scored final configuration cannot be overwritten and tuned against the same eval.
        if (File.Exists(Path.Combine(outputDirectory, ReportFile)))
            throw new InvalidOperationException("Final evaluation already exists. Do not retrain or tune this frozen experiment.");
        var labels = RoleIntentDatasetGenerator.ReadLabels(dataDirectory).ToArray();
        var train = ReadRecords(dataDirectory, RoleIntentDatasetGenerator.TrainFile, "train", labels);
        var validation = ReadRecords(dataDirectory, RoleIntentDatasetGenerator.ValidationFile, "validation", labels);
        if (train.Select(x => x.SeedId).Intersect(validation.Select(x => x.SeedId)).Any())
            throw new InvalidDataException("Seed family crosses train/validation.");
        var configurations = new[]
        {
            new RoleIntentConfiguration("lbfgs_unweighted", false), new RoleIntentConfiguration("lbfgs_class_weighted", true)
        };
        var trials = new List<RoleIntentTrial>();
        RoleIntentFittedModel? selected = null;
        RoleIntentConfiguration? selectedConfiguration = null;
        var best = double.NegativeInfinity;
        foreach (var configuration in configurations)
        {
            var fitted = Fit(train, labels, configuration);
            var scores = fitted.Predict(validation, threshold: 0);
            var filtered = Measure(validation, scores.Filtered, fitted.ScoreLabels);
            var unfiltered = Measure(validation, scores.Unfiltered, fitted.ScoreLabels);
            trials.Add(new(configuration, unfiltered, filtered));
            // Fixed order breaks exact ties in favor of the simpler unweighted model.
            if (filtered.MacroF1 > best)
            {
                best = filtered.MacroF1;
                selected = fitted;
                selectedConfiguration = configuration;
            }
        }
        var model = selected!;
        var validationScores = model.Predict(validation, 0);
        var policy = SelectThreshold(validation, validationScores.Filtered);
        var trainScores = model.Predict(train, policy.Threshold);
        var finalValidation = model.Predict(validation, policy.Threshold);
        Directory.CreateDirectory(outputDirectory);
        SaveDeterministic(model, Path.Combine(outputDirectory, ModelFile));
        var metadata = new RoleIntentModelMetadata(
            Version, MlPackageVersion, Seed, model.ScoreLabels, labels, selectedConfiguration!, trials.ToArray(),
            "Maximum role-filtered top-1 validation macro-F1 among reproducible L-BFGS trials; ties prefer unweighted. SDCA was rejected during development because exact repeated artifacts/probabilities differed. Eval is not read by training.",
            policy, "RoleIntentDatasetGenerator.Normalize; word 1-2 grams + separate character 2,3,4 grams; concatenated L2 features",
            "Maximum-entropy Score probabilities, renormalized globally and after role masking; not calibrated on real users. Offline role-intent commands/test host use scalar CPU math (DOTNET_EnableHWIntrinsic=0) and disable tiered JIT for exact repetition on the same environment.",
            "Chung means union of all six roles (all labels); Vietnamese role codes map to catalog roles. Unknown roles fail closed.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RoleIntentDatasetGenerator.TrainFile] = RoleIntentDatasetGenerator.FileSha256(Path.Combine(dataDirectory, RoleIntentDatasetGenerator.TrainFile)),
                [RoleIntentDatasetGenerator.ValidationFile] = RoleIntentDatasetGenerator.FileSha256(Path.Combine(dataDirectory, RoleIntentDatasetGenerator.ValidationFile)),
                [RoleIntentDatasetGenerator.LabelsFile] = RoleIntentDatasetGenerator.FileSha256(Path.Combine(dataDirectory, RoleIntentDatasetGenerator.LabelsFile))
            },
            RoleIntentDatasetGenerator.FileSha256(Path.Combine(outputDirectory, ModelFile)),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(finalValidation, JsonOptions)))).ToLowerInvariant(),
            MetricsPair(train, trainScores, model.ScoreLabels), MetricsPair(validation, finalValidation, model.ScoreLabels));
        WriteJson(Path.Combine(outputDirectory, MetadataFile), metadata);
        return metadata;
    }

    public static RoleIntentFittedModel Fit(IReadOnlyList<RoleIntentRecord> train, RoleIntentLabel[] labels, RoleIntentConfiguration configuration)
    {
        if (Vector.IsHardwareAccelerated)
            throw new InvalidOperationException("Role-intent training requires scalar CPU math. Use --train-role-intent or start the host with DOTNET_EnableHWIntrinsic=0.");
        if (train.Count == 0 || train.Select(x => x.Label).Distinct().Count() != labels.Length)
            throw new InvalidDataException("Training must contain every catalog label.");
        var counts = train.GroupBy(x => x.Label).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var inputs = train.Select(row => Input(row, configuration.ClassWeighted ? (float)train.Count / (labels.Length * counts[row.Label]) : 1)).ToArray();
        var ml = new MLContext(Seed);
        var view = ml.Data.LoadFromEnumerable(inputs);
        IEstimator<ITransformer> pipeline = ml.Transforms.Text.FeaturizeText("WordFeatures", new TextFeaturizingEstimator.Options
        {
            WordFeatureExtractor = new WordBagEstimator.Options { NgramLength = 2, UseAllLengths = true },
            CharFeatureExtractor = null, KeepDiacritics = true, KeepPunctuations = false
        }, nameof(RoleIntentModelInput.Text));
        // UseAllLengths would include character unigrams. Separate extractors give exactly 2-4.
        foreach (var length in new[] { 2, 3, 4 })
            pipeline = pipeline.Append(ml.Transforms.Text.FeaturizeText($"Char{length}Features", new TextFeaturizingEstimator.Options
            {
                WordFeatureExtractor = null,
                CharFeatureExtractor = new WordBagEstimator.Options { NgramLength = length, UseAllLengths = false },
                KeepDiacritics = true, KeepPunctuations = false
            }, nameof(RoleIntentModelInput.Text)));
        pipeline = pipeline.Append(ml.Transforms.Concatenate("CombinedFeatures", "WordFeatures", "Char2Features", "Char3Features", "Char4Features"))
            .Append(ml.Transforms.NormalizeLpNorm("Features", "CombinedFeatures"))
            .Append(ml.Transforms.Conversion.MapValueToKey("KeyLabel", nameof(RoleIntentModelInput.Label), keyOrdinality: ValueToKeyMappingEstimator.KeyOrdinality.ByValue))
            .Append(ml.MulticlassClassification.Trainers.LbfgsMaximumEntropy(new LbfgsMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "KeyLabel", FeatureColumnName = "Features",
                ExampleWeightColumnName = configuration.ClassWeighted ? nameof(RoleIntentModelInput.Weight) : null,
                NumberOfThreads = 1, MaximumNumberOfIterations = configuration.MaximumIterations
            }))
            .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var transformer = pipeline.Fit(view);
        var scoreLabels = ScoreLabels(transformer.Transform(view));
        return new(ml, transformer, view.Schema, scoreLabels, labels);
    }

    public static RoleIntentFittedModel Load(string outputDirectory, out RoleIntentModelMetadata metadata)
    {
        metadata = Read<RoleIntentModelMetadata>(Path.Combine(outputDirectory, MetadataFile));
        var path = Path.Combine(outputDirectory, ModelFile);
        if (RoleIntentDatasetGenerator.FileSha256(path) != metadata.ModelSha256)
            throw new InvalidDataException("Role intent artifact/metadata checksum mismatch.");
        var ml = new MLContext(metadata.Seed);
        var transformer = ml.Model.Load(path, out var schema);
        var probe = ml.Data.LoadFromEnumerable(new[] { new RoleIntentModelInput() });
        var scoreLabels = ScoreLabels(transformer.Transform(probe));
        if (!scoreLabels.SequenceEqual(metadata.ScoreLabels, StringComparer.Ordinal))
            throw new InvalidDataException("Model score labels do not match metadata.");
        return new(ml, transformer, schema, scoreLabels, metadata.LabelCatalog);
    }

    public static RoleIntentEvaluationReport EvaluateOnce(string dataDirectory, string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, ReportFile);
        if (File.Exists(path)) throw new InvalidOperationException("This final configuration has already been evaluated. Do not evaluate it again.");
        var fitted = Load(outputDirectory, out var metadata);
        foreach (var (file, hash) in metadata.DataSha256)
            if (RoleIntentDatasetGenerator.FileSha256(Path.Combine(dataDirectory, file)) != hash)
                throw new InvalidDataException($"Frozen training input changed: {file}");
        var evalPath = Path.Combine(dataDirectory, EvalFile);
        if (RoleIntentDatasetGenerator.FileSha256(evalPath) != EvalSha256)
            throw new InvalidDataException("Frozen eval checksum mismatch.");
        // Reserve the report before reading/scoring eval: concurrent/repeated commands fail closed.
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var lines = File.ReadAllLines(evalPath, Encoding.UTF8);
        if (lines[0] != "vai_tro\tcau\tnhan\tnguon" || lines.Length != 241) throw new InvalidDataException("Unexpected eval header/count.");
        var eval = lines.Skip(1).Select((line, index) =>
        {
            var columns = line.Split('\t');
            if (columns.Length != 4) throw new InvalidDataException("Invalid eval row.");
            return new RoleIntentRecord($"RI-EVAL-{index + 1:D3}", "", columns[0], columns[2], columns[1], "eval", "original", columns[3]);
        }).ToArray();
        var predictions = fitted.Predict(eval, metadata.ThresholdPolicy.Threshold);
        var report = new RoleIntentEvaluationReport(Version, EvalProvenance, EvalSha256, metadata.ModelSha256,
            metadata.Configuration, metadata.ThresholdPolicy, metadata.Train, metadata.Validation,
            MetricsPair(eval, predictions, fitted.ScoreLabels),
            Errors(eval, predictions.Unfiltered), Errors(eval, predictions.Filtered),
            "All accuracy/P/R/F1/confusion metrics use top-1 labels before abstention. Below-threshold decisions are reported separately; no runtime fallback is executed.");
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, JsonOptions).Replace("\r\n", "\n") + "\n");
        output.Write(bytes);
        return report;
    }

    public static RoleIntentThresholdPolicy SelectThreshold(IReadOnlyList<RoleIntentRecord> expected, IReadOnlyList<RoleIntentPrediction> predictions)
    {
        const double target = .90;
        var minimum = Math.Min(expected.Count, Math.Max(20, (int)Math.Ceiling(expected.Count * .10)));
        var trials = Enumerable.Range(0, 20).Select(index =>
        {
            var threshold = index * .05;
            var accepted = predictions.Select((prediction, row) => (prediction, row)).Where(x => x.prediction.Confidence >= threshold).ToArray();
            var correct = accepted.Count(x => x.prediction.TopLabel == expected[x.row].Label);
            return new RoleIntentThresholdTrial(threshold, accepted.Length, accepted.Length == 0 ? 0 : (double)correct / accepted.Length,
                (double)accepted.Length / expected.Count);
        }).ToArray();
        var chosen = trials.Where(x => x.Accepted >= minimum && x.AcceptedAccuracy >= target)
            .OrderByDescending(x => x.Coverage).ThenBy(x => x.Threshold).FirstOrDefault();
        return new(chosen?.Threshold ?? 0, target, minimum, trials,
            chosen is null ? "No grid threshold met 90% accepted accuracy with minimum support on role-filtered validation; threshold 0 retains coverage without claiming reliable confidence."
                : "Among fixed 0.00-0.95 (step 0.05) grid thresholds meeting 90% accepted accuracy and minimum support on role-filtered validation, maximize coverage; tie chooses lower threshold.");
    }

    public static RoleIntentMetrics Measure(IReadOnlyList<RoleIntentRecord> expected, IReadOnlyList<RoleIntentPrediction> predictions, string[] labels)
    {
        if (expected.Count == 0 || predictions.Count != expected.Count) throw new InvalidDataException("Invalid scoring denominator.");
        var index = labels.Select((label, position) => (label, position)).ToDictionary(x => x.label, x => x.position, StringComparer.Ordinal);
        var matrix = labels.Select(_ => new int[labels.Length]).ToArray();
        for (var row = 0; row < expected.Count; row++) matrix[index[expected[row].Label]][index[predictions[row].TopLabel]]++;
        var metrics = labels.Select((label, position) =>
        {
            var tp = matrix[position][position];
            var actual = matrix[position].Sum();
            var predicted = matrix.Sum(values => values[position]);
            var precision = predicted == 0 ? 0 : (double)tp / predicted;
            var recall = actual == 0 ? 0 : (double)tp / actual;
            return new RoleIntentClassMetric(label, actual, precision, recall, precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall));
        }).ToArray();
        var confusions = labels.SelectMany((actual, a) => labels.Select((predicted, p) => new RoleIntentConfusion(actual, predicted, matrix[a][p])))
            .Where(x => x.Actual != x.Predicted && x.Count > 0).OrderByDescending(x => x.Count).ThenBy(x => x.Actual, StringComparer.Ordinal).ThenBy(x => x.Predicted, StringComparer.Ordinal).ToArray();
        var accepted = predictions.Select((prediction, row) => (prediction, row)).Where(x => !x.prediction.IsUncertain).ToArray();
        var acceptedCorrect = accepted.Count(x => x.prediction.TopLabel == expected[x.row].Label);
        return new(expected.Count, (double)matrix.Select((values, position) => values[position]).Sum() / expected.Count,
            metrics.Average(x => x.F1), (double)(expected.Count - accepted.Length) / expected.Count,
            accepted.Length, accepted.Length == 0 ? 0 : (double)acceptedCorrect / accepted.Length, (double)acceptedCorrect / expected.Count,
            metrics, labels, matrix, confusions.Take(10).ToArray(),
            new("ActionRequest", "PrescriptionPayment", matrix[index["ActionRequest"]][index["PrescriptionPayment"]]),
            new("PrescriptionPayment", "ActionRequest", matrix[index["PrescriptionPayment"]][index["ActionRequest"]]));
    }

    internal static RoleIntentModelInput Input(RoleIntentRecord row, float weight = 1) => new()
    {
        Text = RoleIntentDatasetGenerator.Normalize(row.Text), Label = row.Label, Weight = weight
    };
    private static RoleIntentRecord[] ReadRecords(string directory, string file, string split, RoleIntentLabel[] labels)
    {
        var rows = Read<RoleIntentRecord[]>(Path.Combine(directory, file));
        if (rows.Length == 0 || rows.Any(row => row.Split != split || !labels.Any(label => label.Label == row.Label) || string.IsNullOrWhiteSpace(row.Text)))
            throw new InvalidDataException($"Invalid {split} dataset.");
        return rows;
    }
    internal static string[] ScoreLabels(IDataView view)
    {
        VBuffer<ReadOnlyMemory<char>> slots = default;
        view.Schema["Score"].Annotations.GetValue("SlotNames", ref slots);
        return slots.DenseValues().Select(x => x.ToString()).ToArray();
    }
    private static RoleIntentMetricsPair MetricsPair(RoleIntentRecord[] rows, RoleIntentPredictions predictions, string[] labels) =>
        new(Measure(rows, predictions.Unfiltered, labels), Measure(rows, predictions.Filtered, labels));
    private static RoleIntentError[] Errors(RoleIntentRecord[] rows, RoleIntentPrediction[] predictions) =>
        rows.Select((row, index) => new RoleIntentError(row.Id, row.Role, row.Text, row.Label, predictions[index].TopLabel,
            predictions[index].DecisionLabel, predictions[index].Confidence, predictions[index].IsUncertain))
            .Where(x => x.Actual != x.Predicted).ToArray();
    private static void SaveDeterministic(RoleIntentFittedModel fitted, string path)
    {
        using var buffer = new MemoryStream();
        fitted.Context.Model.Save(fitted.Transformer, fitted.InputSchema, buffer);
        buffer.Position = 0;
        using var source = new ZipArchive(buffer, ZipArchiveMode.Read);
        using var target = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        foreach (var entry in source.Entries.OrderBy(x => x.FullName, StringComparer.Ordinal))
        {
            var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            copy.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var input = entry.Open();
            using var output = copy.Open();
            input.CopyTo(output);
        }
    }
    internal static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"Invalid JSON: {path}");
    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
}

public sealed class RoleIntentFittedModel(MLContext context, ITransformer transformer, DataViewSchema inputSchema, string[] scoreLabels, RoleIntentLabel[] labels)
{
    internal MLContext Context { get; } = context;
    internal ITransformer Transformer { get; } = transformer;
    internal DataViewSchema InputSchema { get; } = inputSchema;
    public string[] ScoreLabels { get; } = scoreLabels;

    public RoleIntentPredictions Predict(IReadOnlyList<RoleIntentRecord> rows, double threshold)
    {
        var view = Context.Data.LoadFromEnumerable(rows.Select(row => RoleIntentModelPipeline.Input(row)));
        var scores = Context.Data.CreateEnumerable<RoleIntentModelOutput>(Transformer.Transform(view), reuseRowObject: false).ToArray();
        return new(scores.Select(score => Decide(score.Score, null, threshold)).ToArray(),
            scores.Select((score, index) => Decide(score.Score, rows[index].Role, threshold)).ToArray());
    }

    public RoleIntentPrediction Decide(float[] scores, string? role, double threshold)
    {
        if (scores.Length != ScoreLabels.Length || scores.Any(x => !float.IsFinite(x) || x < 0) || scores.Sum(x => (double)x) <= 0)
            throw new InvalidDataException("Invalid maximum-entropy probability vector.");
        if (role is not null && role != "Chung")
        {
            role = RoleIntentDatasetGenerator.RoleCodes.GetValueOrDefault(role, role);
            if (!labels.Any(label => label.Roles.Contains(role, StringComparer.Ordinal))) throw new ArgumentException("Unknown actor role.", nameof(role));
        }
        var allowed = labels.Where(label => role is null || role == "Chung" || label.Roles.Contains(role, StringComparer.Ordinal))
            .Select(label => label.Label).ToHashSet(StringComparer.Ordinal);
        var probabilities = ScoreLabels.Select((label, position) => (label, score: (double)scores[position]))
            .Where(x => allowed.Contains(x.label)).ToDictionary(x => x.label, x => x.score, StringComparer.Ordinal);
        var total = probabilities.Values.Sum();
        if (total <= 0) throw new InvalidDataException("No probability mass for authorized labels.");
        foreach (var label in probabilities.Keys.ToArray()) probabilities[label] /= total;
        var top = probabilities.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First();
        var uncertain = top.Value < threshold;
        return new(top.Key, uncertain ? "không chắc" : top.Key, top.Value, uncertain, probabilities);
    }
}

public sealed class RoleIntentModelInput
{
    public string Text { get; set; } = "";
    public string Label { get; set; } = "";
    public float Weight { get; set; } = 1;
}
public sealed class RoleIntentModelOutput { public float[] Score { get; set; } = []; }
public sealed record RoleIntentConfiguration(string Name, bool ClassWeighted, int MaximumIterations = 100);
public sealed record RoleIntentPrediction(string TopLabel, string DecisionLabel, double Confidence, bool IsUncertain, Dictionary<string, double> Probabilities);
public sealed record RoleIntentPredictions(RoleIntentPrediction[] Unfiltered, RoleIntentPrediction[] Filtered);
public sealed record RoleIntentClassMetric(string Label, int Support, double Precision, double Recall, double F1);
public sealed record RoleIntentConfusion(string Actual, string Predicted, int Count);
public sealed record RoleIntentMetrics(int Samples, double Accuracy, double MacroF1, double UncertainRate, int Accepted,
    double AcceptedAccuracy, double DecisionAccuracy, RoleIntentClassMetric[] PerLabel, string[] ConfusionLabels, int[][] ConfusionMatrix,
    RoleIntentConfusion[] TopConfusions, RoleIntentConfusion ActionRequestToPrescriptionPayment, RoleIntentConfusion PrescriptionPaymentToActionRequest);
public sealed record RoleIntentMetricsPair(RoleIntentMetrics Unfiltered, RoleIntentMetrics Filtered);
public sealed record RoleIntentTrial(RoleIntentConfiguration Configuration, RoleIntentMetrics ValidationUnfiltered, RoleIntentMetrics ValidationFiltered);
public sealed record RoleIntentThresholdTrial(double Threshold, int Accepted, double AcceptedAccuracy, double Coverage);
public sealed record RoleIntentThresholdPolicy(double Threshold, double TargetAcceptedAccuracy, int MinimumAccepted,
    RoleIntentThresholdTrial[] Trials, string Reason);
public sealed record RoleIntentModelMetadata(string ModelVersion, string MicrosoftMlPackageVersion, int Seed, string[] ScoreLabels,
    RoleIntentLabel[] LabelCatalog, RoleIntentConfiguration Configuration, RoleIntentTrial[] ConfigurationTrials, string SelectionRule,
    RoleIntentThresholdPolicy ThresholdPolicy, string Features, string ScoreSemantics, string RoleSemantics,
    Dictionary<string, string> DataSha256, string ModelSha256, string ValidationPredictionSha256,
    RoleIntentMetricsPair Train, RoleIntentMetricsPair Validation);
public sealed record RoleIntentError(string Id, string Role, string Text, string Actual, string Predicted, string Decision, double Confidence, bool IsUncertain);
public sealed record RoleIntentEvaluationReport(string ModelVersion, string Provenance, string EvalSha256, string ModelSha256,
    RoleIntentConfiguration Configuration, RoleIntentThresholdPolicy ThresholdPolicy, RoleIntentMetricsPair Train,
    RoleIntentMetricsPair Validation, RoleIntentMetricsPair Eval, RoleIntentError[] UnfilteredErrors, RoleIntentError[] FilteredErrors, string MetricSemantics);
