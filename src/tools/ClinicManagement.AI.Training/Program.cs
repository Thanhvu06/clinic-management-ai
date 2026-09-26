using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClinicManagement.AI.Training;

namespace ClinicManagement.AI.Training;

public class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("=========================================================");
        Console.WriteLine("ClinicCare AI: Specialty Classification Training Tool");
        Console.WriteLine("=========================================================");

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();

        if (command == "--validate")
        {
            string dataPath = args.Length > 1 ? args[1] : "data/symptom_specialty_dataset.json";
            return RunValidate(dataPath);
        }
        else if (command == "--train")
        {
            string dataPath = "data/symptom_specialty_dataset.json";
            string outDir = "models";
            bool allowDemo = args.Contains("--allow-demo-data");
            string? manifestPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--data" && i + 1 < args.Length) dataPath = args[++i];
                if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
                if ((args[i] == "--manifest" || args[i] == "--approval-manifest") && i + 1 < args.Length) manifestPath = args[++i];
            }

            return RunTrain(dataPath, outDir, allowDemo, manifestPath);
        }
        else if (command == "--train-intent" || command == "--eval-intent")
        {
            string dataPath = "data/vietnamese_intent_dataset.json";
            string outDir = "models";

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--data" && i + 1 < args.Length) dataPath = args[++i];
                if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            }

            return RunTrainIntent(dataPath, outDir);
        }
        else if (command == "--evaluate-copilot")
        {
            var benchmarkPath = args.Length > 1 ? args[1] : Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "patient_copilot_benchmark.json");
            if (!File.Exists(benchmarkPath))
            {
                Console.WriteLine($"Benchmark file not found: {benchmarkPath}");
                return 1;
            }
            Console.WriteLine(PatientCopilotBenchmarkRunner.Evaluate(benchmarkPath));
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Unknown command: {command}");
            Console.ResetColor();
            PrintUsage();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  ClinicManagement.AI.Training --validate [datasetPath]");
        Console.WriteLine("  ClinicManagement.AI.Training --train [--data datasetPath] [--out outputDir] [--allow-demo-data] [--manifest manifestPath]");
        Console.WriteLine("  ClinicManagement.AI.Training --train-intent [--data datasetPath] [--out outputDir]");
        Console.WriteLine("  ClinicManagement.AI.Training --eval-intent [--data datasetPath]");
        Console.WriteLine("  ClinicManagement.AI.Training --evaluate-copilot [benchmarkPath]");
    }

    private static int RunValidate(string dataPath)
    {
        if (!File.Exists(dataPath))
        {
            var alt = Path.Combine("src", "tools", "ClinicManagement.AI.Training", dataPath);
            if (File.Exists(alt)) dataPath = alt;
        }

        if (!File.Exists(dataPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Dataset file not found: {dataPath}");
            Console.ResetColor();
            return 1;
        }

        var json = File.ReadAllText(dataPath);
        var records = JsonSerializer.Deserialize<List<DatasetRecord>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (records == null)
        {
            Console.WriteLine("Failed to deserialize dataset.");
            return 1;
        }

        var validator = new DatasetValidator();
        var report = validator.Validate(records);

        Console.WriteLine($"Total Records: {report.TotalRecords} (Approved: {report.ApprovedRecords}, Unapproved: {report.UnapprovedRecords})");
        Console.WriteLine("Class Distribution (Total / Training Subset):");
        foreach (var kvp in report.TotalClassDistribution)
        {
            int trainCount = report.TrainingClassDistribution.GetValueOrDefault(kvp.Key, 0);
            Console.WriteLine($"  {kvp.Key}: {kvp.Value} total sample(s) | {trainCount} in training");
        }

        Console.WriteLine("Split Distribution:");
        foreach (var kvp in report.SplitDistribution)
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value} record(s)");
        }

        if (report.Warnings.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warnings:");
            foreach (var w in report.Warnings) Console.WriteLine($"  - {w}");
            Console.ResetColor();
        }

        if (!report.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Validation FAILED with errors:");
            foreach (var e in report.Errors) Console.WriteLine($"  - {e}");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Validation PASSED successfully.");
        Console.ResetColor();
        return 0;
    }

    private static int RunTrain(string dataPath, string outDir, bool allowDemo, string? manifestPath)
    {
        if (!File.Exists(dataPath))
        {
            var alt = Path.Combine("src", "tools", "ClinicManagement.AI.Training", dataPath);
            if (File.Exists(alt)) dataPath = alt;
        }

        if (!File.Exists(dataPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Dataset file not found: {dataPath}");
            Console.ResetColor();
            return 1;
        }

        var json = File.ReadAllText(dataPath);
        var records = JsonSerializer.Deserialize<List<DatasetRecord>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (records == null || !records.Any())
        {
            Console.WriteLine("Error: Dataset is empty or invalid JSON.");
            return 1;
        }

        var trainer = new ModelTrainer(seed: 42);
        var result = trainer.Train(records, json, outDir, allowDemo, manifestPath);

        if (!result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Training FAILED: {result.Message}");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Training completed successfully.");
        Console.WriteLine($"Status: {result.Message}");
        Console.WriteLine($"Model exported: {result.ModelPath}");
        Console.WriteLine($"Metadata exported: {result.MetadataPath}");
        Console.WriteLine($"ClinicallyValidated: {result.Metadata.ClinicallyValidated}");
        Console.WriteLine("Evaluation Metrics (Honest Evaluation on Test Split):");
        Console.WriteLine($"  - Micro Accuracy:  {result.Metadata.Metrics.MicroAccuracy:P2}");
        Console.WriteLine($"  - Macro Accuracy:  {result.Metadata.Metrics.MacroAccuracy:P2}");
        Console.WriteLine($"  - Macro Precision: {result.Metadata.Metrics.MacroPrecision:P2}");
        Console.WriteLine($"  - Macro Recall:    {result.Metadata.Metrics.MacroRecall:P2}");
        Console.WriteLine($"  - Macro F1:        {result.Metadata.Metrics.MacroF1:P2}");
        Console.WriteLine($"  - Top-3 Accuracy:  {result.Metadata.Metrics.Top3Accuracy:P2}");
        Console.WriteLine($"  - Log Loss:        {result.Metadata.Metrics.LogLoss:F4}");
        Console.ResetColor();
        return 0;
    }

    private static int RunTrainIntent(string dataPath, string outDir)
    {
        if (!File.Exists(dataPath))
        {
            var alt = Path.Combine("src", "tools", "ClinicManagement.AI.Training", dataPath);
            if (File.Exists(alt)) dataPath = alt;
        }

        if (!Directory.Exists(outDir) && Directory.Exists(Path.Combine("src", "tools", "ClinicManagement.AI.Training", outDir)))
        {
            outDir = Path.Combine("src", "tools", "ClinicManagement.AI.Training", outDir);
        }

        if (!File.Exists(dataPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Intent dataset file not found: {dataPath}");
            Console.ResetColor();
            return 1;
        }

        var json = File.ReadAllText(dataPath);
        var records = JsonSerializer.Deserialize<List<IntentDatasetRecord>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (records == null || !records.Any())
        {
            Console.WriteLine("Error: Intent dataset is empty or invalid JSON.");
            return 1;
        }

        var trainer = new IntentModelTrainer(seed: 42);
        var result = trainer.Train(records, json, outDir);

        if (!result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Intent Training FAILED: {result.Message}");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Intent model training completed successfully.");
        Console.WriteLine($"Status: {result.Message}");
        Console.WriteLine($"Model exported: {result.ModelPath}");
        Console.WriteLine($"Metadata exported: {result.MetadataPath}");
        Console.WriteLine("=========================================================");
        Console.WriteLine("BENCHMARK RESULTS ON INDEPENDENT TEST SPLIT (30 samples):");
        Console.WriteLine($"  - Optimal Validation Threshold: {result.Benchmark.OptimalThreshold:F2} (Val Acc: {result.Benchmark.ValAccuracyAtOptimalThreshold:P2})");
        Console.WriteLine($"  - Pure Rule Baseline Accuracy:  {result.Benchmark.RuleBasedAccuracy:P2} ({result.Benchmark.RuleBasedCorrect}/{result.Benchmark.TotalTestSamples})");
        Console.WriteLine($"  - ML.NET Model-Only Accuracy:   {result.Benchmark.MlNetOnlyAccuracy:P2} ({result.Benchmark.MlNetOnlyCorrect}/{result.Benchmark.TotalTestSamples})");
        Console.WriteLine($"  - Hybrid Pipeline Accuracy:     {result.Benchmark.HybridAccuracy:P2} ({result.Benchmark.HybridCorrect}/{result.Benchmark.TotalTestSamples})");
        Console.WriteLine($"  - ML.NET Macro Precision:       {result.Benchmark.MlNetMetrics.MacroPrecision:P2}");
        Console.WriteLine($"  - ML.NET Macro Recall:          {result.Benchmark.MlNetMetrics.MacroRecall:P2}");
        Console.WriteLine($"  - ML.NET Macro F1:              {result.Benchmark.MlNetMetrics.MacroF1:P2}");
        Console.WriteLine($"  - ML.NET Log Loss:              {result.Benchmark.MlNetMetrics.LogLoss:F4}");
        Console.WriteLine($"  - Inference Latency:            avg={result.Benchmark.Latency.AvgMs:F4}ms, p50={result.Benchmark.Latency.P50Ms:F4}ms, p95={result.Benchmark.Latency.P95Ms:F4}ms");
        Console.WriteLine("=========================================================");
        Console.ResetColor();
        return 0;
    }
}
