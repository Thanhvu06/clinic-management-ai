namespace ClinicManagement.Application.AI.Planning;

/// <summary>
/// Where a provider plan was rejected. Closed, server-owned set: diagnostics
/// never carry provider text, user text, JSON paths or exception messages.
/// </summary>
public enum AiPlannerValidationStage
{
    ProviderEnvelope,
    GeneratedJson,
    PlannerSchema,
    ToolPlan,
    ResourceBinding
}

public enum AiPlannerValidationReason
{
    MissingCandidate,
    MissingText,
    UnsupportedFinishReason,
    OutputTruncated,
    MalformedJson,
    InvalidFieldType,
    DuplicateField,
    MissingRequiredField,
    MissingSchemaVersion,
    UnsupportedSchemaVersion,
    MissingConfidence,
    InvalidConfidence,
    InvalidIntent,
    MissingClarification,
    ClarificationWithTools,
    ToolLimitExceeded,
    ToolNotAllowed,
    UnsupportedToolVersion,
    InvalidArguments,
    ForbiddenArgument,
    UnknownArgument,
    MissingArgument,
    ResourceContextRequired,
    ResourceMismatch,
    ResourceContextChanged
}

/// <summary>Server-known output fields; used instead of provider property names.</summary>
public enum AiPlannerOutputField
{
    Root,
    PlannerSchemaVersion,
    PlannerConfidence,
    PrimaryIntent,
    IsClear,
    Clarification,
    Reply,
    ToolCalls,
    ToolName,
    ToolVersion,
    ToolArguments
}

/// <summary>Gemini candidate finish reason folded into a closed set.</summary>
public enum AiProviderFinishReason
{
    NotReported,
    Stop,
    MaxTokens,
    Safety,
    Recitation,
    Blocked,
    Other
}

public sealed record AiPlannerValidationDiagnostic(AiPlannerValidationStage Stage, AiPlannerValidationReason Reason)
{
    public AiPlannerOutputField? Field { get; init; }

    /// <summary>Zero-based position of the first rejected tool call.</summary>
    public int? RejectedToolIndex { get; init; }

    public int? ToolCount { get; init; }

    /// <summary>Only a canonical name from the request allowlist; never provider-invented text.</summary>
    public string? ToolName { get; init; }

    public AiProviderFinishReason FinishReason { get; init; } = AiProviderFinishReason.NotReported;

    public static AiProviderFinishReason MapFinishReason(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        null or "" or "FINISH_REASON_UNSPECIFIED" => AiProviderFinishReason.NotReported,
        "STOP" => AiProviderFinishReason.Stop,
        "MAX_TOKENS" => AiProviderFinishReason.MaxTokens,
        "SAFETY" or "IMAGE_SAFETY" => AiProviderFinishReason.Safety,
        "RECITATION" or "IMAGE_RECITATION" => AiProviderFinishReason.Recitation,
        "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" or "LANGUAGE" or "IMAGE_PROHIBITED_CONTENT" => AiProviderFinishReason.Blocked,
        _ => AiProviderFinishReason.Other
    };
}

/// <summary>
/// Application error codes a role planner rejection can surface. These are
/// the existing wire values; the canary allowlists exactly this set.
/// </summary>
public static class AiPlannerErrorCodes
{
    public const string InvalidProviderSchema = "INVALID_PROVIDER_SCHEMA";
    public const string InvalidProviderPlan = "INVALID_PROVIDER_PLAN";
    public const string InvalidProviderClarification = "INVALID_PROVIDER_CLARIFICATION";
    public const string ToolLimitExceeded = "PLANNER_TOOL_LIMIT_EXCEEDED";
    public const string ToolNotAllowed = "PLANNER_TOOL_NOT_ALLOWED";
    public const string ToolVersionNotSupported = "TOOL_VERSION_NOT_SUPPORTED";
    public const string InvalidToolArguments = "INVALID_TOOL_ARGUMENTS";
    public const string ForbiddenToolArgument = "FORBIDDEN_TOOL_ARGUMENT";
    public const string UnknownToolArgument = "UNKNOWN_TOOL_ARGUMENT";
    public const string MissingToolArgument = "MISSING_TOOL_ARGUMENT";
    public const string ResourceContextRequired = "RESOURCE_CONTEXT_REQUIRED";
    public const string ProviderResourceMismatch = "PROVIDER_RESOURCE_MISMATCH";
    public const string ResourceContextChanged = "RESOURCE_CONTEXT_CHANGED";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        InvalidProviderSchema, InvalidProviderPlan, InvalidProviderClarification,
        ToolLimitExceeded, ToolNotAllowed, ToolVersionNotSupported, InvalidToolArguments,
        ForbiddenToolArgument, UnknownToolArgument, MissingToolArgument,
        ResourceContextRequired, ProviderResourceMismatch, ResourceContextChanged
    };
}
