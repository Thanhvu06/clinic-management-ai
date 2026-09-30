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

/// <summary>Gemini <c>error.status</c> of a rejected HTTP call, folded into a closed set.</summary>
public enum AiProviderErrorStatus
{
    NotAvailable,
    InvalidArgument,
    FailedPrecondition,
    NotFound,
    PermissionDenied,
    Unauthenticated,
    ResourceExhausted,
    Unavailable,
    DeadlineExceeded,
    Internal,
    Other
}

/// <summary>
/// Part of the outgoing request a provider rejection points at. NotAvailable
/// means the error body named nothing; Unknown means it named something the
/// server does not classify.
/// </summary>
public enum AiProviderRejectedRequestPart
{
    NotAvailable,
    ResponseFormatSchema,
    SystemInstruction,
    Contents,
    GenerationConfig,
    Unknown
}

/// <summary>
/// Sanitized reason a provider HTTP call was rejected. Only closed values are
/// kept: the error body is read solely to pick them and is never stored.
/// </summary>
public sealed record AiProviderHttpDiagnostic
{
    public const string NotAvailable = "NotAvailable";
    public const string Other = "Other";

    public static IReadOnlySet<int> ReportedHttpStatuses { get; } = new HashSet<int> { 400, 401, 403, 404, 408, 429, 500, 502, 503, 504 };

    /// <summary>An allowlisted HTTP status code, "Other" or "NotAvailable".</summary>
    public string HttpStatus { get; init; } = NotAvailable;

    public AiProviderErrorStatus ErrorStatus { get; init; } = AiProviderErrorStatus.NotAvailable;

    public AiProviderRejectedRequestPart RejectedRequestPart { get; init; } = AiProviderRejectedRequestPart.NotAvailable;

    public static string MapHttpStatus(int? statusCode) => statusCode switch
    {
        null => NotAvailable,
        var code when ReportedHttpStatuses.Contains(code.Value) => code.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Other
    };

    public static AiProviderErrorStatus MapErrorStatus(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        null or "" => AiProviderErrorStatus.NotAvailable,
        "INVALID_ARGUMENT" => AiProviderErrorStatus.InvalidArgument,
        "FAILED_PRECONDITION" => AiProviderErrorStatus.FailedPrecondition,
        "NOT_FOUND" => AiProviderErrorStatus.NotFound,
        "PERMISSION_DENIED" => AiProviderErrorStatus.PermissionDenied,
        "UNAUTHENTICATED" => AiProviderErrorStatus.Unauthenticated,
        "RESOURCE_EXHAUSTED" => AiProviderErrorStatus.ResourceExhausted,
        "UNAVAILABLE" => AiProviderErrorStatus.Unavailable,
        "DEADLINE_EXCEEDED" => AiProviderErrorStatus.DeadlineExceeded,
        "INTERNAL" => AiProviderErrorStatus.Internal,
        _ => AiProviderErrorStatus.Other
    };

    /// <summary>
    /// Case-insensitive substring match over provider field paths and message.
    /// The first classifiable text wins; the text itself is discarded.
    /// </summary>
    public static AiProviderRejectedRequestPart MapRejectedRequestPart(IEnumerable<string?> texts)
    {
        var sawText = false;
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            sawText = true;
            var part = ClassifyRequestPart(text);
            if (part != AiProviderRejectedRequestPart.Unknown) return part;
        }

        return sawText ? AiProviderRejectedRequestPart.Unknown : AiProviderRejectedRequestPart.NotAvailable;
    }

    private static AiProviderRejectedRequestPart ClassifyRequestPart(string text)
    {
        // Most specific first: a response-format path also names generation_config.
        if (ContainsAny(text, "responseFormat", "response_format", "responseSchema", "response_schema", "responseJsonSchema", "response_json_schema"))
            return AiProviderRejectedRequestPart.ResponseFormatSchema;
        if (ContainsAny(text, "systemInstruction", "system_instruction"))
            return AiProviderRejectedRequestPart.SystemInstruction;
        if (ContainsAny(text, "contents"))
            return AiProviderRejectedRequestPart.Contents;
        if (ContainsAny(text, "generationConfig", "generation_config"))
            return AiProviderRejectedRequestPart.GenerationConfig;
        return AiProviderRejectedRequestPart.Unknown;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
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
