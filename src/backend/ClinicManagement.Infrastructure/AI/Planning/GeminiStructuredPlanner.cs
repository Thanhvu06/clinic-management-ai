using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI.Planning;

/// <summary>
/// Optional semantic planner. Its JSON is treated as untrusted input and must
/// pass this role-specific contract before the executor sees any call.
/// </summary>
public sealed class GeminiStructuredPlanner : IAiStructuredPlanner
{
    private readonly IAiSpecialtySuggestionProvider _provider;
    private readonly IAiProviderHealth _health;
    private readonly ILogger<GeminiStructuredPlanner> _logger;

    public GeminiStructuredPlanner(IAiSpecialtySuggestionProvider provider, IAiProviderHealth health, ILogger<GeminiStructuredPlanner> logger)
    {
        _provider = provider;
        _health = health;
        _logger = logger;
    }

    public async Task<AiStructuredPlannerResult> PlanAsync(AiStructuredPlannerRequest request, CancellationToken cancellationToken = default)
    {
        if (!_health.CanAttempt())
            return Failed(AiProviderStatusContract.Unavailable, AiProviderStatusContract.FailureCircuitOpen, false, false);

        var allowed = request.AllowedToolNames.Where(AiPlannerPolicy.IsAllowed).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var planningDefinitions = request.AllowedTools.Count > 0
            ? request.AllowedTools
            : request.AllowedToolNames.Select(name => new AiToolDefinition { Name = name }).ToArray();
        var definitionMap = planningDefinitions
            .Where(x => allowed.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var providerRequest = new AiRolePlannerProviderRequest
        {
            Role = request.Role,
            Message = SanitizeForProvider(request.Message),
            AllowedTools = AiRolePlannerContract.BuildToolContracts(planningDefinitions, allowed),
            AllowedIntents = AiRolePlannerContract.AllowedIntents,
            // Resource identifiers stay on the server; the provider only
            // learns whether a verified resource of each kind is open.
            Context = new AiRolePlannerServerContext
            {
                LocalIntent = request.LocalIntent,
                LocalConfidence = request.LocalConfidence,
                HasAppointment = request.Resource.AppointmentId.HasValue,
                HasVisit = request.Resource.VisitId.HasValue,
                HasDiagnosticOrder = request.Resource.DiagnosticOrderId.HasValue,
                HasPrescription = request.Resource.PrescriptionId.HasValue,
                LastIntent = request.Memory?.LastIntent,
                LastSubIntent = request.Memory?.LastSubIntent,
                PendingClarification = request.Memory?.PendingClarification,
                ConversationVersion = request.Memory?.Version
            }
        };

        try
        {
            var providerResult = await _provider.PlanRoleCopilotAsync(providerRequest, cancellationToken);
            var providerState = AiProviderStatusContract.FromProviderResult(providerResult.Status, true);
            var correlationId = providerResult.CorrelationId;
            var attempts = providerResult.ProviderAttemptCount;
            if (!providerResult.IsSuccess)
            {
                if (string.Equals(providerResult.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) && cancellationToken.IsCancellationRequested)
                {
                    _health.RecordFailure(AiProviderStatusContract.FailureClientCancelled);
                    throw new OperationCanceledException(cancellationToken);
                }

                if (AiProviderStatusContract.IsCircuitFailure(providerResult.Status))
                    _health.RecordFailure(providerResult.Status);
                else
                    _health.RecordFailure(providerResult.FailureCode);
                return Failed(
                    providerState,
                    providerResult.FailureCode == AiProviderStatusContract.FailureNone
                        ? AiProviderStatusContract.FailureCodeFromProviderStatus(providerResult.Status)
                        : providerResult.FailureCode,
                    true,
                    providerResult.Retryable,
                    providerResult.RetryAfterUtc,
                    providerResult.RetryAfterSeconds,
                    correlationId,
                    attempts,
                    providerResult.Diagnostic);
            }

            var output = providerResult.Output;
            var schemaFailure = output is null
                ? new AiPlannerValidationDiagnostic(AiPlannerValidationStage.GeneratedJson, AiPlannerValidationReason.MissingRequiredField) { Field = AiPlannerOutputField.Root }
                : ValidateSchema(output);
            if (schemaFailure is not null)
                return Rejected(AiPlannerErrorCodes.InvalidProviderSchema, schemaFailure, correlationId, attempts);

            var calls = output!.ToolCalls!;
            var planFailure = ValidatePlan(output, calls, definitionMap);
            if (planFailure is not null)
                return Rejected(AiPlannerErrorCodes.InvalidProviderPlan, planFailure, correlationId, attempts);

            var preflight = AiToolBindingRegistry.ValidateAndBindPlan(calls, planningDefinitions, request.Resource);
            if (!preflight.IsValid)
                return Rejected(preflight.Code, preflight.Diagnostic, correlationId, attempts);

            if (output.IsClear == false && string.IsNullOrWhiteSpace(output.Clarification))
                return Rejected(
                    AiPlannerErrorCodes.InvalidProviderClarification,
                    new AiPlannerValidationDiagnostic(AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.MissingClarification) { Field = AiPlannerOutputField.Clarification },
                    correlationId,
                    attempts);

            _health.RecordSuccess();

            return new AiStructuredPlannerResult
            {
                IsSuccess = true,
                ProviderCalled = true,
                ProviderState = providerState,
                ProviderAttemptCount = attempts,
                FailureCode = AiProviderStatusContract.FailureNone,
                CorrelationId = correlationId,
                Decision = new AiPlannerDecision
                {
                    PlannerMode = AiPlannerModes.Gemini,
                    Intent = output.PrimaryIntent!,
                    Confidence = output.PlannerConfidence!.Value,
                    Message = Limit(output.Reply, 500),
                    Clarification = Limit(output.Clarification, 300),
                    ToolCalls = preflight.BoundCalls
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _health.RecordFailure(AiProviderStatusContract.FailureClientCancelled);
            throw;
        }
        catch (Exception ex)
        {
            _health.RecordFailure(AiProviderStatusContract.FailureNetworkError);
            _logger.LogWarning("Structured AI planner failed with {ExceptionType}; returning a fail-closed fallback", ex.GetType().Name);
            return Failed(AiProviderStatusContract.Degraded, AiProviderStatusContract.FailureNetworkError, true, true);
        }
    }

    /// <summary>
    /// Required planner fields. Nothing is defaulted: a missing version,
    /// confidence, intent, isClear or toolCalls rejects the whole plan.
    /// </summary>
    private static AiPlannerValidationDiagnostic? ValidateSchema(AiRolePlannerOutput output)
    {
        static AiPlannerValidationDiagnostic Schema(AiPlannerValidationReason reason, AiPlannerOutputField field) =>
            new(AiPlannerValidationStage.PlannerSchema, reason) { Field = field };

        if (output.PlannerSchemaVersion is null)
            return Schema(AiPlannerValidationReason.MissingSchemaVersion, AiPlannerOutputField.PlannerSchemaVersion);
        if (!string.Equals(output.PlannerSchemaVersion, AiRolePlannerContract.SchemaVersion, StringComparison.Ordinal))
            return Schema(AiPlannerValidationReason.UnsupportedSchemaVersion, AiPlannerOutputField.PlannerSchemaVersion);
        if (!output.PlannerConfidence.HasValue)
            return Schema(AiPlannerValidationReason.MissingConfidence, AiPlannerOutputField.PlannerConfidence);
        if (output.PlannerConfidence is < 0m or > 1m)
            return Schema(AiPlannerValidationReason.InvalidConfidence, AiPlannerOutputField.PlannerConfidence);
        if (!AiRolePlannerContract.IsAllowedIntent(output.PrimaryIntent))
            return Schema(AiPlannerValidationReason.InvalidIntent, AiPlannerOutputField.PrimaryIntent);
        if (!output.IsClear.HasValue)
            return Schema(AiPlannerValidationReason.MissingRequiredField, AiPlannerOutputField.IsClear);
        if (output.ToolCalls is null)
            return Schema(AiPlannerValidationReason.MissingRequiredField, AiPlannerOutputField.ToolCalls);
        return null;
    }

    /// <summary>
    /// Whole-plan check before any tool runs. The first invalid call rejects
    /// every call; nothing is dropped, renamed or repaired.
    /// </summary>
    private static AiPlannerValidationDiagnostic? ValidatePlan(
        AiRolePlannerOutput output,
        IReadOnlyList<AiPlannerToolCall> calls,
        IReadOnlyDictionary<string, AiToolDefinition> definitions)
    {
        if (output.IsClear == false && calls.Count > 0)
            return new AiPlannerValidationDiagnostic(AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ClarificationWithTools) { ToolCount = calls.Count };
        if (calls.Count > AiRolePlannerContract.MaxToolCalls)
            return new AiPlannerValidationDiagnostic(AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ToolLimitExceeded)
            {
                RejectedToolIndex = AiRolePlannerContract.MaxToolCalls,
                ToolCount = calls.Count
            };

        for (var index = 0; index < calls.Count; index++)
        {
            var call = calls[index];
            AiPlannerValidationDiagnostic Plan(AiPlannerValidationReason reason, string? canonicalName) =>
                new(AiPlannerValidationStage.ToolPlan, reason) { RejectedToolIndex = index, ToolCount = calls.Count, ToolName = canonicalName };

            if (string.IsNullOrWhiteSpace(call.Name) || !AiPlannerPolicy.IsAllowed(call.Name) ||
                !definitions.TryGetValue(call.Name.Trim(), out var definition))
                return Plan(AiPlannerValidationReason.ToolNotAllowed, null);
            if (!string.Equals(call.Version, definition.Version, StringComparison.Ordinal))
                return Plan(AiPlannerValidationReason.UnsupportedToolVersion, definition.Name);
            if (call.Arguments.ValueKind != JsonValueKind.Object)
                return Plan(AiPlannerValidationReason.InvalidArguments, definition.Name);
            if (AiToolBindingRegistry.ContainsForbiddenArgument(call.Arguments))
                return Plan(AiPlannerValidationReason.ForbiddenArgument, definition.Name);
        }

        return null;
    }

    private AiStructuredPlannerResult Rejected(string code, AiPlannerValidationDiagnostic? diagnostic, string? correlationId, int attempts)
    {
        if (!string.Equals(code, AiPlannerErrorCodes.ProviderResourceMismatch, StringComparison.Ordinal))
            _health.RecordFailure(AiProviderStatusContract.FailureInvalidResponse);
        _logger.LogWarning(
            "[{CorrelationId}] Role planner output rejected: {ErrorCode} {Stage}/{Reason} tool {ToolIndex} of {ToolCount}.",
            correlationId, code, diagnostic?.Stage, diagnostic?.Reason, diagnostic?.RejectedToolIndex, diagnostic?.ToolCount);
        return Failed(AiProviderStatusContract.Degraded, code, true, correlationId: correlationId, providerAttempts: attempts, diagnostic: diagnostic);
    }

    private static AiStructuredPlannerResult Failed(
        string providerState,
        string? reason,
        bool called,
        bool retryable = false,
        DateTimeOffset? retryAfterUtc = null,
        int? retryAfterSeconds = null,
        string? correlationId = null,
        int providerAttempts = 0,
        AiPlannerValidationDiagnostic? diagnostic = null) => new()
    {
        IsSuccess = false,
        ProviderCalled = called,
        ProviderAttemptCount = providerAttempts,
        ProviderState = providerState,
        FailureReason = reason,
        FailureCode = ToFailureCode(reason),
        Retryable = retryable,
        RetryAfterUtc = retryAfterUtc,
        RetryAfterSeconds = retryAfterSeconds,
        CorrelationId = correlationId,
        Diagnostic = diagnostic,
        Decision = new AiPlannerDecision
        {
            PlannerMode = AiPlannerModes.Fallback,
            Intent = AiChatIntentTypes.ClarificationRequired,
            ErrorCode = reason,
            Clarification = reason == AiPlannerErrorCodes.ProviderResourceMismatch
                ? "Tôi không thể dùng resource do provider chọn vì nó không khớp resource đang mở. Vui lòng chọn lại resource hiện tại rồi thử lại."
                : "Tôi chưa xác định được yêu cầu đủ an toàn để tra cứu. Vui lòng mô tả rõ dữ liệu hoặc workspace cần xem.",
            Message = reason == AiPlannerErrorCodes.ProviderResourceMismatch
                ? "Tôi không thể dùng resource do provider chọn vì nó không khớp resource đang mở."
                : "Tôi chưa xác định được yêu cầu đủ an toàn để tra cứu. Vui lòng mô tả rõ dữ liệu hoặc workspace cần xem."
        }
    };

    private static string ToFailureCode(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return AiProviderStatusContract.FailureUnknown;
        if (reason.StartsWith("INVALID_PROVIDER", StringComparison.Ordinal)) return AiProviderStatusContract.FailureInvalidResponse;
        if (reason == "PROVIDER_FAILURE") return AiProviderStatusContract.FailureNetworkError;
        if (reason == AiPlannerErrorCodes.ProviderResourceMismatch) return AiProviderStatusContract.FailureNone;
        return AiProviderStatusContract.FailureCodeFromProviderStatus(reason);
    }

    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(length, value.Trim().Length)];

    private static string SanitizeForProvider(string value)
    {
        var sanitized = value.Trim();
        sanitized = Regex.Replace(sanitized, @"(?<!\d)(?:\d{12}|\d{9})(?!\d)", "[identifier]", RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[email]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"(?<!\d)(?:\+?84|0)[1-9]\d{7,9}(?!\d)", "[phone]", RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"\b(?:cccd|cmnd|căn cước|mrn|mã hồ sơ|mã bệnh nhân)\s*[:#=]?\s*[A-Za-z0-9-]+", "[identifier]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"\b[0-9a-f]{8}-[0-9a-f-]{27,}\b", "[internal-id]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"\b(?:appointment|visit|encounter|order|prescription|patient|doctor)\s*id\s*[:#=]?\s*\d+\b", "[internal-id]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
