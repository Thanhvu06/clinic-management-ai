using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            string? outDir = null;

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
        else if (command == "--generate-phase4-dataset")
        {
            var path = PositionalOrDefault(args, Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "phase4_independent_cases.json"));
            Console.WriteLine(Phase4BenchmarkRunner.Generate(path));
            return 0;
        }
        else if (command == "--validate-phase4")
        {
            var path = PositionalOrDefault(args, Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "phase4_independent_cases.json"));
            var trainPath = OptionOrDefault(args, "--train", Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "vietnamese_intent_dataset.json"));
            var json = Phase4BenchmarkRunner.ValidateOnly(path, trainPath);
            Console.WriteLine(json);
            var report = JsonSerializer.Deserialize<Phase4ValidationReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return report?.IsValid == true ? 0 : 1;
        }
        else if (command == "--phase4-self-test")
        {
            var passed = Phase4BenchmarkRunner.SelfTest(out var detail);
            Console.WriteLine(JsonSerializer.Serialize(new { passed, detail }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        else if (command == "--benchmark-phase4")
        {
            var path = PositionalOrDefault(args, Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "phase4_independent_cases.json"));
            var trainPath = OptionOrDefault(args, "--train", Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "vietnamese_intent_dataset.json"));
            var reportPath = OptionOrDefault(args, "--report", Path.Combine("docs", "ai", "PHASE_4_BENCHMARK_REPORT.json"));
            var sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? ReadGitValue("rev-parse", "HEAD") ?? "local-unprovided";
            var workingTreeDirty = !string.IsNullOrWhiteSpace(ReadGitValue("status", "--short"));
            var json = Phase4BenchmarkRunner.Evaluate(path, trainPath, sourceHead, workingTreeDirty);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, json);
            Console.WriteLine(json);
            var report = JsonSerializer.Deserialize<Phase4BenchmarkReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return report?.Dataset.IsValid == true
                && report.SelfTestPassed
                && report.DevelopmentSet.ValidationPassed
                && report.IndependentHoldout.ValidationPassed
                ? 0
                : 1;
        }
        else if (command == "--gen-role-intent")
        {
            // Reuse the existing repository-root resolution without changing older commands.
            var modelsDirectory = ResolveIntentOutputDirectory(null, Directory.GetCurrentDirectory());
            var repoRoot = Path.GetFullPath(Path.Combine(modelsDirectory, "..", "..", "..", ".."));
            var defaultData = Path.Combine(repoRoot, "src", "tools", "ClinicManagement.AI.Training", "data");
            var dataDirectory = OptionOrDefault(args, "--data-dir", defaultData);
            var manifest = RoleIntentDatasetGenerator.Generate(dataDirectory);
            Console.WriteLine(JsonSerializer.Serialize(manifest.Totals));
            return 0;
        }
        else if (command is "--train-role-intent" or "--eval-role-intent")
        {
            var version = OptionOrDefault(args, "--version", "v2");
            if (version is not ("v1" or "v2"))
            {
                Console.Error.WriteLine("Role-intent --version must be v1 or v2.");
                return 1;
            }
            var scalarExit = RoleIntentModelPipeline.RunScalarCommand(args);
            if (scalarExit.HasValue) return scalarExit.Value;
            var modelsDirectory = ResolveIntentOutputDirectory(null, Directory.GetCurrentDirectory());
            var repoRoot = Path.GetFullPath(Path.Combine(modelsDirectory, "..", "..", "..", ".."));
            var projectDirectory = Path.Combine(repoRoot, "src", "tools", "ClinicManagement.AI.Training");
            var dataDirectory = OptionOrDefault(args, "--data-dir", Path.Combine(projectDirectory, "data"));
            var outputDirectory = OptionOrDefault(args, "--out-dir", Path.Combine(projectDirectory, "models", $"role-intent-{version}"));
            try
            {
                if (version == "v2" && command == "--train-role-intent")
                {
                    var metadata = RoleIntentModelV2Pipeline.Train(dataDirectory, outputDirectory);
                    Console.WriteLine(JsonSerializer.Serialize(new { metadata.Configuration, metadata.ThresholdPolicy.Threshold, metadata.ValidationPredictionSha256 }));
                }
                else if (version == "v2")
                {
                    var report = RoleIntentModelV2Pipeline.EvaluateOnce(dataDirectory, outputDirectory, Path.Combine(projectDirectory, "models", "role-intent-v1"));
                    Console.WriteLine(JsonSerializer.Serialize(new { report.Eval.Unfiltered.Accuracy, report.Eval.Unfiltered.MacroF1, filteredAccuracy = report.Eval.Filtered.Accuracy, filteredMacroF1 = report.Eval.Filtered.MacroF1, report.Provenance }));
                }
                else if (File.Exists(Path.Combine(outputDirectory, RoleIntentModelPipeline.ReportFile)))
                {
                    // Frozen v1 is read-only: return its recorded result without training or rescoring eval.
                    RoleIntentModelPipeline.Load(outputDirectory, out var metadata);
                    var report = RoleIntentModelPipeline.Read<RoleIntentEvaluationReport>(Path.Combine(outputDirectory, RoleIntentModelPipeline.ReportFile));
                    if (report.ModelSha256 != metadata.ModelSha256) throw new InvalidDataException("Frozen v1 report checksum mismatch.");
                    if (command == "--train-role-intent")
                        Console.WriteLine(JsonSerializer.Serialize(new { metadata.Configuration, metadata.ThresholdPolicy.Threshold, metadata.ValidationPredictionSha256, frozen = true }));
                    else
                        Console.WriteLine(JsonSerializer.Serialize(new { report.Eval.Unfiltered.Accuracy, report.Eval.Unfiltered.MacroF1, filteredAccuracy = report.Eval.Filtered.Accuracy, filteredMacroF1 = report.Eval.Filtered.MacroF1, report.Provenance, cached = true }));
                }
                else if (command == "--train-role-intent")
                {
                    var metadata = RoleIntentModelPipeline.Train(dataDirectory, outputDirectory);
                    Console.WriteLine(JsonSerializer.Serialize(new { metadata.Configuration, metadata.ThresholdPolicy.Threshold, metadata.ValidationPredictionSha256 }));
                }
                else
                {
                    var report = RoleIntentModelPipeline.EvaluateOnce(dataDirectory, outputDirectory);
                    Console.WriteLine(JsonSerializer.Serialize(new { report.Eval.Unfiltered.Accuracy, report.Eval.Unfiltered.MacroF1, filteredAccuracy = report.Eval.Filtered.Accuracy, filteredMacroF1 = report.Eval.Filtered.MacroF1, report.Provenance }));
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }
        else if (command == "--gateb")
        {
            return RunGateB(args);
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
        Console.WriteLine("  ClinicManagement.AI.Training --generate-phase4-dataset [datasetPath]");
        Console.WriteLine("  ClinicManagement.AI.Training --validate-phase4 [datasetPath] [--train trainDatasetPath]");
        Console.WriteLine("  ClinicManagement.AI.Training --phase4-self-test");
        Console.WriteLine("  ClinicManagement.AI.Training --benchmark-phase4 [datasetPath] [--train trainDatasetPath] [--report reportPath]");
        Console.WriteLine("  ClinicManagement.AI.Training --gen-role-intent [--data-dir directory]");
        Console.WriteLine("  ClinicManagement.AI.Training --train-role-intent [--version v1|v2 (default v2)] [--data-dir directory] [--out-dir directory]");
        Console.WriteLine("  ClinicManagement.AI.Training --eval-role-intent [--version v1|v2 (default v2)] [--data-dir directory] [--out-dir directory]");
        Console.WriteLine("  ClinicManagement.AI.Training --gateb [--report reportPath] [--promotion promotionPath]");
    }

    private static string PositionalOrDefault(string[] args, string fallback) =>
        args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : fallback;

    private static string OptionOrDefault(string[] args, string option, string fallback)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }
        return fallback;
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

    public static string ResolveIntentOutputDirectory(string? explicitOut, string startDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitOut))
            return Path.GetFullPath(explicitOut, startDirectory);

        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, "src", "backend", "ClinicManagement.sln")))
                return Path.Combine(directory.FullName, "src", "backend", "ClinicManagement.Infrastructure", "models");
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find the repository root for the default intent model output.");
    }

    private static int RunTrainIntent(string dataPath, string? explicitOut)
    {
        if (!File.Exists(dataPath))
        {
            var alt = Path.Combine("src", "tools", "ClinicManagement.AI.Training", dataPath);
            if (File.Exists(alt)) dataPath = alt;
        }

        var outDir = ResolveIntentOutputDirectory(explicitOut, Directory.GetCurrentDirectory());

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

    private static int RunGateB(string[] args)
    {
        var legacyPath = OptionOrDefault(args, "--legacy", Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "vietnamese_intent_dataset.json"));
        var additionsPath = OptionOrDefault(args, "--additions", Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "gateb_intent_registry_additions.json"));
        var independentPath = OptionOrDefault(args, "--independent", Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "phase4_independent_cases.json"));
        var blindPath = OptionOrDefault(args, "--blind", Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "phase5_blind_holdout.json"));
        var manifestPath = OptionOrDefault(args, "--manifest", Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "phase5_blind_holdout_manifest.json"));
        var reportPath = OptionOrDefault(args, "--report", Path.Combine("docs", "ai", "GATE_B_MODEL_REPORT.json"));
        var promotionPath = OptionOrDefault(args, "--promotion", Path.Combine("docs", "ai", "GATE_B_PROMOTION_DECISION.json"));
        var productionModelPath = OptionOrDefault(args, "--production-model", Path.Combine("src", "backend", "ClinicManagement.Infrastructure", "models", "vietnamese_intent_classifier_v1.zip"));
        var productionMetadataPath = OptionOrDefault(args, "--production-metadata", Path.Combine("src", "backend", "ClinicManagement.Infrastructure", "models", "intent_model_metadata.json"));

        var report = GateBModelPipeline.Run(
            legacyPath,
            additionsPath,
            independentPath,
            blindPath,
            manifestPath,
            productionModelPath,
            productionMetadataPath,
            ReadGitValue("rev-parse", "HEAD") ?? "local-unprovided",
            !string.IsNullOrWhiteSpace(ReadGitValue("status", "--short")));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(promotionPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(promotionPath, JsonSerializer.Serialize(report.Promotion, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            report.Status,
            report.Registry.IsValid,
            report.SelectedCandidate,
            report.Promotion.Decision,
            report.Promotion.Reason,
            candidates = report.Candidates.Select(candidate => new { candidate.Name, candidate.Status, candidate.Deterministic, developmentMacroF1 = candidate.Development.MacroF1, blindMacroF1 = candidate.FrozenBlind.MacroF1 })
        }, new JsonSerializerOptions { WriteIndented = true }));
        return report.Registry.IsValid ? 0 : 1;
    }

    private static string? ReadGitValue(params string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = string.Join(" ", arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument)),
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
