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
    private static readonly HashSet<string> ForbiddenArgumentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "userId", "actorId", "role", "facilityId", "facilityAuthorization",
        "confirm", "confirmationToken", "concurrencyToken", "idempotencyKey"
    };

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
        var context = new List<ChatMessageDto>();
        if (!string.IsNullOrWhiteSpace(request.Memory?.LastIntent) || !string.IsNullOrWhiteSpace(request.Memory?.PendingClarification))
            context.Add(new ChatMessageDto
            {
                Role = "model",
                Content = $"Previous server-owned conversation state: intent={request.Memory?.LastIntent ?? "none"}; subIntent={request.Memory?.LastSubIntent ?? "none"}; pendingClarification={request.Memory?.PendingClarification ?? "none"}."
            });

        var planningDefinitions = request.AllowedTools.Count > 0
            ? request.AllowedTools
            : request.AllowedToolNames.Select(name => new AiToolDefinition { Name = name }).ToArray();
        var allowedToolContracts = planningDefinitions
            .Where(x => allowed.Contains(x.Name, StringComparer.OrdinalIgnoreCase) && AiPlannerPolicy.IsAllowed(x.Name))
            .Select(x => new
            {
                name = x.Name,
                description = x.Description,
                capabilities = x.Capabilities.Select(capability => capability.ToString()).OrderBy(x => x).ToArray(),
                riskLevel = x.RiskLevel.ToString(),
                confirmation = x.Confirmation.ToString(),
                // Resource identifiers are server-bound. Do not disclose even
                // their argument names to the provider-facing contract.
                argumentSchema = x.ArgumentSchema
                    .Where(argument => !argument.ServerBound)
                    .Select(argument => new
                    {
                        name = argument.Name,
                        type = argument.Type.ToString(),
                        required = argument.Required,
                        serverBound = argument.ServerBound
                    }).ToArray(),
                hasServerBoundResource = x.ResourceBinding.ServerBoundArgumentNames.Count > 0
            })
            .ToArray();

        var policyContext = JsonSerializer.Serialize(new
        {
            actorRole = request.Role.ToString(),
            localIntent = request.LocalIntent,
            localConfidence = request.LocalConfidence,
            allowedToolNames = allowed,
            allowedToolContracts,
            resourceContext = new
            {
                currentRoute = request.Resource.CurrentRoute,
                hasAppointment = request.Resource.AppointmentId.HasValue,
                hasVisit = request.Resource.VisitId.HasValue,
                hasDiagnosticOrder = request.Resource.DiagnosticOrderId.HasValue,
                hasPrescription = request.Resource.PrescriptionId.HasValue
            },
            conversation = new
            {
                request.Memory?.SanitizedSummary,
                request.Memory?.LastIntent,
                request.Memory?.LastSubIntent,
                request.Memory?.PendingClarification,
                request.Memory?.Version
            },
            instruction = "Return JSON only. Use only allowedTools. Do not emit write or confirmation tools. Ask a clarification when data is missing. Resource identifiers are server-bound and must not be requested from or echoed to the user."
        });

        try
        {
            var providerResult = await _provider.ChatWithAiAsync(SanitizeForProvider(request.Message), context, new List<WhitelistItemDto>(), policyContext, cancellationToken);
            var providerState = AiProviderStatusContract.FromProviderResult(providerResult.Status, true);
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
                    providerResult.CorrelationId,
                    providerResult.ProviderAttemptCount);
            }

            if (!string.Equals(providerResult.PlannerSchemaVersion, "1.0", StringComparison.Ordinal) ||
                !providerResult.PlannerConfidence.HasValue || providerResult.PlannerConfidence is < 0m or > 1m ||
                !AiChatIntentTypes.IsAllowed(providerResult.PrimaryIntent))
            {
                _health.RecordFailure(AiProviderStatusContract.FailureInvalidResponse);
                return Failed(AiProviderStatusContract.Degraded, "INVALID_PROVIDER_SCHEMA", true, providerAttempts: providerResult.ProviderAttemptCount);
            }

            var intent = providerResult.PrimaryIntent!;
            var calls = providerResult.ToolCalls ?? new List<AiPlannerToolCall>();
            if (calls.Count > 3 || calls.Any(x => !ValidateCall(x, allowed)))
            {
                _health.RecordFailure(AiProviderStatusContract.FailureInvalidResponse);
                return Failed(AiProviderStatusContract.Degraded, "INVALID_PROVIDER_PLAN", true, providerAttempts: providerResult.ProviderAttemptCount);
            }

            var preflight = AiToolBindingRegistry.ValidateAndBindPlan(calls, planningDefinitions, request.Resource);
            if (!preflight.IsValid)
            {
                if (!string.Equals(preflight.Code, "PROVIDER_RESOURCE_MISMATCH", StringComparison.Ordinal))
                    _health.RecordFailure(AiProviderStatusContract.FailureInvalidResponse);
                return Failed(AiProviderStatusContract.Degraded, preflight.Code, true, providerAttempts: providerResult.ProviderAttemptCount);
            }

            if (!providerResult.IsClear && string.IsNullOrWhiteSpace(providerResult.Clarification) && string.IsNullOrWhiteSpace(providerResult.ClarificationPrompt))
            {
                _health.RecordFailure(AiProviderStatusContract.FailureInvalidResponse);
                return Failed(AiProviderStatusContract.Degraded, "INVALID_PROVIDER_CLARIFICATION", true, providerAttempts: providerResult.ProviderAttemptCount);
            }

            _health.RecordSuccess();

            return new AiStructuredPlannerResult
            {
                IsSuccess = true,
                ProviderCalled = true,
                ProviderState = providerState,
                ProviderAttemptCount = providerResult.ProviderAttemptCount,
                FailureCode = AiProviderStatusContract.FailureNone,
                CorrelationId = providerResult.CorrelationId,
                Decision = new AiPlannerDecision
                {
                    PlannerMode = AiPlannerModes.Gemini,
                    Intent = intent,
                    SubIntent = providerResult.SecondaryIntent,
                    Confidence = providerResult.PlannerConfidence.Value,
                    Message = Limit(providerResult.Reply, 500),
                    Clarification = Limit(providerResult.Clarification ?? providerResult.ClarificationPrompt, 300),
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
            _logger.LogWarning(ex, "Structured AI planner failed; returning a fail-closed fallback");
            return Failed(AiProviderStatusContract.Degraded, AiProviderStatusContract.FailureNetworkError, true, true);
        }
    }

    private static bool ValidateCall(AiPlannerToolCall call, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(call.Name) || !allowed.Contains(call.Name, StringComparer.OrdinalIgnoreCase) || !AiPlannerPolicy.IsAllowed(call.Name)) return false;
        if (!string.Equals(call.Version, "1.0", StringComparison.Ordinal)) return false;
        if (call.Arguments.ValueKind != JsonValueKind.Object) return false;
        return !ContainsForbiddenArgument(call.Arguments);
    }

    private static bool ContainsForbiddenArgument(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (ForbiddenArgumentNames.Contains(property.Name) || ContainsForbiddenArgument(property.Value)) return true;
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                if (ContainsForbiddenArgument(item)) return true;
        return false;
    }

    private static AiStructuredPlannerResult Failed(
        string providerState,
        string? reason,
        bool called,
        bool retryable = false,
        DateTimeOffset? retryAfterUtc = null,
        int? retryAfterSeconds = null,
        string? correlationId = null,
        int providerAttempts = 0) => new()
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
        Decision = new AiPlannerDecision
        {
            PlannerMode = AiPlannerModes.Fallback,
            Intent = AiChatIntentTypes.ClarificationRequired,
            ErrorCode = reason,
            Clarification = reason == "PROVIDER_RESOURCE_MISMATCH"
                ? "Tôi không thể dùng resource do provider chọn vì nó không khớp resource đang mở. Vui lòng chọn lại resource hiện tại rồi thử lại."
                : "Tôi chưa xác định được yêu cầu đủ an toàn để tra cứu. Vui lòng mô tả rõ dữ liệu hoặc workspace cần xem.",
            Message = reason == "PROVIDER_RESOURCE_MISMATCH"
                ? "Tôi không thể dùng resource do provider chọn vì nó không khớp resource đang mở."
                : "Tôi chưa xác định được yêu cầu đủ an toàn để tra cứu. Vui lòng mô tả rõ dữ liệu hoặc workspace cần xem."
        }
    };

    private static string ToFailureCode(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return AiProviderStatusContract.FailureUnknown;
        if (reason.StartsWith("INVALID_PROVIDER", StringComparison.Ordinal)) return AiProviderStatusContract.FailureInvalidResponse;
        if (reason == "PROVIDER_FAILURE") return AiProviderStatusContract.FailureNetworkError;
        if (reason == "PROVIDER_RESOURCE_MISMATCH") return AiProviderStatusContract.FailureNone;
        return AiProviderStatusContract.FailureCodeFromProviderStatus(reason);
    }

    private static bool HasResource(AiResolvedResourceContext x) => x.AppointmentId.HasValue || x.VisitId.HasValue || x.DiagnosticOrderId.HasValue || x.PrescriptionId.HasValue;
    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(length, value.Trim().Length)];

    private static string SanitizeForProvider(string value)
    {
        var sanitized = value.Trim();
        sanitized = Regex.Replace(sanitized, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[email]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"(?<!\d)(?:\+?84|0)[1-9]\d{7,9}(?!\d)", "[phone]", RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"\b(?:cccd|cmnd|căn cước|mrn|mã hồ sơ|mã bệnh nhân)\s*[:#=]?\s*[A-Za-z0-9-]+", "[identifier]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"\b[0-9a-f]{8}-[0-9a-f-]{27,}\b", "[internal-id]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"\b(?:appointment|visit|encounter|order|prescription|patient|doctor)\s*id\s*[:#=]?\s*\d+\b", "[internal-id]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
