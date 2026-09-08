using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClinicManagement.AI.Training;

public class EvaluationMetricsDto
{
    [JsonPropertyName("microAccuracy")]
    public double MicroAccuracy { get; set; }

    [JsonPropertyName("macroAccuracy")]
    public double MacroAccuracy { get; set; }

    [JsonPropertyName("logLoss")]
    public double LogLoss { get; set; }

    [JsonPropertyName("macroF1")]
    public double MacroF1 { get; set; }

    [JsonPropertyName("macroPrecision")]
    public double MacroPrecision { get; set; }

    [JsonPropertyName("macroRecall")]
    public double MacroRecall { get; set; }

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

    [JsonPropertyName("specialtyCodes")]
    public List<string> SpecialtyCodes { get; set; } = new();

    [JsonPropertyName("metrics")]
    public EvaluationMetricsDto Metrics { get; set; } = new();
}
