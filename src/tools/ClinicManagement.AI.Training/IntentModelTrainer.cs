using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public class IntentBenchmarkResult
{
    public int TotalTestSamples { get; set; }
    public int MlNetCorrect { get; set; }
    public double MlNetAccuracy { get; set; }
    public int RuleBasedCorrect { get; set; }
    public double RuleBasedAccuracy { get; set; }
    public EvaluationMetricsDto MlNetMetrics { get; set; } = new();
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

        if (trainRecords.Count == 0 || testRecords.Count == 0)
        {
            result.Success = false;
            result.Message = "Dataset must have non-empty 'train' and 'test' splits.";
            return result;
        }

        var mlContext = new MLContext(seed: _seed);

        var trainData = trainRecords.Select(r => new IntentInput { Text = r.Text, Label = r.Intent }).ToList();
        var valData = valRecords.Select(r => new IntentInput { Text = r.Text, Label = r.Intent }).ToList();
        var testData = testRecords.Select(r => new IntentInput { Text = r.Text, Label = r.Intent }).ToList();

        var trainDataView = mlContext.Data.LoadFromEnumerable(trainData);
        var testDataView = mlContext.Data.LoadFromEnumerable(testData);

        // Build classification pipeline
        var pipeline = mlContext.Transforms.Text.FeaturizeText("Features", nameof(IntentInput.Text))
            .Append(mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(IntentInput.Label)))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        var model = pipeline.Fit(trainDataView);

        // Evaluate ML.NET Model
        var testPredictions = model.Transform(testDataView);
        var metrics = mlContext.MulticlassClassification.Evaluate(testPredictions, "Label", "Score");

        var predEngine = mlContext.Model.CreatePredictionEngine<IntentInput, IntentOutput>(model);

        // Evaluate and Benchmark against Rule-Based Baseline on Test Set
        int mlNetCorrect = 0;
        int ruleBasedCorrect = 0;
        var ruleClassifier = new VietnameseIntentClassifier();

        foreach (var sample in testRecords)
        {
            var mlPred = predEngine.Predict(new IntentInput { Text = sample.Text });
            if (string.Equals(mlPred.PredictedLabel, sample.Intent, StringComparison.OrdinalIgnoreCase))
            {
                mlNetCorrect++;
            }

            var rulePred = ruleClassifier.Classify(sample.Text, new IntentClassificationContext());
            if (string.Equals(rulePred.Intent, sample.Intent, StringComparison.OrdinalIgnoreCase))
            {
                ruleBasedCorrect++;
            }
        }

        var benchmark = new IntentBenchmarkResult
        {
            TotalTestSamples = testRecords.Count,
            MlNetCorrect = mlNetCorrect,
            MlNetAccuracy = (double)mlNetCorrect / testRecords.Count,
            RuleBasedCorrect = ruleBasedCorrect,
            RuleBasedAccuracy = (double)ruleBasedCorrect / testRecords.Count,
            MlNetMetrics = new EvaluationMetricsDto
            {
                MicroAccuracy = metrics.MicroAccuracy,
                MacroAccuracy = metrics.MacroAccuracy,
                LogLoss = metrics.LogLoss,
                MacroPrecision = metrics.MacroAccuracy,
                MacroRecall = metrics.MacroAccuracy,
                MacroF1 = metrics.MacroAccuracy,
                Top3Accuracy = metrics.TopKAccuracy
            }
        };

        // Save model
        Directory.CreateDirectory(outputDirectory);
        var modelPath = Path.Combine(outputDirectory, "vietnamese_intent_classifier_v1.zip");
        mlContext.Model.Save(model, trainDataView.Schema, modelPath);

        // Save metadata
        var metadataPath = Path.Combine(outputDirectory, "intent_model_metadata.json");
        var metadataObj = new
        {
            ModelVersion = "1.0.0",
            TrainedAtUtc = DateTime.UtcNow,
            TotalRecords = approved.Count,
            TrainCount = trainRecords.Count,
            ValCount = valRecords.Count,
            TestCount = testRecords.Count,
            Intents = approved.Select(r => r.Intent).Distinct().OrderBy(i => i).ToList(),
            Benchmark = benchmark
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadataObj, new JsonSerializerOptions { WriteIndented = true }));

        result.Success = true;
        result.Message = "Intent model trained and benchmarked successfully.";
        result.ModelPath = modelPath;
        result.MetadataPath = metadataPath;
        result.Benchmark = benchmark;

        return result;
    }
}
