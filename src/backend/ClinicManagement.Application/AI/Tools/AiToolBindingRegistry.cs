using System.Text.Json;
using ClinicManagement.Application.AI.Planning;

namespace ClinicManagement.Application.AI.Tools;

public sealed class AiToolPlanPreflightResult
{
    public bool IsValid { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<AiPlannerToolCall> BoundCalls { get; init; } = Array.Empty<AiPlannerToolCall>();

    public static AiToolPlanPreflightResult Valid(IReadOnlyList<AiPlannerToolCall> calls) => new()
    {
        IsValid = true,
        BoundCalls = calls
    };

    public static AiToolPlanPreflightResult Invalid(string code, string message) => new()
    {
        IsValid = false,
        Code = code,
        Message = message
    };
}

/// <summary>
/// Server-owned planner boundary. Provider JSON is never allowed to choose a
/// resource. A whole plan is validated and bound before any executor call.
/// </summary>
public static class AiToolBindingRegistry
{
    private static readonly IReadOnlySet<string> ForbiddenArgumentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "userId", "actorId", "role", "facilityId", "facilityAuthorization",
        "confirm", "confirmationToken", "concurrencyToken", "idempotencyKey"
    };

    public static AiToolPlanPreflightResult ValidateAndBindPlan(
        IReadOnlyList<AiPlannerToolCall> calls,
        IReadOnlyList<AiToolDefinition> definitions,
        AiResolvedResourceContext resource)
    {
        if (calls.Count > 3)
            return AiToolPlanPreflightResult.Invalid("PLANNER_TOOL_LIMIT_EXCEEDED", "Kế hoạch AI vượt quá giới hạn số công cụ cho một lượt.");

        var definitionMap = definitions
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var bound = new List<AiPlannerToolCall>(calls.Count);

        foreach (var call in calls)
        {
            if (string.IsNullOrWhiteSpace(call.Name) || !definitionMap.TryGetValue(call.Name.Trim(), out var definition) ||
                !definition.Enabled || !AiPlannerPolicy.IsAllowed(definition.Name))
                return AiToolPlanPreflightResult.Invalid("PLANNER_TOOL_NOT_ALLOWED", "Kế hoạch công cụ không nằm trong allowlist.");
            if (!string.Equals(definition.Version, call.Version?.Trim(), StringComparison.OrdinalIgnoreCase))
                return AiToolPlanPreflightResult.Invalid("TOOL_VERSION_NOT_SUPPORTED", "Phiên bản công cụ không được hỗ trợ.");
            if (call.Arguments.ValueKind != JsonValueKind.Object)
                return AiToolPlanPreflightResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số công cụ phải là JSON object.");

            var schema = definition.ArgumentSchema.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in call.Arguments.EnumerateObject())
            {
                if (!properties.TryAdd(property.Name, property.Value.Clone()))
                    return AiToolPlanPreflightResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số công cụ chứa tên trùng lặp.");
                if (ContainsForbiddenArgument(property.Value) || ForbiddenArgumentNames.Contains(property.Name))
                    return AiToolPlanPreflightResult.Invalid("FORBIDDEN_TOOL_ARGUMENT", "Tham số quyền hạn chỉ được xác định phía server.");
                if (!schema.TryGetValue(property.Name, out var argument))
                    return AiToolPlanPreflightResult.Invalid("UNKNOWN_TOOL_ARGUMENT", "Tham số công cụ không nằm trong schema server.");
                if (!MatchesType(property.Value, argument.Type))
                    return AiToolPlanPreflightResult.Invalid("INVALID_TOOL_ARGUMENTS", "Kiểu tham số công cụ không hợp lệ.");
            }

            foreach (var argument in definition.ArgumentSchema.Where(x => x.Required && !x.ServerBound))
            {
                if (!properties.ContainsKey(argument.Name))
                    return AiToolPlanPreflightResult.Invalid("MISSING_TOOL_ARGUMENT", "Thiếu tham số bắt buộc cho công cụ.");
            }

            var binding = definition.ResourceBinding;
            if (binding.RequiresCurrentResource && !HasAnyBoundResource(resource, binding.ServerBoundArgumentNames))
                return AiToolPlanPreflightResult.Invalid("RESOURCE_CONTEXT_REQUIRED", "Công cụ này chỉ được chạy trên resource hiện tại đã xác minh.");

            var suppliedBound = binding.ServerBoundArgumentNames
                .Where(properties.ContainsKey)
                .ToArray();
            if (suppliedBound.Length > 1)
                return AiToolPlanPreflightResult.Invalid("INVALID_TOOL_ARGUMENTS", "Công cụ chỉ nhận một resource binding server-bound.");

            foreach (var name in suppliedBound)
            {
                if (!TryPositiveInt64(properties[name], out var supplied) || !MatchesCurrentResource(name, supplied, resource))
                    return AiToolPlanPreflightResult.Invalid("PROVIDER_RESOURCE_MISMATCH", "Provider đã chọn resource khác với resource hiện tại; không có tool nào được thực thi.");
            }

            if (binding.RequiresCurrentResource)
            {
                var canonical = CanonicalBinding(resource, binding.ServerBoundArgumentNames);
                if (canonical is null)
                    return AiToolPlanPreflightResult.Invalid("RESOURCE_CONTEXT_REQUIRED", "Resource hiện tại không có định danh phù hợp cho công cụ này.");
                foreach (var name in binding.ServerBoundArgumentNames)
                    properties.Remove(name);
                properties[canonical.Value.Name] = JsonSerializer.SerializeToElement(canonical.Value.Value);
            }

            bound.Add(new AiPlannerToolCall
            {
                Name = definition.Name,
                Version = definition.Version,
                Arguments = JsonSerializer.SerializeToElement(properties)
            });
        }

        return AiToolPlanPreflightResult.Valid(bound);
    }

    public static bool IsSameResourceContext(AiResolvedResourceContext left, AiResolvedResourceContext right) =>
        string.Equals(left.CurrentRoute, right.CurrentRoute, StringComparison.Ordinal) &&
        left.AppointmentId == right.AppointmentId &&
        left.VisitId == right.VisitId &&
        left.EncounterId == right.EncounterId &&
        left.DiagnosticOrderId == right.DiagnosticOrderId &&
        left.PrescriptionId == right.PrescriptionId &&
        string.Equals(left.ResourceVersion, right.ResourceVersion, StringComparison.Ordinal);

    private static bool ContainsForbiddenArgument(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ForbiddenArgumentNames.Contains(property.Name) || ContainsForbiddenArgument(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (ContainsForbiddenArgument(item)) return true;
        }
        return false;
    }

    private static bool MatchesType(JsonElement value, AiToolArgumentType type) => type switch
    {
        AiToolArgumentType.String => value.ValueKind == JsonValueKind.String,
        AiToolArgumentType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        AiToolArgumentType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false
    };

    private static bool TryPositiveInt64(JsonElement value, out long result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result) && result > 0;
    }

    private static bool HasAnyBoundResource(AiResolvedResourceContext resource, IReadOnlyList<string> names) =>
        names.Any(name => CurrentResourceValue(name, resource).HasValue);

    private static bool MatchesCurrentResource(string name, long value, AiResolvedResourceContext resource) =>
        CurrentResourceValue(name, resource) == value;

    private static (string Name, long Value)? CanonicalBinding(AiResolvedResourceContext resource, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var value = CurrentResourceValue(name, resource);
            if (value.HasValue) return (name, value.Value);
        }
        return null;
    }

    private static long? CurrentResourceValue(string name, AiResolvedResourceContext resource) => name.ToLowerInvariant() switch
    {
        "appointmentid" => resource.AppointmentId,
        "visitid" => resource.VisitId,
        "diagnosticorderid" => resource.DiagnosticOrderId,
        "prescriptionid" => resource.PrescriptionId,
        _ => null
    };
}
