using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Infrastructure.AI;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicManagement.AI.Training;

public class IntentInput
{
    public string Text { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class IntentOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}

public class IntentEvaluationResult
{
    public string Text { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string PredictedLabel { get; set; } = string.Empty;
    public float[] Score { get; set; } = Array.Empty<float>();
}

public class LatencyStatsDto
{
    [JsonPropertyName("iterations")]
    public int Iterations { get; set; }

    [JsonPropertyName("warmupIterations")]
    public int WarmupIterations { get; set; }

    [JsonPropertyName("minMs")]
    public double MinMs { get; set; }

    [JsonPropertyName("maxMs")]
    public double MaxMs { get; set; }

    [JsonPropertyName("avgMs")]
    public double AvgMs { get; set; }

    [JsonPropertyName("p50Ms")]
    public double P50Ms { get; set; }

    [JsonPropertyName("p95Ms")]
    public double P95Ms { get; set; }

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;
}

public class ConfusionMatrixDto
{
    [JsonPropertyName("classes")]
    public List<string> Classes { get; set; } = new();

    [JsonPropertyName("matrix")]
    public List<List<int>> Matrix { get; set; } = new();
}

public class IntentBenchmarkResult
{
    [JsonPropertyName("totalTrainSamples")]
    public int TotalTrainSamples { get; set; }

    [JsonPropertyName("totalValSamples")]
    public int TotalValSamples { get; set; }

    [JsonPropertyName("totalTestSamples")]
    public int TotalTestSamples { get; set; }

    [JsonPropertyName("optimalThreshold")]
    public double OptimalThreshold { get; set; }

    [JsonPropertyName("valAccuracyAtOptimalThreshold")]
    public double ValAccuracyAtOptimalThreshold { get; set; }

    [JsonPropertyName("ruleBasedCorrect")]
    public int RuleBasedCorrect { get; set; }

    [JsonPropertyName("ruleBasedAccuracy")]
    public double RuleBasedAccuracy { get; set; }

    [JsonPropertyName("mlNetOnlyCorrect")]
    public int MlNetOnlyCorrect { get; set; }

    [JsonPropertyName("mlNetOnlyAccuracy")]
    public double MlNetOnlyAccuracy { get; set; }

    [JsonPropertyName("hybridCorrect")]
    public int HybridCorrect { get; set; }

    [JsonPropertyName("hybridAccuracy")]
    public double HybridAccuracy { get; set; }

    [JsonPropertyName("mlNetMetrics")]
    public EvaluationMetricsDto MlNetMetrics { get; set; } = new();

    [JsonPropertyName("latency")]
    public LatencyStatsDto Latency { get; set; } = new();

    [JsonPropertyName("confusionMatrix")]
    public ConfusionMatrixDto ConfusionMatrix { get; set; } = new();
}

public class IntentTrainingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string MetadataPath { get; set; } = string.Empty;
    public IntentBenchmarkResult Benchmark { get; set; } = new();
}

public class IntentModelTrainer
{
    private readonly int _seed;

    public IntentModelTrainer(int seed = 42)
    {
        _seed = seed;
    }

    public IntentTrainingResult Train(
        List<IntentDatasetRecord> records,
        string rawDatasetJson,
        string outputDirectory)
    {
        var result = new IntentTrainingResult();
        if (records == null || records.Count == 0)
        {
            result.Success = false;
            result.Message = "Dataset is empty.";
            return result;
        }

        var approved = records.Where(r => r.Approved).ToList();
        if (approved.Count < 30)
        {
            result.Success = false;
            result.Message = $"Too few approved records ({approved.Count}). Minimum 30 required.";
            return result;
        }

        var trainRecords = approved.Where(r => string.Equals(r.Split, "train", StringComparison.OrdinalIgnoreCase)).ToList();
        var valRecords = approved.Where(r => string.Equals(r.Split, "val", StringComparison.OrdinalIgnoreCase)).ToList();
        var testRecords = approved.Where(r => string.Equals(r.Split, "test", StringComparison.OrdinalIgnoreCase)).ToList();

        if (trainRecords.Count == 0 || testRecords.Count == 0 || valRecords.Count == 0)
        {
            result.Success = false;
            result.Message = "Dataset must have non-empty 'train', 'val', and 'test' splits.";
            return result;
        }

        var mlContext = new MLContext(seed: _seed);

        var trainData = trainRecords.Select(r => new IntentInput { Text = r.Text, Label = r.Intent }).ToList();
        var valData = valRecords.Select(r => new IntentInput { Text = r.Text, Label = r.Intent }).ToList();
        var testData = testRecords.Select(r => new IntentInput { Text = r.Text, Label = r.Intent }).ToList();

        var trainDataView = mlContext.Data.LoadFromEnumerable(trainData);
        var valDataView = mlContext.Data.LoadFromEnumerable(valData);
        var testDataView = mlContext.Data.LoadFromEnumerable(testData);

        // Build classification pipeline
        var pipeline = mlContext.Transforms.Text.FeaturizeText("Features", nameof(IntentInput.Text))
            .Append(mlContext.Transforms.Conversion.MapValueToKey("KeyLabel", nameof(IntentInput.Label)))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("KeyLabel", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        var model = pipeline.Fit(trainDataView);

        // Inspect Score column slot names to extract exact class order
        var trainTransformed = model.Transform(trainDataView);
        VBuffer<ReadOnlyMemory<char>> slotNames = default;
        trainTransformed.Schema["Score"].Annotations.GetValue("SlotNames", ref slotNames);
        var scoreLabels = slotNames.DenseValues().Select(v => v.ToString()).ToArray();

        // 1. Validation Set Threshold Search
        // Search threshold t in [0.10, 0.70] that maximizes Validation Macro F1
        var valTransformed = model.Transform(valDataView);
        var valPredictions = mlContext.Data.CreateEnumerable<IntentEvaluationResult>(valTransformed, reuseRowObject: false).ToList();

        double bestThreshold = 0.35;
        double bestValScore = 0.0;
        var candidateThresholds = new[] { 0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60 };

        foreach (var t in candidateThresholds)
        {
            int correct = 0;
            foreach (var r in valPredictions)
            {
                float maxScore = (r.Score != null && r.Score.Length > 0) ? r.Score.Max() : 0.0f;
                string effectivePred = maxScore >= t ? r.PredictedLabel : AiChatIntentTypes.UnclearOrOutOfScope;
                if (string.Equals(effectivePred, r.Label, StringComparison.OrdinalIgnoreCase))
                {
                    correct++;
                }
            }

            double valAcc = (double)correct / valPredictions.Count;
            if (valAcc > bestValScore)
            {
                bestValScore = valAcc;
                bestThreshold = t;
            }
        }

        // 2. Compute Full Evaluation Metrics on Test Split
        var testTransformed = model.Transform(testDataView);
        var mlNetMetrics = ComputeMulticlassMetrics(mlContext, testTransformed, scoreLabels);

        // Verify score semantics on test split:
        // SdcaMaximumEntropy outputs probabilities directly: each score in [0, 1], finite, and sum ≈ 1
        var testResults = mlContext.Data.CreateEnumerable<IntentEvaluationResult>(testTransformed, reuseRowObject: false).ToList();
        foreach (var r in testResults)
        {
            if (r.Score != null && r.Score.Length > 0)
            {
                foreach (var s in r.Score)
                {
                    if (float.IsNaN(s) || float.IsInfinity(s))
                    {
                        throw new InvalidOperationException("Score vector contains NaN or Infinity values.");
                    }
                }
                float sum = r.Score.Sum();
                if (Math.Abs(sum - 1.0f) > 0.05f)
                {
                    throw new InvalidOperationException($"Score vector sum ({sum}) deviates significantly from 1.0.");
                }
            }
        }

        // 3. Independent Evaluation on Test Set across 3 approaches:
        // a) Pure Rule-Based Baseline (Mode = Off, strictly NO ML loaded or invoked)
        var ruleOnlyClassifier = new VietnameseIntentClassifier(IntentClassificationMode.Off);
        int ruleBasedCorrect = 0;
        foreach (var sample in testRecords)
        {
            var rulePred = ruleOnlyClassifier.Classify(sample.Text, new IntentClassificationContext());
            if (string.Equals(rulePred.Intent, sample.Intent, StringComparison.OrdinalIgnoreCase))
            {
                ruleBasedCorrect++;
            }
        }

        // b) ML.NET Only (using bestThreshold determined from validation set)
        int mlNetOnlyCorrect = 0;
        var predEngine = mlContext.Model.CreatePredictionEngine<IntentInput, IntentOutput>(model);
        foreach (var sample in testRecords)
        {
            var pred = predEngine.Predict(new IntentInput { Text = sample.Text });
            float conf = (pred.Score != null && pred.Score.Length > 0) ? pred.Score.Max() : 0.0f;
            string label = conf >= bestThreshold ? pred.PredictedLabel : AiChatIntentTypes.UnclearOrOutOfScope;
            if (string.Equals(label, sample.Intent, StringComparison.OrdinalIgnoreCase))
            {
                mlNetOnlyCorrect++;
            }
        }

        // c) Hybrid Pipeline (Active mode: Rules + ML.NET fallback above threshold)
        // Temporarily save model to disk for hybrid test
        Directory.CreateDirectory(outputDirectory);
        var tempModelPath = Path.Combine(outputDirectory, "vietnamese_intent_classifier_v1.zip");
        mlContext.Model.Save(model, trainDataView.Schema, tempModelPath);

        var hybridClassifier = new VietnameseIntentClassifier(IntentClassificationMode.Active, tempModelPath, (float)bestThreshold);
        int hybridCorrect = 0;
        foreach (var sample in testRecords)
        {
            var hypPred = hybridClassifier.Classify(sample.Text, new IntentClassificationContext());
            if (string.Equals(hypPred.Intent, sample.Intent, StringComparison.OrdinalIgnoreCase))
            {
                hybridCorrect++;
            }
        }

        // 4. Latency Benchmark with Warm-up
        var latencyStats = MeasureInferenceLatency(predEngine, testRecords.Select(r => r.Text).ToList());

        // 5. Construct Confusion Matrix Data Structure
        var distinctIntents = approved.Select(r => r.Intent).Distinct().OrderBy(i => i, StringComparer.Ordinal).ToList();
        var confMatrix = new ConfusionMatrixDto
        {
            Classes = distinctIntents,
            Matrix = new List<List<int>>()
        };

        foreach (var actual in distinctIntents)
        {
            var row = new List<int>();
            foreach (var predicted in distinctIntents)
            {
                int count = testResults.Count(r => r.Label == actual && r.PredictedLabel == predicted);
                row.Add(count);
            }
            confMatrix.Matrix.Add(row);
        }

        var benchmark = new IntentBenchmarkResult
        {
            TotalTrainSamples = trainRecords.Count,
            TotalValSamples = valRecords.Count,
            TotalTestSamples = testRecords.Count,
            OptimalThreshold = bestThreshold,
            ValAccuracyAtOptimalThreshold = bestValScore,
            RuleBasedCorrect = ruleBasedCorrect,
            RuleBasedAccuracy = (double)ruleBasedCorrect / testRecords.Count,
            MlNetOnlyCorrect = mlNetOnlyCorrect,
            MlNetOnlyAccuracy = (double)mlNetOnlyCorrect / testRecords.Count,
            HybridCorrect = hybridCorrect,
            HybridAccuracy = (double)hybridCorrect / testRecords.Count,
            MlNetMetrics = mlNetMetrics,
            Latency = latencyStats,
            ConfusionMatrix = confMatrix
        };

        // 6. Save Metadata
        var metadataPath = Path.Combine(outputDirectory, "intent_model_metadata.json");
        var metadataObj = new
        {
            ModelVersion = "1.1.0",
            TrainedAtUtc = DateTime.UtcNow,
            Algorithm = "SdcaMaximumEntropy",
            DatasetHashSha256 = ComputeSha256(rawDatasetJson),
            Provenance = "Synthetic Curated Vietnamese Healthcare Intent Dataset (178 samples)",
            Splits = new
            {
                Train = trainRecords.Count,
                Validation = valRecords.Count,
                Test = testRecords.Count
            },
            Intents = distinctIntents,
            OptimalConfidenceThreshold = bestThreshold,
            Benchmark = benchmark
        };

        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadataObj, new JsonSerializerOptions { WriteIndented = true }));

        result.Success = true;
        result.Message = "Intent model trained and evaluated successfully.";
        result.ModelPath = tempModelPath;
        result.MetadataPath = metadataPath;
        result.Benchmark = benchmark;

        return result;
    }

    private static EvaluationMetricsDto ComputeMulticlassMetrics(
        MLContext mlContext,
        IDataView testPredictions,
        string[] scoreLabels)
    {
        var evalResults = mlContext.Data.CreateEnumerable<IntentEvaluationResult>(testPredictions, reuseRowObject: false).ToList();
        if (!evalResults.Any())
        {
            return new EvaluationMetricsDto();
        }

        var metrics = mlContext.MulticlassClassification.Evaluate(testPredictions, labelColumnName: "KeyLabel", scoreColumnName: "Score");

        var perClassMetrics = new Dictionary<string, ClassMetricDto>();
        var distinctLabels = evalResults.Select(r => r.Label).Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList();

        double sumPrecision = 0;
        double sumRecall = 0;
        double sumF1 = 0;
        int evaluatedClassesCount = 0;

        foreach (var c in distinctLabels)
        {
            int tp = evalResults.Count(r => r.Label == c && r.PredictedLabel == c);
            int fp = evalResults.Count(r => r.Label != c && r.PredictedLabel == c);
            int fn = evalResults.Count(r => r.Label == c && r.PredictedLabel != c);
            int support = evalResults.Count(r => r.Label == c);

            double prec = (tp + fp) > 0 ? (double)tp / (tp + fp) : 0.0;
            double rec = (tp + fn) > 0 ? (double)tp / (tp + fn) : 0.0;
            double f1 = (prec + rec) > 0 ? (2 * prec * rec) / (prec + rec) : 0.0;

            perClassMetrics[c] = new ClassMetricDto
            {
                Precision = prec,
                Recall = rec,
                F1 = f1,
                Support = support
            };

            sumPrecision += prec;
            sumRecall += rec;
            sumF1 += f1;
            evaluatedClassesCount++;
        }

        double macroPrecision = evaluatedClassesCount > 0 ? sumPrecision / evaluatedClassesCount : 0.0;
        double macroRecall = evaluatedClassesCount > 0 ? sumRecall / evaluatedClassesCount : 0.0;
        double macroF1 = evaluatedClassesCount > 0 ? sumF1 / evaluatedClassesCount : 0.0;

        int top3Hits = 0;
        foreach (var r in evalResults)
        {
            if (r.Score != null && r.Score.Length > 0 && scoreLabels.Length == r.Score.Length)
            {
                var top3Classes = r.Score
                    .Select((score, idx) => new { Score = score, Class = scoreLabels[idx] })
                    .OrderByDescending(x => x.Score)
                    .Take(3)
                    .Select(x => x.Class)
                    .ToList();

                if (top3Classes.Contains(r.Label, StringComparer.OrdinalIgnoreCase))
                {
                    top3Hits++;
                }
            }
            else
            {
                if (string.Equals(r.PredictedLabel, r.Label, StringComparison.OrdinalIgnoreCase))
                {
                    top3Hits++;
                }
            }
        }

        double top3Accuracy = evalResults.Count > 0 ? (double)top3Hits / evalResults.Count : 0.0;

        return new EvaluationMetricsDto
        {
            MicroAccuracy = metrics.MicroAccuracy,
            MacroAccuracy = metrics.MacroAccuracy,
            LogLoss = metrics.LogLoss,
            MacroPrecision = macroPrecision,
            MacroRecall = macroRecall,
            MacroF1 = macroF1,
            Top3Accuracy = top3Accuracy,
            PerClassMetrics = perClassMetrics
        };
    }

    private static LatencyStatsDto MeasureInferenceLatency(
        PredictionEngine<IntentInput, IntentOutput> predEngine,
        List<string> testTexts)
    {
        if (testTexts.Count == 0) return new LatencyStatsDto();

        // 1. Warm-up (50 calls)
        for (int i = 0; i < 50; i++)
        {
            var text = testTexts[i % testTexts.Count];
            predEngine.Predict(new IntentInput { Text = text });
        }

        // 2. Timed benchmark (200 calls)
        const int benchmarkRuns = 200;
        var durations = new List<double>(benchmarkRuns);

        for (int i = 0; i < benchmarkRuns; i++)
        {
            var text = testTexts[i % testTexts.Count];
            var sw = Stopwatch.StartNew();
            predEngine.Predict(new IntentInput { Text = text });
            sw.Stop();
            durations.Add(sw.Elapsed.TotalMilliseconds);
        }

        durations.Sort();
        double min = durations.First();
        double max = durations.Last();
        double avg = durations.Average();
        double p50 = durations[(int)(benchmarkRuns * 0.50)];
        double p95 = durations[(int)(benchmarkRuns * 0.95)];

        return new LatencyStatsDto
        {
            Iterations = benchmarkRuns,
            WarmupIterations = 50,
            MinMs = Math.Round(min, 4),
            MaxMs = Math.Round(max, 4),
            AvgMs = Math.Round(avg, 4),
            P50Ms = Math.Round(p50, 4),
            P95Ms = Math.Round(p95, 4),
            Environment = $".NET 10.0 / {Environment.OSVersion}"
        };
    }

    private static string ComputeSha256(string content)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
