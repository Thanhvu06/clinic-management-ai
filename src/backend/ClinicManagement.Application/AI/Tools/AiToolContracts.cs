using System.Text.Json;

namespace ClinicManagement.Application.AI.Tools;

public enum AiToolAccessMode
{
    Public,
    Authenticated,
    RoleRestricted
}

public enum AiToolRiskLevel
{
    Low,
    Medium,
    High
}

public enum AiToolConfirmationRequirement
{
    None,
    ExplicitUserConfirmation,
    ExistingBookingConfirmation
}

public enum AiToolInvocationChannel
{
    Planner,
    DirectHumanPreparation,
    DirectHumanConfirmation,
    InternalSystem
}

public enum AiActorCapability
{
    ReadClinicCatalog,
    ReadOwnAppointments,
    PrepareBooking,
    PrepareAppointmentChange,
    ExecuteConfirmedPatientAction,
    PrepareReceptionAction,
    PrepareDoctorAction,
    PrepareDiagnosticAction,
    PreparePharmacyAction,
    ExecuteConfirmedRoleAction,
    ReadReceptionWorkspace,
    ReadDoctorWorkspace,
    ReadDiagnosticWorkspace,
    ReadPharmacyWorkspace,
    ReadAdminMetrics
}

public enum AiActorRole
{
    Patient,
    Receptionist,
    Doctor,
    DiagnosticTechnician,
    Pharmacist,
    Admin
}

public sealed record AiToolDataSource(string Name, string Kind, string Status = "verified");

public enum AiToolArgumentType
{
    String,
    Integer,
    Boolean
}

/// <summary>
/// Closed, server-owned argument metadata for planner calls.  This is
/// deliberately separate from the provider prompt: the executor and planner
/// preflight use this metadata as the trust boundary.
/// </summary>
public sealed record AiToolArgumentDefinition(
    string Name,
    AiToolArgumentType Type,
    bool Required = false,
    bool ServerBound = false);

public sealed class AiToolResourceBinding
{
    public IReadOnlyList<string> ServerBoundArgumentNames { get; init; } = Array.Empty<string>();
    public bool RequiresCurrentResource { get; init; }

    public static AiToolResourceBinding None { get; } = new();
}

public sealed class AiToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public string Description { get; init; } = string.Empty;
    public AiToolAccessMode AccessMode { get; init; }
    public AiToolRiskLevel RiskLevel { get; init; }
    public AiToolConfirmationRequirement Confirmation { get; init; }
    public bool Enabled { get; init; } = true;
    public IReadOnlySet<AiActorRole> AllowedRoles { get; init; } = new HashSet<AiActorRole>();
    public IReadOnlySet<AiActorCapability> Capabilities { get; init; } = new HashSet<AiActorCapability>();
    public IReadOnlyList<AiToolDataSource> DataSources { get; init; } = Array.Empty<AiToolDataSource>();
    public IReadOnlyList<AiToolArgumentDefinition> ArgumentSchema { get; init; } = Array.Empty<AiToolArgumentDefinition>();
    public AiToolResourceBinding ResourceBinding { get; init; } = AiToolResourceBinding.None;
}

public sealed class AiToolInvocation
{
    public string ToolName { get; init; } = string.Empty;
    public string ToolVersion { get; init; } = "1.0";
    public string ArgumentsJson { get; init; } = "{}";
    public string? SessionId { get; init; }
    public string? ConversationId { get; init; }
    public string? CorrelationId { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed class AiToolExecutionContext
{
    public Guid? ActorId { get; init; }
    public bool IsAuthenticated { get; init; }
    public IReadOnlySet<AiActorRole> Roles { get; init; } = new HashSet<AiActorRole>();
    public string? SessionId { get; init; }
    public long? FacilityId { get; init; }
    public AiToolInvocationChannel InvocationChannel { get; init; } = AiToolInvocationChannel.Planner;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}

public sealed class AiToolArgumentValidationResult
{
    public bool IsValid { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static AiToolArgumentValidationResult Valid() => new() { IsValid = true };
    public static AiToolArgumentValidationResult Invalid(string code, string message) => new()
    {
        IsValid = false,
        Code = code,
        Message = message
    };
}

public sealed class AiToolError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool Retryable { get; init; }
}

public sealed class AiToolExecutionResult
{
    public string? ToolName { get; set; }
    public string Status { get; init; } = "completed";
    public object? Data { get; init; }
    public string? ResultType { get; init; }
    public string? DisplayText { get; init; }
    public AiToolError? Error { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool IsIdempotentReplay { get; init; }
    public string? ActionId { get; init; }
    public AiToolActionPreview? Preview { get; init; }
    public IReadOnlyList<AiToolDataSource> DataSources { get; init; } = Array.Empty<AiToolDataSource>();
    public DateTimeOffset RetrievedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static AiToolExecutionResult Failed(string code, string message, bool retryable = false) => new()
    {
        Status = "failed",
        Error = new AiToolError { Code = code, Message = message, Retryable = retryable }
    };
}

public sealed class AiToolActionPreview
{
    public string ToolName { get; init; } = string.Empty;
    public string Status { get; init; } = "pending_confirmation";
    public string ResourceType { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public AiToolActionPreviewResource Resource { get; init; } = new();
    public IReadOnlyList<AiToolActionPreviewChange> Changes { get; init; } = Array.Empty<AiToolActionPreviewChange>();
    public string? ResourceVersion { get; init; }
    public string Consequence { get; init; } = string.Empty;
    public string ConfirmationSummary { get; init; } = string.Empty;
    public DateTimeOffset ValidatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public IReadOnlyList<AiToolDataSource> Sources { get; init; } = Array.Empty<AiToolDataSource>();
}

public sealed class AiToolActionPreviewResource
{
    public string Identity { get; init; } = string.Empty;
    public string? Facility { get; init; }
    public string? Department { get; init; }
    public string? Subject { get; init; }
    public string? Encounter { get; init; }
    public string? CurrentStatus { get; init; }
}

public sealed class AiToolActionPreviewChange
{
    public string Kind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<AiToolActionPreviewItem> Items { get; init; } = Array.Empty<AiToolActionPreviewItem>();
}

public sealed class AiToolActionPreviewItem
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public int? Quantity { get; init; }
    public string? Unit { get; init; }
}

public interface IAiToolHandler
{
    AiToolDefinition Definition { get; }
    AiToolArgumentValidationResult ValidateArguments(AiToolInvocation invocation, AiToolExecutionContext context);
    Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken = default);
}

public interface IAiToolRegistry
{
    IReadOnlyCollection<AiToolDefinition> GetDefinitions();
    bool TryGet(string canonicalName, out AiToolDefinition definition);
    bool TryGetHandler(string canonicalName, out IAiToolHandler handler);
}

public interface IAiToolExecutor
{
    Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, CancellationToken cancellationToken = default);
    Task<AiToolExecutionResult> ExecuteDirectPreparationAsync(AiToolInvocation invocation, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiToolExecutionResult>> ExecutePlannerPlanAsync(IReadOnlyList<AiPlannerToolCall> plannedCalls, string? sessionId, CancellationToken cancellationToken = default);
    Task<AiToolExecutionResult> ExecuteHumanConfirmationAsync(Guid actionId, string sessionId, string? concurrencyToken, CancellationToken cancellationToken = default);
    Task<AiToolExecutionResult> ExecuteRoleActionConfirmationAsync(Guid actionId, string sessionId, string? confirmationToken, CancellationToken cancellationToken = default);
}

public interface IAiPendingActionCancellationService
{
    Task<AiToolExecutionResult> CancelPatientActionAsync(Guid actionId, string sessionId, CancellationToken cancellationToken = default);
    Task<AiToolExecutionResult> CancelRoleActionAsync(Guid actionId, string sessionId, CancellationToken cancellationToken = default);
}

public sealed class AiSafetyGuardResult
{
    public bool IsEmergency { get; init; }
    public bool IsPromptInjection { get; init; }
    public string? MatchedCategory { get; init; }
}

public interface IAiSafetyGuard
{
    AiSafetyGuardResult Inspect(string? message);
}

public sealed class AiPlannerToolCall
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public JsonElement Arguments { get; init; }
}
