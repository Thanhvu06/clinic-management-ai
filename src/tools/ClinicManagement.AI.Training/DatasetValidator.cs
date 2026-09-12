using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClinicManagement.AI.Training;

public class ValidationReport
{
    public bool IsValid { get; set; } = true;
    public int TotalRecords { get; set; }
    public int ApprovedRecords { get; set; }
    public int UnapprovedRecords { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    
    // Separated distributions per requirement
    public Dictionary<string, int> TotalClassDistribution { get; set; } = new();
    public Dictionary<string, int> ApprovedClassDistribution { get; set; } = new();
    public Dictionary<string, int> TrainingClassDistribution { get; set; } = new();
    public Dictionary<string, int> SplitDistribution { get; set; } = new();
    public List<string> MissingClassesInTraining { get; set; } = new();
    public List<string> MissingClassesInTesting { get; set; } = new();
    
    // Backward compatibility alias
    public Dictionary<string, int> ClassDistribution => TotalClassDistribution;
    public double ClassImbalanceRatio { get; set; }
}

public class DatasetValidator
{
    public static readonly Regex CanonicalCodeRegex = new(@"^SP(0[1-9]|1[0-1])$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedSplits = new(StringComparer.OrdinalIgnoreCase) { "train", "val", "test" };

    public static readonly HashSet<string> CanonicalCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SP01", "SP02", "SP03", "SP04", "SP05", "SP06", "SP07", "SP08", "SP09", "SP10", "SP11"
    };

    public ValidationReport Validate(List<DatasetRecord>? records)
    {
        var report = new ValidationReport();

        // 1. Null-safe check before accessing records.Count
        if (records == null || records.Count == 0)
        {
            report.IsValid = false;
            report.TotalRecords = 0;
            report.Errors.Add("Dataset is null or empty.");
            return report;
        }

        report.TotalRecords = records.Count;

        var normalizedTextMap = new Dictionary<string, string>(); // normalized -> caseId
        var trainFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var testFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var trainClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var testClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int approvedCount = 0;
        int unapprovedCount = 0;

        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.CaseId))
            {
                report.Errors.Add("Record missing caseId.");
                report.IsValid = false;
            }

            if (string.IsNullOrWhiteSpace(r.Text))
            {
                report.Errors.Add($"Record {r.CaseId} has empty text.");
                report.IsValid = false;
            }

            // Validate code format and catalog membership
            if (string.IsNullOrWhiteSpace(r.PrimarySpecialtyCode))
            {
                report.Errors.Add($"Record {r.CaseId} has empty primarySpecialtyCode.");
                report.IsValid = false;
            }
            else
            {
                var cleanCode = r.PrimarySpecialtyCode.Trim();
                if (cleanCode.Contains('-'))
                {
                    report.Errors.Add($"Record {r.CaseId} uses invalid hyphenated code '{cleanCode}'. Must use canonical format SP01-SP11.");
                    report.IsValid = false;
                }
                else if (!CanonicalCodeRegex.IsMatch(cleanCode) || !CanonicalCodes.Contains(cleanCode))
                {
                    report.Errors.Add($"Record {r.CaseId} uses non-canonical specialty code '{cleanCode}' not present in clinic catalog.");
                    report.IsValid = false;
                }
            }

            // Validate Split
            var split = string.IsNullOrWhiteSpace(r.Split) ? "unassigned" : r.Split.Trim().ToLowerInvariant();
            if (!AllowedSplits.Contains(split))
            {
                report.Errors.Add($"Record {r.CaseId} has invalid split '{r.Split}'. Allowed splits: train, val, test.");
                report.IsValid = false;
            }

            if (r.Approved) approvedCount++;
            else unapprovedCount++;

            var code = string.IsNullOrWhiteSpace(r.PrimarySpecialtyCode) ? "UNKNOWN" : r.PrimarySpecialtyCode.Trim();

            // Total class distribution
            report.TotalClassDistribution[code] = report.TotalClassDistribution.GetValueOrDefault(code, 0) + 1;

            // Approved class distribution
            if (r.Approved)
            {
                report.ApprovedClassDistribution[code] = report.ApprovedClassDistribution.GetValueOrDefault(code, 0) + 1;

                // Training class distribution (Approved + train split)
                if (split == "train")
                {
                    report.TrainingClassDistribution[code] = report.TrainingClassDistribution.GetValueOrDefault(code, 0) + 1;
                    trainClasses.Add(code);
                }
                else if (split == "test")
                {
                    testClasses.Add(code);
                }
            }

            // Track split distribution
            report.SplitDistribution[split] = report.SplitDistribution.GetValueOrDefault(split, 0) + 1;

            // Check normalized text duplicates across all records: DUPLICATES ARE ERRORS
            if (!string.IsNullOrWhiteSpace(r.Text))
            {
                var normText = NormalizeText(r.Text);
                if (normalizedTextMap.TryGetValue(normText, out var existingCaseId))
                {
                    report.Errors.Add($"Duplicate normalized text detected between Case {r.CaseId} and Case {existingCaseId}.");
                    report.IsValid = false;
                }
                else
                {
                    normalizedTextMap[normText] = r.CaseId;
                }
            }

            // Check scenario family leakage
            if (!string.IsNullOrWhiteSpace(r.ScenarioFamily))
            {
                var fam = r.ScenarioFamily.Trim();
                if (split == "train") trainFamilies.Add(fam);
                else if (split == "val") valFamilies.Add(fam);
                else if (split == "test") testFamilies.Add(fam);
            }
        }

        report.ApprovedRecords = approvedCount;
        report.UnapprovedRecords = unapprovedCount;

        // Check leakage between splits (train vs test, train vs val, val vs test): ALL ARE ERRORS
        var trainTestLeak = trainFamilies.Intersect(testFamilies, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var fam in trainTestLeak)
        {
            report.IsValid = false;
            report.Errors.Add($"Scenario family leakage detected: '{fam}' appears in both train and test splits.");
        }

        var trainValLeak = trainFamilies.Intersect(valFamilies, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var fam in trainValLeak)
        {
            report.IsValid = false;
            report.Errors.Add($"Scenario family leakage detected: '{fam}' appears in both train and val splits.");
        }

        var valTestLeak = valFamilies.Intersect(testFamilies, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var fam in valTestLeak)
        {
            report.IsValid = false;
            report.Errors.Add($"Scenario family leakage detected: '{fam}' appears in both val and test splits.");
        }

        // Identify missing canonical classes in training / test subsets
        foreach (var code in CanonicalCodes)
        {
            if (!trainClasses.Contains(code)) report.MissingClassesInTraining.Add(code);
            if (!testClasses.Contains(code)) report.MissingClassesInTesting.Add(code);
        }

        if (report.MissingClassesInTraining.Any())
        {
            report.Warnings.Add($"Training subset lacks examples for canonical classes: {string.Join(", ", report.MissingClassesInTraining)}. Model will not predict these classes.");
        }

        // Check class imbalance strictly on TRAINING subset
        if (report.TrainingClassDistribution.Count > 1)
        {
            int maxCount = report.TrainingClassDistribution.Values.Max();
            int minCount = report.TrainingClassDistribution.Values.Min();
            report.ClassImbalanceRatio = minCount > 0 ? (double)maxCount / minCount : double.PositiveInfinity;
            if (report.ClassImbalanceRatio > 3.0)
            {
                report.Warnings.Add($"Class imbalance warning on training subset: Ratio is {report.ClassImbalanceRatio:F1}:1 (Max: {maxCount}, Min: {minCount}).");
            }
        }

        if (unapprovedCount > 0)
        {
            report.Warnings.Add($"Dataset contains {unapprovedCount} unapproved records. Only approved records will be used for training.");
        }

        if (report.Errors.Any())
        {
            report.IsValid = false;
        }

        return report;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lower = text.ToLowerInvariant().Trim();
        return Regex.Replace(lower, @"\s+", " ");
    }
}
