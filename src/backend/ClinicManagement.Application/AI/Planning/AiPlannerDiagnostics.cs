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
/// What kind of rejection error.message describes. NotAvailable means the
/// error body carried no message; Other means it matched no known pattern.
/// </summary>
public enum AiProviderRejectionKind
{
    NotAvailable,
    UnknownField,
    InvalidValue,
    UnsupportedKeyword,
    SchemaTooComplex,
    Other
}

/// <summary>
/// Sanitized reason a provider HTTP call was rejected. Only closed values are
/// kept: the error body is read solely to pick them and is never stored.
/// </summary>
public sealed record AiProviderHttpDiagnostic
{
    public const string NotAvailable = "NotAvailable";
    public const string Other = "Other";
    public const string UnknownPathSegment = "?";
    public const string Truncated = "…";
    public const int MaxFieldPathLength = 300;
    public const int MaxFieldPathSegments = 24;

    public static IReadOnlySet<int> ReportedHttpStatuses { get; } = new HashSet<int> { 400, 401, 403, 404, 408, 429, 500, 502, 503, 504 };

    /// <summary>
    /// Request field names the role planner sends and the schema keywords it
    /// may use, with their snake_case forms. Nothing else is ever echoed.
    /// </summary>
    public static IReadOnlySet<string> AllowedRequestNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "contents", "systemInstruction", "system_instruction", "generationConfig", "generation_config",
        "temperature", "maxOutputTokens", "max_output_tokens", "responseFormat", "response_format",
        "responseSchema", "response_schema", "responseMimeType", "response_mime_type", "text",
        "mimeType", "mime_type", "schema",
        "type", "title", "description", "properties", "required", "additionalProperties", "additional_properties",
        "enum", "format", "minimum", "maximum", "items", "prefixItems", "prefix_items", "minItems", "min_items",
        "maxItems", "max_items", "anyOf", "any_of", "nullable", "const"
    };

    /// <summary>An allowlisted HTTP status code, "Other" or "NotAvailable".</summary>
    public string HttpStatus { get; init; } = NotAvailable;

    public AiProviderErrorStatus ErrorStatus { get; init; } = AiProviderErrorStatus.NotAvailable;

    public AiProviderRejectedRequestPart RejectedRequestPart { get; init; } = AiProviderRejectedRequestPart.NotAvailable;

    public AiProviderRejectionKind RejectionKind { get; init; } = AiProviderRejectionKind.NotAvailable;

    /// <summary>A name from <see cref="AllowedRequestNames"/>, "Other" or "NotAvailable".</summary>
    public string RejectedName { get; init; } = NotAvailable;

    /// <summary>
    /// Rejected request path whose segments are indexes, allowlisted names or
    /// property names of the schema this request sent; anything else is "?".
    /// </summary>
    public string RejectedFieldPath { get; init; } = NotAvailable;

    // Measured by the server from the schema it sent; null unless a role
    // planner request was rejected over HTTP.
    public int? RequestSchemaSizeBytes { get; init; }
    public int? RequestSchemaToolBranches { get; init; }
    public int? RequestSchemaMaxDepth { get; init; }

    public static AiProviderRejectionKind MapRejectionKind(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return AiProviderRejectionKind.NotAvailable;
        if (ContainsAny(message, "unknown name", "cannot find field")) return AiProviderRejectionKind.UnknownField;
        if (ContainsAny(message, "invalid value", "invalid json payload")) return AiProviderRejectionKind.InvalidValue;
        if (ContainsAny(message, "not supported", "unsupported")) return AiProviderRejectionKind.UnsupportedKeyword;
        if (ContainsAny(message, "too many", "exceed", "nesting", "complex", "too large")) return AiProviderRejectionKind.SchemaTooComplex;
        return AiProviderRejectionKind.Other;
    }

    private static readonly System.Text.RegularExpressions.Regex RejectedNamePattern = new(
        "(?:unknown name|unknown field|cannot find field|unsupported keyword|unsupported field)\\s*:?\\s*\"([^\"]{0,128})\"",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>The quoted name Google gives after "Unknown name", kept only if allowlisted.</summary>
    public static string MapRejectedName(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return NotAvailable;
        try
        {
            var match = RejectedNamePattern.Match(message);
            if (!match.Success) return NotAvailable;
            return AllowedRequestNames.Contains(match.Groups[1].Value) ? match.Groups[1].Value : Other;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return Other;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex MessagePathPattern = new(
        "at '([^']{1,512})'",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// First fieldViolations[].field, else the path after "at '" in the
    /// message, normalized by <see cref="NormalizeFieldPath"/>.
    /// </summary>
    public static string MapRejectedFieldPath(string? violationField, string? message, IReadOnlySet<string> schemaPropertyNames)
    {
        if (!string.IsNullOrWhiteSpace(violationField))
            return NormalizeFieldPath(violationField, schemaPropertyNames);
        if (string.IsNullOrWhiteSpace(message)) return NotAvailable;
        try
        {
            var match = MessagePathPattern.Match(message);
            return match.Success ? NormalizeFieldPath(match.Groups[1].Value, schemaPropertyNames) : NotAvailable;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return Other;
        }
    }

    /// <summary>
    /// Splits on '.' and '[...]'. A segment survives only as an index 0-999,
    /// an allowlisted request name or a sent schema property name; others
    /// become "?". At most 24 segments and 300 characters, then "…".
    /// </summary>
    public static string NormalizeFieldPath(string? path, IReadOnlySet<string> schemaPropertyNames)
    {
        if (string.IsNullOrWhiteSpace(path)) return NotAvailable;
        var segments = SplitPath(path);
        if (segments.Count == 0) return NotAvailable;

        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < segments.Count; index++)
        {
            var (text, bracket) = segments[index];
            var kept = IsKnownSegment(text, schemaPropertyNames) ? text : UnknownPathSegment;
            var piece = bracket ? "[" + kept + "]" : (builder.Length == 0 ? kept : "." + kept);
            if (index >= MaxFieldPathSegments || builder.Length + piece.Length > MaxFieldPathLength - Truncated.Length)
                return builder.Append(Truncated).ToString();
            builder.Append(piece);
        }

        return builder.ToString();
    }

    /// <summary>True only for a value <see cref="NormalizeFieldPath"/> could have produced.</summary>
    public static bool IsNormalizedFieldPath(string? value, IReadOnlySet<string> schemaPropertyNames)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxFieldPathLength) return false;
        var body = value.EndsWith(Truncated, StringComparison.Ordinal) ? value[..^Truncated.Length] : value;
        if (body.Length == 0) return false;
        var segments = SplitPath(body);
        if (segments.Count == 0 || segments.Count > MaxFieldPathSegments) return false;
        return segments.All(x => x.Text == UnknownPathSegment || IsKnownSegment(x.Text, schemaPropertyNames)) &&
               string.Equals(Rebuild(segments), body, StringComparison.Ordinal);
    }

    private static bool IsKnownSegment(string text, IReadOnlySet<string> schemaPropertyNames) =>
        text.Length is >= 1 and <= 3 && text.All(char.IsAsciiDigit) ||
        AllowedRequestNames.Contains(text) ||
        schemaPropertyNames.Contains(text);

    private static List<(string Text, bool Bracket)> SplitPath(string path)
    {
        var segments = new List<(string Text, bool Bracket)>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0) segments.Add((current.ToString(), false));
            current.Clear();
        }

        for (var index = 0; index < path.Length; index++)
        {
            var c = path[index];
            if (c == '.')
            {
                Flush();
            }
            else if (c == '[')
            {
                Flush();
                var close = path.IndexOf(']', index + 1);
                var inner = close < 0 ? path[(index + 1)..] : path[(index + 1)..close];
                segments.Add((inner.Trim().Trim('"', '\''), true));
                index = close < 0 ? path.Length : close;
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return segments;
    }

    private static string Rebuild(IEnumerable<(string Text, bool Bracket)> segments)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var (text, bracket) in segments)
            builder.Append(bracket ? "[" + text + "]" : (builder.Length == 0 ? text : "." + text));
        return builder.ToString();
    }

    /// <summary>Keys of every "properties" object in a schema this server built.</summary>
    public static IReadOnlySet<string> CollectSchemaPropertyNames(System.Text.Json.Nodes.JsonNode? schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Visit(System.Text.Json.Nodes.JsonNode? node)
        {
            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "properties" && value is System.Text.Json.Nodes.JsonObject properties)
                            foreach (var (name, _) in properties) names.Add(name);
                        Visit(value);
                    }
                    break;
                case System.Text.Json.Nodes.JsonArray array:
                    foreach (var item in array) Visit(item);
                    break;
            }
        }

        Visit(schema);
        return names;
    }

    /// <summary>
    /// Server-side size of the sent schema: UTF-8 bytes of its JSON, tool-call
    /// branches (anyOf members, or one direct items schema) and max depth of
    /// nested objects/arrays.
    /// </summary>
    public AiProviderHttpDiagnostic WithRequestSchemaMetrics(System.Text.Json.Nodes.JsonObject schema)
    {
        var anyOfBranches = 0;
        void CountAnyOf(System.Text.Json.Nodes.JsonNode? node)
        {
            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "anyOf" && value is System.Text.Json.Nodes.JsonArray branches) anyOfBranches += branches.Count;
                        CountAnyOf(value);
                    }
                    break;
                case System.Text.Json.Nodes.JsonArray array:
                    foreach (var item in array) CountAnyOf(item);
                    break;
            }
        }

        static int Depth(System.Text.Json.Nodes.JsonNode? node) => node switch
        {
            System.Text.Json.Nodes.JsonObject obj => 1 + obj.Select(x => Depth(x.Value)).DefaultIfEmpty(0).Max(),
            System.Text.Json.Nodes.JsonArray array => 1 + array.Select(Depth).DefaultIfEmpty(0).Max(),
            _ => 0
        };

        CountAnyOf(schema);
        var directItems = schema["properties"]?["toolCalls"]?["items"] is System.Text.Json.Nodes.JsonObject items && items["anyOf"] is null;
        return this with
        {
            RequestSchemaSizeBytes = System.Text.Encoding.UTF8.GetByteCount(schema.ToJsonString()),
            RequestSchemaToolBranches = anyOfBranches > 0 ? anyOfBranches : directItems ? 1 : 0,
            RequestSchemaMaxDepth = Depth(schema)
        };
    }

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
