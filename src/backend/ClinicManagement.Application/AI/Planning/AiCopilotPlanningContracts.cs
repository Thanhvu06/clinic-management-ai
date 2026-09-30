using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Application.AI.Planning;

public sealed class AiResolvedResourceContext
{
    public string? CurrentRoute { get; init; }
    public long? AppointmentId { get; init; }
    public long? VisitId { get; init; }
    public long? EncounterId { get; init; }
    public long? DiagnosticOrderId { get; init; }
    public long? PrescriptionId { get; init; }
    public string? ResourceVersion { get; init; }
}

public sealed class AiContextResolutionResult
{
    public bool IsValid { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool HasPendingConfirmation { get; init; }
    public AiResolvedResourceContext Context { get; init; } = new();

    public static AiContextResolutionResult Valid(AiResolvedResourceContext context, bool hasPendingConfirmation = false) =>
        new() { IsValid = true, Context = context, HasPendingConfirmation = hasPendingConfirmation };

    public static AiContextResolutionResult Invalid(string code, string message) =>
        new() { IsValid = false, ErrorCode = code, ErrorMessage = message };
}

public sealed class AiPlannerDecision
{
    public string PlannerMode { get; init; } = AiPlannerModes.Deterministic;
    public string Intent { get; init; } = AiChatIntentTypes.UnclearOrOutOfScope;
    public string? SubIntent { get; init; }
    public decimal Confidence { get; init; }
    public bool RequiresProvider { get; init; }
    public string? Message { get; init; }
    public string? Clarification { get; init; }
    public string? NavigationRoute { get; init; }
    public string? ErrorCode { get; init; }
    public IReadOnlyList<AiPlannerToolCall> ToolCalls { get; init; } = Array.Empty<AiPlannerToolCall>();
}

public sealed class AiCopilotPlanningContext
{
    public AiActorRole Role { get; init; }
    public string NormalizedMessage { get; init; } = string.Empty;
    public AiConversationAnalysis Analysis { get; init; } = new();
    public AiResolvedResourceContext Resource { get; init; } = new();
    public AiConversationMemoryState? Memory { get; init; }
}

public sealed class AiStructuredPlannerRequest
{
    public AiActorRole Role { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? LocalIntent { get; init; }
    public decimal? LocalConfidence { get; init; }
    public string ConversationId { get; init; } = string.Empty;
    public AiResolvedResourceContext Resource { get; init; } = new();
    public AiConversationMemoryState? Memory { get; init; }
    public IReadOnlyList<string> AllowedToolNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AiToolDefinition> AllowedTools { get; init; } = Array.Empty<AiToolDefinition>();
}

public sealed class AiStructuredPlannerResult
{
    public bool IsSuccess { get; init; }
    public bool ProviderCalled { get; init; }
    public int ProviderAttemptCount { get; init; }
    public string ProviderState { get; init; } = AiProviderStatusContract.NotCalled;
    public string? FailureReason { get; init; }
    public string FailureCode { get; init; } = AiProviderStatusContract.FailureNone;
    public bool Retryable { get; init; }
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public string? CorrelationId { get; init; }
    public AiPlannerValidationDiagnostic? Diagnostic { get; init; }
    public AiProviderHttpDiagnostic? ProviderHttp { get; init; }
    public AiPlannerDecision Decision { get; init; } = new() { PlannerMode = AiPlannerModes.Fallback };
}

public sealed class AiConversationMemoryState
{
    public string ConversationId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string? LastIntent { get; init; }
    public string? LastSubIntent { get; init; }
    public string? PendingClarification { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> ConfirmedEntities { get; init; } = new Dictionary<string, string>();
    public AiResolvedResourceContext? CurrentResource { get; init; }
    public string? SanitizedSummary { get; init; }
    public int Version { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
}

public sealed class AiConversationMemoryWriteRequest
{
    public Guid? UserId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public AiActorRole Role { get; init; }
    public string? Intent { get; init; }
    public string? SubIntent { get; init; }
    public string? PendingClarification { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> ConfirmedEntities { get; init; } = new Dictionary<string, string>();
    public AiResolvedResourceContext? CurrentResource { get; init; }
}

public sealed class AiGroundedResponse
{
    public string Message { get; init; } = string.Empty;
    public string? NavigationRoute { get; init; }
    public IReadOnlyList<AiCopilotDataCardDto> Cards { get; init; } = Array.Empty<AiCopilotDataCardDto>();
    public IReadOnlyList<AiToolDataSource> Sources { get; init; } = Array.Empty<AiToolDataSource>();
}

public interface IAiDeterministicPlanner
{
    AiPlannerDecision Plan(AiCopilotPlanningContext context);

    /// <summary>
    /// Plans a server-owned suggestion button. Never consults message text
    /// and never requires a provider. <paramref name="resource"/> must contain
    /// only resource the caller explicitly supplied and the resolver verified.
    /// A null suggestion produces the same local rejection as an unknown code.
    /// </summary>
    AiPlannerDecision PlanSuggestion(ClinicManagement.Application.AI.Suggestions.AiSuggestionDefinition? suggestion, AiResolvedResourceContext resource);
}

public interface IAiStructuredPlanner
{
    Task<AiStructuredPlannerResult> PlanAsync(AiStructuredPlannerRequest request, CancellationToken cancellationToken = default);
}

public interface IAiCopilotContextResolver
{
    Task<AiContextResolutionResult> ResolveAsync(
        AiCopilotRequestDto request,
        AiConversationMemoryState? memory,
        AiActorRole role,
        Guid? actorId,
        CancellationToken cancellationToken = default);
}

public interface IAiConversationMemoryStore
{
    Task<AiConversationMemoryState?> LoadAsync(string sessionId, Guid? userId, AiActorRole role, CancellationToken cancellationToken = default);
    Task<AiConversationMemoryState> SaveTurnAsync(AiConversationMemoryWriteRequest request, CancellationToken cancellationToken = default);
}

public interface IAiGroundedResponseComposer
{
    AiGroundedResponse Compose(AiPlannerDecision decision, IReadOnlyList<AiToolExecutionResult> results);
}

public interface IAiProviderHealth
{
    bool CanAttempt();
    void RecordSuccess();
    void RecordFailure(string? failureCode = null);
    string State { get; }
    DateTimeOffset? NextProbeAtUtc { get; }
    int ConsecutiveFailures { get; }
    string? LastFailureCode { get; }
    DateTimeOffset? LastSuccessAtUtc { get; }
    long AttemptCount { get; }
    long SuccessCount { get; }
    long FailureCount { get; }
    IReadOnlyDictionary<string, long> FailureCounts { get; }
}
