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

    // Gate B keeps the legacy "intent" field for compatibility with the
    // existing trainer, while the registry uses the taxonomy-neutral "label"
    // field.  The validator requires both to agree when both are present.
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("actor")]
    public string Actor { get; set; } = "Patient";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "vi";

    [JsonPropertyName("scenarioFamily")]
    public string ScenarioFamily { get; set; } = string.Empty;

    [JsonPropertyName("split")]
    public string Split { get; set; } = "train"; // train, val, test

    [JsonPropertyName("approved")]
    public bool Approved { get; set; } = true;

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "legacy_curated";

    [JsonPropertyName("templateFamilyId")]
    public string TemplateFamilyId { get; set; } = string.Empty;

    [JsonPropertyName("semanticGroupId")]
    public string SemanticGroupId { get; set; } = string.Empty;

    [JsonPropertyName("isSynthetic")]
    public bool IsSynthetic { get; set; } = true;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonIgnore]
    public string EffectiveLabel => string.IsNullOrWhiteSpace(Label) ? Intent : Label;
}
