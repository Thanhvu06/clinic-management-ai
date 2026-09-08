using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClinicManagement.AI.Training;

public class ValidationReport
{
    public bool IsValid { get; set; }
    public int TotalRecords { get; set; }
    public int ApprovedRecords { get; set; }
    public int UnapprovedRecords { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, int> ClassDistribution { get; set; } = new();
    public Dictionary<string, int> SplitDistribution { get; set; } = new();
    public double ClassImbalanceRatio { get; set; }
}

public class DatasetValidator
{
    public ValidationReport Validate(List<DatasetRecord> records)
    {
        var report = new ValidationReport
        {
            TotalRecords = records.Count,
            IsValid = true
        };

        if (records == null || records.Count == 0)
        {
            report.IsValid = false;
            report.Errors.Add("Dataset is empty.");
            return report;
        }

        var normalizedTextMap = new Dictionary<string, string>(); // normalized -> caseId
        var trainFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var testFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int approvedCount = 0;
        int unapprovedCount = 0;

        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.CaseId))
            {
                report.Errors.Add($"Record missing caseId.");
                report.IsValid = false;
            }

            if (string.IsNullOrWhiteSpace(r.Text))
            {
                report.Errors.Add($"Record {r.CaseId} has empty text.");
                report.IsValid = false;
            }

            if (string.IsNullOrWhiteSpace(r.PrimarySpecialtyCode))
            {
                report.Errors.Add($"Record {r.CaseId} has empty primarySpecialtyCode.");
                report.IsValid = false;
            }

            if (r.Approved) approvedCount++;
            else unapprovedCount++;

            // Track class distribution
            var code = string.IsNullOrWhiteSpace(r.PrimarySpecialtyCode) ? "UNKNOWN" : r.PrimarySpecialtyCode;
            report.ClassDistribution[code] = report.ClassDistribution.GetValueOrDefault(code, 0) + 1;

            // Track split distribution
            var split = string.IsNullOrWhiteSpace(r.Split) ? "unassigned" : r.Split.ToLowerInvariant();
            report.SplitDistribution[split] = report.SplitDistribution.GetValueOrDefault(split, 0) + 1;

            // Check normalized text duplicates
            var normText = NormalizeText(r.Text);
            if (normalizedTextMap.TryGetValue(normText, out var existingCaseId))
            {
                report.Warnings.Add($"Duplicate text detected between Case {r.CaseId} and Case {existingCaseId}.");
            }
            else
            {
                normalizedTextMap[normText] = r.CaseId;
            }

            // Check scenario family leakage
            if (!string.IsNullOrWhiteSpace(r.ScenarioFamily))
            {
                if (split == "train")
                {
                    trainFamilies.Add(r.ScenarioFamily);
                }
                else if (split == "test")
                {
                    testFamilies.Add(r.ScenarioFamily);
                }
            }
        }

        report.ApprovedRecords = approvedCount;
        report.UnapprovedRecords = unapprovedCount;

        // Check leakage between train and test
        var leakedFamilies = trainFamilies.Intersect(testFamilies, StringComparer.OrdinalIgnoreCase).ToList();
        if (leakedFamilies.Any())
        {
            report.IsValid = false;
            foreach (var fam in leakedFamilies)
            {
                report.Errors.Add($"Scenario family leakage detected: '{fam}' appears in both train and test splits.");
            }
        }

        // Check class imbalance
        if (report.ClassDistribution.Count > 1)
        {
            int maxCount = report.ClassDistribution.Values.Max();
            int minCount = report.ClassDistribution.Values.Min();
            report.ClassImbalanceRatio = minCount > 0 ? (double)maxCount / minCount : double.PositiveInfinity;
            if (report.ClassImbalanceRatio > 3.0)
            {
                report.Warnings.Add($"Class imbalance warning: Ratio is {report.ClassImbalanceRatio:F1}:1 (Max: {maxCount}, Min: {minCount}).");
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
