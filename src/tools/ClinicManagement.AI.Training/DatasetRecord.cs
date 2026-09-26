using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClinicManagement.AI.Training;

public class DatasetRecord
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("primarySpecialtyCode")]
    public string PrimarySpecialtyCode { get; set; } = string.Empty;

    [JsonPropertyName("acceptableSpecialtyCodes")]
    public List<string> AcceptableSpecialtyCodes { get; set; } = new();

    [JsonPropertyName("urgency")]
    public string Urgency { get; set; } = "ROUTINE"; // ROUTINE, SOON, EMERGENCY

    [JsonPropertyName("redFlags")]
    public List<string> RedFlags { get; set; } = new();

    [JsonPropertyName("approved")]
    public bool Approved { get; set; }

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "SIMULATED_TEST_DATA"; // CLINICAL_GROUND_TRUTH or SIMULATED_TEST_DATA

    [JsonPropertyName("scenarioFamily")]
    public string ScenarioFamily { get; set; } = string.Empty;

    [JsonPropertyName("split")]
    public string Split { get; set; } = "train"; // train, val, test

    [JsonPropertyName("datasetVersion")]
    public string DatasetVersion { get; set; } = "1.0.0";
}
