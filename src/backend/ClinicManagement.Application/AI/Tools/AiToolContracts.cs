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
    DirectHumanConfirmation,
    InternalSystem
}

public enum AiActorCapability
{
    ReadClinicCatalog,
    ReadOwnAppointments,
    PrepareBooking,
    PrepareAppointmentChange,
    ExecuteConfirmedPatientAction
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
}

public sealed class AiToolInvocation
{
    public string ToolName { get; init; } = string.Empty;
    public string ToolVersion { get; init; } = "1.0";
    public string ArgumentsJson { get; init; } = "{}";
    public string? SessionId { get; init; }
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
    public string Status { get; init; } = "completed";
    public object? Data { get; init; }
    public string? ResultType { get; init; }
    public string? DisplayText { get; init; }
    public AiToolError? Error { get; init; }
    public bool RequiresConfirmation { get; init; }
    public string? ActionId { get; init; }
    public IReadOnlyList<AiToolDataSource> DataSources { get; init; } = Array.Empty<AiToolDataSource>();
    public DateTimeOffset RetrievedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static AiToolExecutionResult Failed(string code, string message, bool retryable = false) => new()
    {
        Status = "failed",
        Error = new AiToolError { Code = code, Message = message, Retryable = retryable }
    };
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
    Task<IReadOnlyList<AiToolExecutionResult>> ExecutePlannerPlanAsync(IReadOnlyList<AiPlannerToolCall> plannedCalls, string? sessionId, CancellationToken cancellationToken = default);
    Task<AiToolExecutionResult> ExecuteHumanConfirmationAsync(Guid actionId, string sessionId, string? concurrencyToken, CancellationToken cancellationToken = default);
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
