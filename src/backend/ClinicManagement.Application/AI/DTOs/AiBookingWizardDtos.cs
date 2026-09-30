using System.ComponentModel.DataAnnotations;

namespace ClinicManagement.Application.AI.DTOs;

public sealed class AiBookingWizardRequestDto
{
    [Required, MaxLength(128), RegularExpression("^[A-Za-z0-9_-]+$")]
    public string SessionId { get; set; } = string.Empty;
    [Required, RegularExpression("^(start|pick|back|reason)$")]
    public string Step { get; set; } = "start";
    [MaxLength(512)] public string? OptionToken { get; set; }
    // Length and safety errors are returned without reflecting the submitted text.
    public string? Reason { get; set; }
    [MaxLength(256)] public string? CurrentRoute { get; set; }
    [MaxLength(16)] public string? Locale { get; set; }
}

public sealed record AiBookingWizardOptionDto(string Token, string Label, string? Hint = null);
public sealed class AiBookingWizardSummaryDto
{
    public string? SpecialtyName { get; set; }
    public string? DoctorName { get; set; }
    public string? SlotDate { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public bool ReasonProvided { get; set; }
}

public sealed class AiBookingWizardResponseDto
{
    public string Step { get; set; } = "specialty";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<AiBookingWizardOptionDto> Options { get; set; } = new();
    public bool CanGoBack { get; set; }
    public string? BackToken { get; set; }
    public string? ReasonToken { get; set; }
    public AiBookingWizardSummaryDto? Summary { get; set; }
    public AiActionDto? ReviewAction { get; set; }
    public List<AiActionDto> Actions { get; set; } = new();
    public IReadOnlyList<AiSuggestionItemDto> Suggestions { get; set; } = Array.Empty<AiSuggestionItemDto>();
    public string AssistantMode { get; set; } = "Ready";
    public string? ErrorCode { get; set; }
    public bool ProviderWasCalled { get; set; } = false;
}
