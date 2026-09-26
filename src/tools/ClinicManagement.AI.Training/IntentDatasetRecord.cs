using System.Text.Json.Serialization;

namespace ClinicManagement.AI.Training;

public class IntentDatasetRecord
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("intent")]
    public string Intent { get; set; } = string.Empty;

    [JsonPropertyName("scenarioFamily")]
    public string ScenarioFamily { get; set; } = string.Empty;

    [JsonPropertyName("split")]
    public string Split { get; set; } = "train"; // train, val, test

    [JsonPropertyName("approved")]
    public bool Approved { get; set; } = true;

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "CURATED_VIETNAMESE_INTENTS";
}
