using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ClinicManagement.AI.Training;

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

    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--data" && i + 1 < args.Length) dataPath = args[++i];
        if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
    }

    return RunTrain(dataPath, outDir, allowDemo);
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Unknown command: {command}");
    Console.ResetColor();
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  ClinicManagement.AI.Training --validate [datasetPath]");
    Console.WriteLine("  ClinicManagement.AI.Training --train [--data datasetPath] [--out outputDir] [--allow-demo-data]");
}

static int RunValidate(string dataPath)
{
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
    Console.WriteLine($"Class Distribution:");
    foreach (var kvp in report.ClassDistribution)
    {
        Console.WriteLine($"  {kvp.Key}: {kvp.Value} sample(s)");
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

static int RunTrain(string dataPath, string outDir, bool allowDemo)
{
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

    var trainer = new ModelTrainer();
    var result = trainer.Train(records, json, outDir, allowDemo);

    if (!result.Success)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Training FAILED: {result.Message}");
        Console.ResetColor();
        return 1;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine(result.Message);
    Console.WriteLine($"Model exported: {result.ModelPath}");
    Console.WriteLine($"Metadata exported: {result.MetadataPath}");
    Console.ResetColor();
    return 0;
}
