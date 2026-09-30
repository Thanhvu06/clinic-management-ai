using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Application.AI.Planning;

/// <summary>Provider-facing projection of one granted tool. Server-bound arguments are removed.</summary>
public sealed class AiRolePlannerToolContract
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<AiRolePlannerArgumentContract> Arguments { get; init; } = Array.Empty<AiRolePlannerArgumentContract>();
    public bool RequiresCurrentResource { get; init; }
}

public sealed record AiRolePlannerArgumentContract(string Name, AiToolArgumentType Type, bool Required);

/// <summary>Server-owned, identifier-free facts the planner may see.</summary>
public sealed class AiRolePlannerServerContext
{
    public string? LocalIntent { get; init; }
    public decimal? LocalConfidence { get; init; }
    public bool HasAppointment { get; init; }
    public bool HasVisit { get; init; }
    public bool HasDiagnosticOrder { get; init; }
    public bool HasPrescription { get; init; }
    public string? LastIntent { get; init; }
    public string? LastSubIntent { get; init; }
    public string? PendingClarification { get; init; }
    public int? ConversationVersion { get; init; }
}

/// <summary>
/// Typed role Copilot planning request. The server selects this contract; the
/// legacy patient chat keeps using ChatWithAiAsync and its booking fields.
/// </summary>
public sealed class AiRolePlannerProviderRequest
{
    public AiActorRole Role { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<AiRolePlannerToolContract> AllowedTools { get; init; } = Array.Empty<AiRolePlannerToolContract>();
    public IReadOnlyList<string> AllowedIntents { get; init; } = Array.Empty<string>();
    public AiRolePlannerServerContext Context { get; init; } = new();
}

/// <summary>
/// Planner fields read from generated JSON. Null means the field was absent or
/// JSON null; nothing is defaulted here.
/// </summary>
public sealed class AiRolePlannerOutput
{
    public string? PlannerSchemaVersion { get; init; }
    public decimal? PlannerConfidence { get; init; }
    public string? PrimaryIntent { get; init; }
    public bool? IsClear { get; init; }
    public string? Clarification { get; init; }
    public string? Reply { get; init; }
    public IReadOnlyList<AiPlannerToolCall>? ToolCalls { get; init; }
}

/// <summary>Operational fields are set by the provider adapter only, never from generated JSON.</summary>
public sealed class AiRolePlannerProviderResult
{
    public bool IsSuccess { get; init; }
    public string Status { get; init; } = "Success";
    public string FailureCode { get; init; } = AiProviderStatusContract.FailureNone;
    public bool Retryable { get; init; }
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public string? CorrelationId { get; init; }
    public bool ProviderWasCalled { get; init; }
    public int ProviderAttemptCount { get; init; }
    public AiPlannerValidationDiagnostic? Diagnostic { get; init; }
    public AiRolePlannerOutput? Output { get; init; }
}

public static class AiRolePlannerContract
{
    public const string SchemaVersion = "1.0";
    public const int MaxToolCalls = 3;
    public const string MimeType = "application/json";

    /// <summary>The same canonical intent set the planner validator accepts.</summary>
    public static IReadOnlyList<string> AllowedIntents { get; } =
        AiChatIntentTypes.All.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public static bool IsAllowedIntent(string? intent) =>
        !string.IsNullOrWhiteSpace(intent) && AllowedIntents.Contains(intent, StringComparer.Ordinal);

    /// <summary>
    /// Intersection of the request's role tools, the enabled definitions and
    /// the global planner policy. Write/prepare/confirm tools are never in
    /// AiPlannerPolicy, so they cannot reach the provider contract.
    /// </summary>
    public static IReadOnlyList<AiRolePlannerToolContract> BuildToolContracts(
        IEnumerable<AiToolDefinition> definitions,
        IEnumerable<string> allowedNames)
    {
        var allowed = allowedNames
            .Where(AiPlannerPolicy.IsAllowed)
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return definitions
            .Where(x => x.Enabled && allowed.Contains(x.Name) && AiPlannerPolicy.IsAllowed(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => new AiRolePlannerToolContract
            {
                Name = x.Name,
                Version = x.Version,
                Description = x.Description,
                Arguments = x.ArgumentSchema
                    .Where(argument => !argument.ServerBound)
                    .Select(argument => new AiRolePlannerArgumentContract(argument.Name, argument.Type, argument.Required))
                    .ToArray(),
                RequiresCurrentResource = x.ResourceBinding.RequiresCurrentResource
            })
            .ToArray();
    }

    /// <summary>
    /// JSON Schema for generationConfig.responseFormat.text.schema. It only
    /// uses keywords the Gemini structured-output guide lists (type, enum,
    /// properties, required, additionalProperties, items, maxItems, minimum,
    /// maximum, anyOf, description). It narrows output; it does not replace
    /// the server validator.
    /// </summary>
    public static JsonObject BuildResponseSchema(IReadOnlyList<AiRolePlannerToolContract> tools, IReadOnlyList<string> intents)
    {
        var toolCalls = new JsonObject { ["type"] = "array", ["maxItems"] = tools.Count == 0 ? 0 : MaxToolCalls };
        if (tools.Count == 1)
            toolCalls["items"] = ToolCallSchema(tools[0]);
        else if (tools.Count > 1)
            toolCalls["items"] = new JsonObject { ["anyOf"] = new JsonArray(tools.Select(x => (JsonNode)ToolCallSchema(x)).ToArray()) };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["plannerSchemaVersion"] = new JsonObject { ["type"] = "string", ["enum"] = Strings(SchemaVersion) },
                ["primaryIntent"] = new JsonObject { ["type"] = "string", ["enum"] = Strings(intents.ToArray()) },
                ["plannerConfidence"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                ["isClear"] = new JsonObject { ["type"] = "boolean" },
                ["clarification"] = new JsonObject
                {
                    ["type"] = Strings("string", "null"),
                    ["description"] = "Câu hỏi làm rõ khi isClear=false; null khi yêu cầu đã rõ."
                },
                ["toolCalls"] = toolCalls,
                ["reply"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Một câu dẫn ngắn; dữ liệu nghiệp vụ do hệ thống trả từ công cụ."
                }
            },
            ["required"] = Strings("plannerSchemaVersion", "primaryIntent", "plannerConfidence", "isClear", "clarification", "toolCalls", "reply"),
            ["additionalProperties"] = false
        };
    }

    private static JsonObject ToolCallSchema(AiRolePlannerToolContract tool)
    {
        var argumentProperties = new JsonObject();
        foreach (var argument in tool.Arguments)
            argumentProperties[argument.Name] = new JsonObject { ["type"] = JsonType(argument.Type) };
        var arguments = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = argumentProperties,
            ["additionalProperties"] = false
        };
        var required = tool.Arguments.Where(x => x.Required).Select(x => x.Name).ToArray();
        if (required.Length > 0)
            arguments["required"] = Strings(required);

        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = tool.Description,
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string", ["enum"] = Strings(tool.Name) },
                ["version"] = new JsonObject { ["type"] = "string", ["enum"] = Strings(tool.Version) },
                ["arguments"] = arguments
            },
            ["required"] = Strings("name", "version", "arguments"),
            ["additionalProperties"] = false
        };
    }

    private static string JsonType(AiToolArgumentType type) => type switch
    {
        AiToolArgumentType.String => "string",
        AiToolArgumentType.Integer => "integer",
        AiToolArgumentType.Boolean => "boolean",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static JsonArray Strings(params string[] values) => new(values.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());

    private static readonly JsonDocumentOptions StrictJson = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16
    };

    /// <summary>
    /// Reads generated JSON into typed planner fields. Wrong JSON types are
    /// rejected; missing fields stay null for the planner validator to name.
    /// Unknown properties are ignored and never copied anywhere.
    /// </summary>
    public static bool TryParseOutput(string text, out AiRolePlannerOutput? output, out AiPlannerValidationDiagnostic? diagnostic)
    {
        output = null;
        diagnostic = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripOuterFence(text), StrictJson);
        }
        catch (JsonException)
        {
            diagnostic = Json(AiPlannerValidationReason.MalformedJson, AiPlannerOutputField.Root);
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostic = Json(AiPlannerValidationReason.InvalidFieldType, AiPlannerOutputField.Root);
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    diagnostic = Json(AiPlannerValidationReason.DuplicateField, AiPlannerOutputField.Root);
                    return false;
                }
            }

            if (!TryString(root, "plannerSchemaVersion", AiPlannerOutputField.PlannerSchemaVersion, out var version, ref diagnostic) ||
                !TryDecimal(root, "plannerConfidence", out var confidence, ref diagnostic) ||
                !TryString(root, "primaryIntent", AiPlannerOutputField.PrimaryIntent, out var intent, ref diagnostic) ||
                !TryBool(root, "isClear", out var isClear, ref diagnostic) ||
                !TryString(root, "clarification", AiPlannerOutputField.Clarification, out var clarification, ref diagnostic) ||
                !TryString(root, "reply", AiPlannerOutputField.Reply, out var reply, ref diagnostic) ||
                !TryToolCalls(root, out var toolCalls, ref diagnostic))
                return false;

            output = new AiRolePlannerOutput
            {
                PlannerSchemaVersion = version,
                PlannerConfidence = confidence,
                PrimaryIntent = intent,
                IsClear = isClear,
                Clarification = clarification,
                Reply = reply,
                ToolCalls = toolCalls
            };
            return true;
        }
    }

    private static string StripOuterFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal) || trimmed.Length < 6)
            return trimmed;
        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0) return trimmed;
        var header = trimmed[3..firstLineEnd].Trim();
        if (header.Length > 0 && !header.Equals("json", StringComparison.OrdinalIgnoreCase)) return trimmed;
        return trimmed[(firstLineEnd + 1)..^3].Trim();
    }

    private static AiPlannerValidationDiagnostic Json(AiPlannerValidationReason reason, AiPlannerOutputField field, int? index = null) =>
        new(AiPlannerValidationStage.GeneratedJson, reason) { Field = field, RejectedToolIndex = index };

    private static bool TryFind(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return value.ValueKind != JsonValueKind.Null;
            }
        }
        value = default;
        return false;
    }

    private static bool TryString(JsonElement root, string name, AiPlannerOutputField field, out string? value, ref AiPlannerValidationDiagnostic? diagnostic)
    {
        value = null;
        if (!TryFind(root, name, out var element)) return true;
        if (element.ValueKind != JsonValueKind.String)
        {
            diagnostic = Json(AiPlannerValidationReason.InvalidFieldType, field);
            return false;
        }
        value = element.GetString();
        return true;
    }

    private static bool TryDecimal(JsonElement root, string name, out decimal? value, ref AiPlannerValidationDiagnostic? diagnostic)
    {
        value = null;
        if (!TryFind(root, name, out var element)) return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var parsed))
        {
            diagnostic = Json(AiPlannerValidationReason.InvalidFieldType, AiPlannerOutputField.PlannerConfidence);
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool TryBool(JsonElement root, string name, out bool? value, ref AiPlannerValidationDiagnostic? diagnostic)
    {
        value = null;
        if (!TryFind(root, name, out var element)) return true;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            diagnostic = Json(AiPlannerValidationReason.InvalidFieldType, AiPlannerOutputField.IsClear);
            return false;
        }
        value = element.GetBoolean();
        return true;
    }

    private static bool TryToolCalls(JsonElement root, out IReadOnlyList<AiPlannerToolCall>? calls, ref AiPlannerValidationDiagnostic? diagnostic)
    {
        calls = null;
        if (!TryFind(root, "toolCalls", out var element)) return true;
        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostic = Json(AiPlannerValidationReason.InvalidFieldType, AiPlannerOutputField.ToolCalls);
            return false;
        }

        var parsed = new List<AiPlannerToolCall>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                diagnostic = Json(AiPlannerValidationReason.InvalidFieldType, AiPlannerOutputField.ToolCalls, index) with { ToolCount = element.GetArrayLength() };
                return false;
            }

            string? name = null;
            string? version = null;
            var arguments = default(JsonElement);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in item.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    diagnostic = Json(AiPlannerValidationReason.DuplicateField, AiPlannerOutputField.ToolCalls, index) with { ToolCount = element.GetArrayLength() };
                    return false;
                }

                if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        diagnostic = Json(AiPlannerValidationReason.InvalidFieldType, AiPlannerOutputField.ToolName, index) with { ToolCount = element.GetArrayLength() };
                        return false;
                    }
                    name = property.Value.GetString();
                }
                else if (property.Name.Equals("version", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        diagnostic = Json(AiPlannerValidationReason.InvalidFieldType, AiPlannerOutputField.ToolVersion, index) with { ToolCount = element.GetArrayLength() };
                        return false;
                    }
                    version = property.Value.GetString();
                }
                else if (property.Name.Equals("arguments", StringComparison.OrdinalIgnoreCase))
                {
                    // Kept verbatim; the planner validator decides whether it
                    // is an object with allowed, typed arguments.
                    arguments = property.Value.Clone();
                }
            }

            parsed.Add(new AiPlannerToolCall { Name = name ?? string.Empty, Version = version ?? string.Empty, Arguments = arguments });
            index++;
        }

        calls = parsed;
        return true;
    }
}
