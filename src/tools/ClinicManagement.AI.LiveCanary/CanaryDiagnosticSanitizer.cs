using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Planning;

namespace ClinicManagement.AI.LiveCanary;

/// <summary>
/// Report-side allowlist for rejection diagnostics. Anything outside the
/// closed server enums becomes "Unknown"; an absent diagnostic is
/// "NotAvailable". No free text, exception message or JSON path is kept.
/// </summary>
internal static class CanaryDiagnosticSanitizer
{
    public const string NotAvailable = "NotAvailable";
    public const string Unknown = "Unknown";
    public const string OtherErrorCode = "Other";
    private const int MaxToolPosition = 16;

    private static readonly IReadOnlySet<string> Stages = Enum.GetNames<AiPlannerValidationStage>().ToHashSet(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> Reasons = Enum.GetNames<AiPlannerValidationReason>().ToHashSet(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> FinishReasons = Enum.GetNames<AiProviderFinishReason>().ToHashSet(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> HttpStatuses = AiProviderHttpDiagnostic.ReportedHttpStatuses
        .Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .ToHashSet(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ProviderErrorStatuses = Enum.GetNames<AiProviderErrorStatus>().ToHashSet(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> RejectedRequestParts = Enum.GetNames<AiProviderRejectedRequestPart>().ToHashSet(StringComparer.Ordinal);
    private static readonly Regex ServerCorrelationId = new("^[0-9a-f]{8}$", RegexOptions.CultureInvariant);

    private static readonly IReadOnlySet<string> ErrorCodes = AiPlannerErrorCodes.All
        .Concat(new[]
        {
            AiProviderStatusContract.FailureRateLimited,
            AiProviderStatusContract.FailureTimeout,
            AiProviderStatusContract.FailureNetworkError,
            AiProviderStatusContract.FailureServerError,
            AiProviderStatusContract.FailureAuthenticationFailed,
            AiProviderStatusContract.FailureModelUnavailable,
            AiProviderStatusContract.FailureInvalidResponse,
            AiProviderStatusContract.FailureSafetyBlocked,
            AiProviderStatusContract.FailureCircuitOpen,
            AiProviderStatusContract.FailureClientCancelled,
            AiProviderStatusContract.FailureConfigurationDisabled,
            AiProviderStatusContract.FailureAttemptBudgetExceeded,
            AiProviderStatusContract.FailureUnknown
        })
        .ToHashSet(StringComparer.Ordinal);

    public static string ErrorCode(string? value) =>
        value is null ? NotAvailable : ErrorCodes.Contains(value) ? value : OtherErrorCode;

    public static string Stage(string? value) => Closed(value, Stages);
    public static string Reason(string? value) => Closed(value, Reasons);
    public static string FinishReason(string? value) => Closed(value, FinishReasons);

    // Provider HTTP rejection codes: anything outside the closed set is "Other".
    public static string ProviderHttpStatus(string? value) => ClosedOrOther(value, HttpStatuses);
    public static string ProviderErrorStatus(string? value) => ClosedOrOther(value, ProviderErrorStatuses);
    public static string ProviderRejectedRequestPart(string? value) => ClosedOrOther(value, RejectedRequestParts);

    public static string? CorrelationId(string? value) =>
        value is not null && ServerCorrelationId.IsMatch(value) ? value : null;

    public static int? ToolPosition(int? value) => value is >= 0 and <= MaxToolPosition ? value : null;

    public static string? ToolName(string? value, IReadOnlySet<string> allowedNames) =>
        value is not null && allowedNames.Contains(value) ? value : null;

    public static CanaryRejectionDiagnostic FromResponse(JsonElement data, IReadOnlySet<string> allowedToolNames)
    {
        var errorCode = ErrorCode(Text(data, "errorCode"));
        var correlationId = CorrelationId(Text(data, "correlationId"));
        if (!data.TryGetProperty("plannerDiagnostic", out var diagnostic) || diagnostic.ValueKind != JsonValueKind.Object)
            return new CanaryRejectionDiagnostic(errorCode, NotAvailable, NotAvailable, NotAvailable, correlationId, null, null, null);

        return new CanaryRejectionDiagnostic(
            errorCode,
            Stage(Text(diagnostic, "stage") ?? Unknown),
            Reason(Text(diagnostic, "reason") ?? Unknown),
            FinishReason(Text(diagnostic, "finishReason") ?? Unknown),
            correlationId,
            ToolPosition(Int(diagnostic, "rejectedToolIndex")),
            ToolPosition(Int(diagnostic, "toolCount")),
            ToolName(Text(diagnostic, "toolName"), allowedToolNames))
        {
            ProviderHttpStatus = ProviderHttpStatus(Text(diagnostic, "providerHttpStatus")),
            ProviderErrorStatus = ProviderErrorStatus(Text(diagnostic, "providerErrorStatus")),
            ProviderRejectedRequestPart = ProviderRejectedRequestPart(Text(diagnostic, "providerRejectedRequestPart"))
        };
    }

    public static CanaryRejectionDiagnostic FromPlannerResult(AiStructuredPlannerResult result, IReadOnlySet<string> allowedToolNames)
    {
        var diagnostic = result.Diagnostic;
        var providerHttp = result.ProviderHttp;
        return new CanaryRejectionDiagnostic(
            ErrorCode(result.FailureReason),
            diagnostic is null ? NotAvailable : Stage(diagnostic.Stage.ToString()),
            diagnostic is null ? NotAvailable : Reason(diagnostic.Reason.ToString()),
            diagnostic is null ? NotAvailable : FinishReason(diagnostic.FinishReason.ToString()),
            CorrelationId(result.CorrelationId),
            ToolPosition(diagnostic?.RejectedToolIndex),
            ToolPosition(diagnostic?.ToolCount),
            ToolName(diagnostic?.ToolName, allowedToolNames))
        {
            ProviderHttpStatus = ProviderHttpStatus(providerHttp?.HttpStatus),
            ProviderErrorStatus = ProviderErrorStatus(providerHttp?.ErrorStatus.ToString()),
            ProviderRejectedRequestPart = ProviderRejectedRequestPart(providerHttp?.RejectedRequestPart.ToString())
        };
    }

    // The server writes "NotAvailable" for validation fields of a provider
    // HTTP rejection; keep it rather than folding it into "Unknown".
    private static string Closed(string? value, IReadOnlySet<string> allowed) =>
        value is null or NotAvailable ? NotAvailable : allowed.Contains(value) ? value : Unknown;

    private static string ClosedOrOther(string? value, IReadOnlySet<string> allowed) =>
        value is null or NotAvailable ? NotAvailable : allowed.Contains(value) ? value : OtherErrorCode;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : null;
}

internal sealed record CanaryRejectionDiagnostic(
    string ApplicationErrorCode,
    string ValidationStage,
    string ValidationReason,
    string FinishReason,
    string? CorrelationId,
    int? RejectedToolIndex,
    int? ToolCount,
    string? RejectedToolName)
{
    public string ProviderHttpStatus { get; init; } = CanaryDiagnosticSanitizer.NotAvailable;
    public string ProviderErrorStatus { get; init; } = CanaryDiagnosticSanitizer.NotAvailable;
    public string ProviderRejectedRequestPart { get; init; } = CanaryDiagnosticSanitizer.NotAvailable;

    public bool IsPlannerSchemaOrJson =>
        ValidationStage is nameof(AiPlannerValidationStage.ProviderEnvelope) or nameof(AiPlannerValidationStage.GeneratedJson) or nameof(AiPlannerValidationStage.PlannerSchema);

    public bool IsToolPlanOrBinding =>
        ValidationStage is nameof(AiPlannerValidationStage.ToolPlan) or nameof(AiPlannerValidationStage.ResourceBinding);
}
