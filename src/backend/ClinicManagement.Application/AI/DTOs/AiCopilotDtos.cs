using System.ComponentModel.DataAnnotations;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Application.AI.DTOs;

public sealed class AiCopilotRequestDto
{
    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ConversationId { get; set; }

    [MaxLength(128)]
    public string? SessionId { get; set; }

    [MaxLength(256)]
    public string? CurrentRoute { get; set; }

    public AiCopilotResourceContextDto? ResourceContext { get; set; }

    [MaxLength(256)]
    public string? ResourceVersion { get; set; }

    [MaxLength(128)]
    public string? ClientTurnId { get; set; }

    [MaxLength(16)]
    public string? Locale { get; set; }

    [MaxLength(64)]
    public string? Timezone { get; set; }
}

public sealed class AiCopilotResourceContextDto
{
    [Range(1, long.MaxValue)]
    public long? AppointmentId { get; set; }

    [Range(1, long.MaxValue)]
    public long? VisitId { get; set; }

    [Range(1, long.MaxValue)]
    public long? EncounterId { get; set; }

    [Range(1, long.MaxValue)]
    public long? DiagnosticOrderId { get; set; }

    [Range(1, long.MaxValue)]
    public long? PrescriptionId { get; set; }
}

public sealed class AiCopilotDataCardDto
{
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public object? Data { get; init; }
    public IReadOnlyList<AiToolDataSource> Sources { get; init; } = Array.Empty<AiToolDataSource>();
    public DateTimeOffset RetrievedAtUtc { get; init; }
}

public sealed class AiCopilotResponseDto
{
    private string _assistantMode = AiAssistantModes.Ready;
    private string _providerState = AiProviderStatusContract.NotCalled;

    public string ConversationId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string AssistantMode { get => _assistantMode; init => _assistantMode = value; }

    /// <summary>Backward-compatible alias. It now reports lifecycle mode, never provider reachability.</summary>
    public string AssistantStatus { get => _assistantMode; init => _assistantMode = value; }

    public string ProviderState { get => _providerState; init => _providerState = value; }

    /// <summary>Backward-compatible alias for ProviderState.</summary>
    public string ProviderStatus { get => _providerState; init => _providerState = value; }

    public string PlannerMode { get; init; } = AiPlannerModes.Deterministic;
    public string Intent { get; init; } = AiChatIntentTypes.UnclearOrOutOfScope;
    public string? SubIntent { get; init; }
    public decimal Confidence { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string? Clarification { get; init; }
    public string? SafetyNotice { get; init; }
    public string? NavigationRoute { get; init; }
    public string? Navigation { get; init; }
    public IReadOnlyList<string> SuggestedPrompts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AiCopilotDataCardDto> Cards { get; init; } = Array.Empty<AiCopilotDataCardDto>();
    public IReadOnlyList<AiToolDataSource> Sources { get; init; } = Array.Empty<AiToolDataSource>();
    public IReadOnlyList<AiToolDefinition> AvailableTools { get; init; } = Array.Empty<AiToolDefinition>();
}
