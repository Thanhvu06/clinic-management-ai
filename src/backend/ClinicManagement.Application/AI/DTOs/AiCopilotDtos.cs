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

    /// <summary>
    /// Server-owned suggestion code. When present it alone selects the read
    /// action; a valid role-owned code replaces Message with the catalog label.
    /// </summary>
    [MaxLength(64)]
    public string? SuggestionCode { get; set; }
}

public sealed class AiSuggestionItemDto
{
    public string Code { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Group { get; init; }
}

public sealed class AiCopilotSuggestionsRequestDto
{
    [MaxLength(256)]
    public string? CurrentRoute { get; set; }

    public AiCopilotResourceContextDto? ResourceContext { get; set; }
}

public sealed class AiCopilotSuggestionsResponseDto
{
    public string Role { get; init; } = string.Empty;
    public IReadOnlyList<AiSuggestionItemDto> Suggestions { get; init; } = Array.Empty<AiSuggestionItemDto>();
    /// <summary>Role-owned codes accepted for recognised typed requests; never rendered as chips.</summary>
    public IReadOnlyList<AiSuggestionItemDto> TypedSuggestions { get; init; } = Array.Empty<AiSuggestionItemDto>();
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

public sealed class AiCopilotPlannerDiagnosticDto
{
    public string Stage { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string? Field { get; init; }
    public int? RejectedToolIndex { get; init; }
    public int? ToolCount { get; init; }
    public string? ToolName { get; init; }
    public string FinishReason { get; init; } = string.Empty;

    /// <summary>Allowlisted provider HTTP status code, "Other" or "NotAvailable".</summary>
    public string ProviderHttpStatus { get; init; } = ClinicManagement.Application.AI.Planning.AiProviderHttpDiagnostic.NotAvailable;
    public string ProviderErrorStatus { get; init; } = nameof(ClinicManagement.Application.AI.Planning.AiProviderErrorStatus.NotAvailable);
    public string ProviderRejectedRequestPart { get; init; } = nameof(ClinicManagement.Application.AI.Planning.AiProviderRejectedRequestPart.NotAvailable);
    public string ProviderRejectionKind { get; init; } = nameof(ClinicManagement.Application.AI.Planning.AiProviderRejectionKind.NotAvailable);

    /// <summary>Allowlisted request field or schema keyword, "Other" or "NotAvailable".</summary>
    public string ProviderRejectedName { get; init; } = ClinicManagement.Application.AI.Planning.AiProviderHttpDiagnostic.NotAvailable;

    /// <summary>Normalized path; unknown segments are "?".</summary>
    public string ProviderRejectedFieldPath { get; init; } = ClinicManagement.Application.AI.Planning.AiProviderHttpDiagnostic.NotAvailable;
    public int? RequestSchemaSizeBytes { get; init; }
    public int? RequestSchemaToolBranches { get; init; }
    public int? RequestSchemaMaxDepth { get; init; }

    /// <summary>
    /// A provider HTTP rejection has no validation stage, so those fields
    /// read "NotAvailable" rather than borrowing a validation code.
    /// </summary>
    public static AiCopilotPlannerDiagnosticDto? From(
        ClinicManagement.Application.AI.Planning.AiPlannerValidationDiagnostic? diagnostic,
        ClinicManagement.Application.AI.Planning.AiProviderHttpDiagnostic? providerHttp = null)
    {
        if (diagnostic is null && providerHttp is null) return null;
        const string notAvailable = ClinicManagement.Application.AI.Planning.AiProviderHttpDiagnostic.NotAvailable;
        return new AiCopilotPlannerDiagnosticDto
        {
            Stage = diagnostic?.Stage.ToString() ?? notAvailable,
            Reason = diagnostic?.Reason.ToString() ?? notAvailable,
            Field = diagnostic?.Field?.ToString(),
            RejectedToolIndex = diagnostic?.RejectedToolIndex,
            ToolCount = diagnostic?.ToolCount,
            ToolName = diagnostic?.ToolName,
            FinishReason = diagnostic?.FinishReason.ToString() ?? notAvailable,
            ProviderHttpStatus = providerHttp?.HttpStatus ?? notAvailable,
            ProviderErrorStatus = (providerHttp?.ErrorStatus ?? ClinicManagement.Application.AI.Planning.AiProviderErrorStatus.NotAvailable).ToString(),
            ProviderRejectedRequestPart = (providerHttp?.RejectedRequestPart ?? ClinicManagement.Application.AI.Planning.AiProviderRejectedRequestPart.NotAvailable).ToString(),
            ProviderRejectionKind = (providerHttp?.RejectionKind ?? ClinicManagement.Application.AI.Planning.AiProviderRejectionKind.NotAvailable).ToString(),
            ProviderRejectedName = providerHttp?.RejectedName ?? notAvailable,
            ProviderRejectedFieldPath = providerHttp?.RejectedFieldPath ?? notAvailable,
            RequestSchemaSizeBytes = providerHttp?.RequestSchemaSizeBytes,
            RequestSchemaToolBranches = providerHttp?.RequestSchemaToolBranches,
            RequestSchemaMaxDepth = providerHttp?.RequestSchemaMaxDepth
        };
    }
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

    public string ProviderFailureCode { get; init; } = AiProviderStatusContract.FailureNone;
    public string ExecutionMode { get; init; } = AiProviderStatusContract.ExecutionManualHandoff;
    public bool FallbackActive { get; init; }
    public bool Retryable { get; init; }
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public string? CorrelationId { get; init; }
    public bool ProviderWasCalled { get; init; }
    public int ProviderAttemptCount { get; init; }
    public IReadOnlyList<string> ExecutedToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Sanitized, closed-code reason a provider plan was rejected. Null when
    /// nothing was rejected. Never contains provider or user text.
    /// </summary>
    public AiCopilotPlannerDiagnosticDto? PlannerDiagnostic { get; init; }

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

    /// <summary>Server-computed suggestion buttons for the caller's role and verified context.</summary>
    public IReadOnlyList<AiSuggestionItemDto> Suggestions { get; init; } = Array.Empty<AiSuggestionItemDto>();
    public IReadOnlyList<AiCopilotDataCardDto> Cards { get; init; } = Array.Empty<AiCopilotDataCardDto>();
    public IReadOnlyList<AiToolDataSource> Sources { get; init; } = Array.Empty<AiToolDataSource>();
    public IReadOnlyList<AiToolDefinition> AvailableTools { get; init; } = Array.Empty<AiToolDefinition>();
}
