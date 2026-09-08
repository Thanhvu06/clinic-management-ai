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
        List<DatasetRecord> records,
        string rawDatasetJson,
        string outputDirectory,
        bool allowDemoData = false)
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

        // Filter approved records
        var approvedRecords = records.Where(r => r.Approved).ToList();
        if (!approvedRecords.Any())
        {
            result.Success = false;
            result.Message = "No approved records found in dataset.";
            return result;
        }

        bool containsDemoData = approvedRecords.Any(r => r.SourceType == "SIMULATED_TEST_DATA");
        if (containsDemoData && !allowDemoData)
        {
            result.Success = false;
            result.Message = "Dataset contains SIMULATED_TEST_DATA. Training rejected unless --allow-demo-data flag is explicitly supplied.";
            return result;
        }

        // Initialize ML.NET context with deterministic seed
        var mlContext = new MLContext(seed: _seed);

        // Separate train and test splits
        var trainRecords = approvedRecords.Where(r => r.Split.Equals("train", StringComparison.OrdinalIgnoreCase)).ToList();
        var testRecords = approvedRecords.Where(r => r.Split.Equals("test", StringComparison.OrdinalIgnoreCase)).ToList();

        // If no explicit test split, do an 80/20 split
        if (!testRecords.Any() || !trainRecords.Any())
        {
            var shuffled = approvedRecords.OrderBy(x => Guid.NewGuid()).ToList();
            int splitPoint = (int)(shuffled.Count * 0.8);
            trainRecords = shuffled.Take(splitPoint).ToList();
            testRecords = shuffled.Skip(splitPoint).ToList();
        }

        var trainData = trainRecords.Select(r => new SymptomInput { Text = r.Text, Label = r.PrimarySpecialtyCode }).ToList();
        var testData = testRecords.Select(r => new SymptomInput { Text = r.Text, Label = r.PrimarySpecialtyCode }).ToList();

        var trainDataView = mlContext.Data.LoadFromEnumerable(trainData);
        var testDataView = mlContext.Data.LoadFromEnumerable(testData);

        // Build text classification pipeline
        var pipeline = mlContext.Transforms.Text.FeaturizeText("Features", nameof(SymptomInput.Text))
            .Append(mlContext.Transforms.Conversion.MapValueToKey("Label"))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        // Fit model
        var model = pipeline.Fit(trainDataView);

        // Evaluate model
        var testPredictions = model.Transform(testDataView);
        var metrics = mlContext.MulticlassClassification.Evaluate(testPredictions, labelColumnName: "Label", scoreColumnName: "Score");

        // Calculate dataset hash
        string datasetHash;
        using (var sha = SHA256.Create())
        {
            var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawDatasetJson));
            datasetHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        // Prepare metadata
        var specialtyCodes = approvedRecords.Select(r => r.PrimarySpecialtyCode).Distinct().OrderBy(c => c).ToList();
        var metadata = new ModelMetadata
        {
            ModelVersion = "1.0.0",
            DatasetVersion = approvedRecords.FirstOrDefault()?.DatasetVersion ?? "1.0.0",
            DatasetHashSha256 = datasetHash,
            TrainedAtUtc = DateTime.UtcNow,
            ClinicallyValidated = !containsDemoData,
            SpecialtyCodes = specialtyCodes,
            Metrics = new EvaluationMetricsDto
            {
                MicroAccuracy = metrics.MicroAccuracy,
                MacroAccuracy = metrics.MacroAccuracy,
                LogLoss = metrics.LogLoss,
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
        result.Message = $"Model successfully trained and exported. Accuracy: {metrics.MacroAccuracy:P2}. ClinicallyValidated: {metadata.ClinicallyValidated}";

        return result;
    }
}
