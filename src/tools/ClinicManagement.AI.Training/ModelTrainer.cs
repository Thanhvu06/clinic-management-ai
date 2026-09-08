using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicManagement.AI.Training;

public class SymptomInput
{
    public string Text { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class SymptomOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}

public class SymptomEvaluationResult
{
    [ColumnName("Label")]
    public string Label { get; set; } = string.Empty;

    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    [ColumnName("Score")]
    public float[] Score { get; set; } = Array.Empty<float>();
}

public class TrainingResult
{
    public bool Success { get; set; }
    public string ModelPath { get; set; } = string.Empty;
    public string MetadataPath { get; set; } = string.Empty;
    public ModelMetadata Metadata { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class ModelTrainer
{
    private readonly int _seed;

    public ModelTrainer(int seed = 42)
    {
        _seed = seed;
    }

    public TrainingResult Train(
        List<DatasetRecord>? records,
        string rawDatasetJson,
        string outputDirectory,
        bool allowDemoData = false,
        string? approvalManifestPath = null)
    {
        var result = new TrainingResult();
        var validator = new DatasetValidator();
        var report = validator.Validate(records);

        if (!report.IsValid)
        {
            result.Success = false;
            result.Message = $"Validation failed with {report.Errors.Count} error(s): " + string.Join("; ", report.Errors);
            return result;
        }

        // Filter approved records strictly
        var approvedRecords = records!.Where(r => r.Approved).ToList();
        if (!approvedRecords.Any())
        {
            result.Success = false;
            result.Message = "No approved records found in dataset.";
            return result;
        }

        bool containsDemoData = approvedRecords.Any(r => string.Equals(r.SourceType, "SIMULATED_TEST_DATA", StringComparison.OrdinalIgnoreCase));
        if (containsDemoData && !allowDemoData)
        {
            result.Success = false;
            result.Message = "Dataset contains SIMULATED_TEST_DATA. Training rejected unless --allow-demo-data flag is explicitly supplied.";
            return result;
        }

        // Calculate dataset SHA-256 hash
        string datasetHash;
        using (var sha = SHA256.Create())
        {
            var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawDatasetJson));
            datasetHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        // Handle clinical validation governance
        bool isClinicallyValidated = false;
        string? approvedBy = null;
        DateTime? approvedAtUtc = null;

        if (!string.IsNullOrWhiteSpace(approvalManifestPath) && File.Exists(approvalManifestPath))
        {
            try
            {
                var manifestJson = File.ReadAllText(approvalManifestPath);
                var manifest = JsonSerializer.Deserialize<ClinicalApprovalManifest>(manifestJson);
                if (manifest != null &&
                    string.Equals(manifest.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(manifest.DatasetHashSha256, datasetHash, StringComparison.OrdinalIgnoreCase) &&
                    !containsDemoData)
                {
                    isClinicallyValidated = true;
                    approvedBy = manifest.ApprovedBy;
                    approvedAtUtc = manifest.ApprovedAtUtc;
                }
                else
                {
                    Console.WriteLine("[GOVERNANCE] Clinical approval manifest rejected: Hash mismatch or dataset contains demo data.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GOVERNANCE] Failed to parse clinical approval manifest: {ex.Message}");
            }
        }

        // Initialize ML.NET context with deterministic seed
        var mlContext = new MLContext(seed: _seed);

        // Separate train, val, and test splits
        var trainRecords = approvedRecords.Where(r => string.Equals(r.Split, "train", StringComparison.OrdinalIgnoreCase)).ToList();
        var valRecords = approvedRecords.Where(r => string.Equals(r.Split, "val", StringComparison.OrdinalIgnoreCase)).ToList();
        var testRecords = approvedRecords.Where(r => string.Equals(r.Split, "test", StringComparison.OrdinalIgnoreCase)).ToList();

        // Deterministic grouped split fallback if no train/test split present
        if (!testRecords.Any() || !trainRecords.Any())
        {
            var groupedFamilies = approvedRecords
                .GroupBy(r => string.IsNullOrWhiteSpace(r.ScenarioFamily) ? r.CaseId : r.ScenarioFamily)
                .OrderBy(g => g.Key, StringComparer.Ordinal) // Deterministic ordering
                .ToList();

            trainRecords = new List<DatasetRecord>();
            testRecords = new List<DatasetRecord>();

            int targetTestCount = Math.Max(1, (int)(approvedRecords.Count * 0.2));
            int currentTestCount = 0;

            foreach (var group in groupedFamilies)
            {
                if (currentTestCount < targetTestCount && trainRecords.Any())
                {
                    testRecords.AddRange(group);
                    currentTestCount += group.Count();
                }
                else
                {
                    trainRecords.AddRange(group);
                }
            }
        }

        var trainData = trainRecords.Select(r => new SymptomInput { Text = r.Text, Label = r.PrimarySpecialtyCode }).ToList();
        var testData = testRecords.Select(r => new SymptomInput { Text = r.Text, Label = r.PrimarySpecialtyCode }).ToList();

        var trainDataView = mlContext.Data.LoadFromEnumerable(trainData);
        var testDataView = mlContext.Data.LoadFromEnumerable(testData);

        // Build text classification pipeline
        var pipeline = mlContext.Transforms.Text.FeaturizeText("Features", nameof(SymptomInput.Text))
            .Append(mlContext.Transforms.Conversion.MapValueToKey("KeyLabel", "Label"))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("KeyLabel", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        // Fit model
        var model = pipeline.Fit(trainDataView);

        // Evaluate model
        var testPredictions = model.Transform(testDataView);
        var metrics = mlContext.MulticlassClassification.Evaluate(testPredictions, labelColumnName: "KeyLabel", scoreColumnName: "Score");

        // Read evaluated records to calculate honest, exact per-class metrics, confusion matrix, top-3 accuracy
        var evaluationResults = mlContext.Data.CreateEnumerable<SymptomEvaluationResult>(testPredictions, reuseRowObject: false).ToList();

        // Retrieve class slot names for score vector
        string[] scoreLabels = Array.Empty<string>();
        try
        {
            VBuffer<ReadOnlyMemory<char>> slotNames = default;
            testPredictions.Schema["Score"].Annotations.GetValue("SlotNames", ref slotNames);
            scoreLabels = slotNames.DenseValues().Select(v => v.ToString()).ToArray();
        }
        catch
        {
            // If slot names metadata is unavailable, fallback to distinct labels in training
            scoreLabels = trainRecords.Select(r => r.PrimarySpecialtyCode).Distinct().OrderBy(c => c).ToArray();
        }

        // Calculate per-class metrics & confusion matrix
        var perClassMetrics = new Dictionary<string, ClassMetricDto>();
        var testLabels = evaluationResults.Select(r => r.Label).Distinct().OrderBy(l => l).ToList();

        double sumPrecision = 0;
        double sumRecall = 0;
        double sumF1 = 0;
        int evaluatedClassesCount = 0;

        foreach (var c in testLabels)
        {
            int tp = evaluationResults.Count(r => r.Label == c && r.PredictedLabel == c);
            int fp = evaluationResults.Count(r => r.Label != c && r.PredictedLabel == c);
            int fn = evaluationResults.Count(r => r.Label == c && r.PredictedLabel != c);
            int support = evaluationResults.Count(r => r.Label == c);

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

        // Calculate Top-3 Accuracy
        int top3Hits = 0;
        foreach (var r in evaluationResults)
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
                // Fallback: if single prediction matches
                if (string.Equals(r.PredictedLabel, r.Label, StringComparison.OrdinalIgnoreCase))
                {
                    top3Hits++;
                }
            }
        }

        double top3Accuracy = evaluationResults.Count > 0 ? (double)top3Hits / evaluationResults.Count : 0.0;

        // Prepare metadata
        var specialtyCodes = approvedRecords.Select(r => r.PrimarySpecialtyCode).Distinct().OrderBy(c => c).ToList();
        var metadata = new ModelMetadata
        {
            ModelVersion = "1.0.0",
            DatasetVersion = approvedRecords.FirstOrDefault()?.DatasetVersion ?? "1.0.0",
            DatasetHashSha256 = datasetHash,
            TrainedAtUtc = DateTime.UtcNow,
            ClinicallyValidated = isClinicallyValidated,
            ApprovalManifestPath = isClinicallyValidated ? approvalManifestPath : null,
            ApprovedBy = approvedBy,
            ApprovedAtUtc = approvedAtUtc,
            SpecialtyCodes = specialtyCodes,
            Metrics = new EvaluationMetricsDto
            {
                MicroAccuracy = metrics.MicroAccuracy,
                MacroAccuracy = metrics.MacroAccuracy,
                LogLoss = metrics.LogLoss,
                MacroPrecision = macroPrecision,
                MacroRecall = macroRecall,
                MacroF1 = macroF1,
                Top3Accuracy = top3Accuracy,
                PerClassMetrics = perClassMetrics,
                ConfusionMatrix = metrics.ConfusionMatrix.GetFormattedConfusionTable()
            }
        };

        // Export model & metadata
        Directory.CreateDirectory(outputDirectory);
        var modelPath = Path.Combine(outputDirectory, "specialty_classifier_v1.zip");
        var metadataPath = Path.Combine(outputDirectory, "model_metadata.json");

        mlContext.Model.Save(model, trainDataView.Schema, modelPath);
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

        result.Success = true;
        result.ModelPath = modelPath;
        result.MetadataPath = metadataPath;
        result.Metadata = metadata;
        result.Message = $"Model successfully trained and exported. Train: {trainRecords.Count}, Test: {testRecords.Count}. MacroAccuracy: {metrics.MacroAccuracy:P2}, MacroF1: {macroF1:P2}, Top3Accuracy: {top3Accuracy:P2}. ClinicallyValidated: {metadata.ClinicallyValidated}";

        return result;
    }
}
