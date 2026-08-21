using System;

namespace ClinicManagement.Domain.Entities;

public class AiSuggestionLog
{
    public long Id { get; set; }
    public long PatientId { get; set; }
    public string InputText { get; set; } = string.Empty;
    public string SuggestedSpecialtiesJson { get; set; } = string.Empty;
    public long? SelectedSpecialtyId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Patient Patient { get; set; } = null!;
    public Specialty? SelectedSpecialty { get; set; }
}
