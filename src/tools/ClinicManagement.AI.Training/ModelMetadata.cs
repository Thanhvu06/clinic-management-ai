using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClinicManagement.AI.Training;

public class ClassMetricDto
{
    [JsonPropertyName("precision")]
    public double Precision { get; set; }

    [JsonPropertyName("recall")]
    public double Recall { get; set; }

    [JsonPropertyName("f1")]
    public double F1 { get; set; }

    [JsonPropertyName("support")]
    public int Support { get; set; }
}

public class EvaluationMetricsDto
{
    [JsonPropertyName("microAccuracy")]
    public double MicroAccuracy { get; set; }

    [JsonPropertyName("macroAccuracy")]
    public double MacroAccuracy { get; set; }

    [JsonPropertyName("logLoss")]
    public double LogLoss { get; set; }

    [JsonPropertyName("macroPrecision")]
    public double MacroPrecision { get; set; }

    [JsonPropertyName("macroRecall")]
    public double MacroRecall { get; set; }

    [JsonPropertyName("macroF1")]
    public double MacroF1 { get; set; }

    [JsonPropertyName("top3Accuracy")]
    public double Top3Accuracy { get; set; }

    [JsonPropertyName("perClassMetrics")]
    public Dictionary<string, ClassMetricDto> PerClassMetrics { get; set; } = new();

    [JsonPropertyName("confusionMatrix")]
    public string ConfusionMatrix { get; set; } = string.Empty;
}

public class ModelMetadata
{
    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; set; } = "1.0.0";

    [JsonPropertyName("datasetVersion")]
    public string DatasetVersion { get; set; } = "1.0.0";

    [JsonPropertyName("datasetHashSha256")]
    public string DatasetHashSha256 { get; set; } = string.Empty;

    [JsonPropertyName("trainedAtUtc")]
    public DateTime TrainedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("clinicallyValidated")]
    public bool ClinicallyValidated { get; set; } = false;

    [JsonPropertyName("approvalManifestPath")]
    public string? ApprovalManifestPath { get; set; }

    [JsonPropertyName("approvedBy")]
    public string? ApprovedBy { get; set; }

    [JsonPropertyName("approvedAtUtc")]
    public DateTime? ApprovedAtUtc { get; set; }

    [JsonPropertyName("specialtyCodes")]
    public List<string> SpecialtyCodes { get; set; } = new();

    [JsonPropertyName("metrics")]
    public EvaluationMetricsDto Metrics { get; set; } = new();
}

public class ClinicalApprovalManifest
{
    [JsonPropertyName("manifestVersion")]
    public string ManifestVersion { get; set; } = "1.0.0";

    [JsonPropertyName("datasetHashSha256")]
    public string DatasetHashSha256 { get; set; } = string.Empty;

    [JsonPropertyName("approvedBy")]
    public string ApprovedBy { get; set; } = string.Empty;

    [JsonPropertyName("approvedAtUtc")]
    public DateTime ApprovedAtUtc { get; set; }

    [JsonPropertyName("authority")]
    public string Authority { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "APPROVED";
}
