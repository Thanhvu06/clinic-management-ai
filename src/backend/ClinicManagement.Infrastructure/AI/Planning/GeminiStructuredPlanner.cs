using System.Text.Json;
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
            return Failed(AiProviderStatusContract.Unavailable, "PROVIDER_CIRCUIT_OPEN", false);

        var allowed = request.AllowedToolNames.Where(AiPlannerPolicy.IsAllowed).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var context = new List<ChatMessageDto>();
        if (!string.IsNullOrWhiteSpace(request.Memory?.LastIntent))
            context.Add(new ChatMessageDto { Role = "model", Content = $"Previous sanitized intent: {request.Memory.LastIntent}." });

        var policyContext = JsonSerializer.Serialize(new
        {
            actorRole = request.Role.ToString(),
            allowedTools = allowed,
            resourceContextAvailable = HasResource(request.Resource),
            instruction = "Return JSON only. Use only allowedTools. Do not emit write or confirmation tools. Ask a clarification when data is missing."
        });

        try
        {
            var providerResult = await _provider.ChatWithAiAsync(request.Message, context, new List<WhitelistItemDto>(), policyContext, cancellationToken);
            var providerState = AiProviderStatusContract.FromProviderResult(providerResult.Status, true);
            if (!providerResult.IsSuccess)
            {
                _health.RecordFailure();
                return Failed(providerState, providerResult.Status, true);
            }

            if (!string.Equals(providerResult.PlannerSchemaVersion, "1.0", StringComparison.Ordinal) ||
                !providerResult.PlannerConfidence.HasValue || providerResult.PlannerConfidence is < 0m or > 1m ||
                !AiChatIntentTypes.IsAllowed(providerResult.PrimaryIntent))
            {
                _health.RecordFailure();
                return Failed(AiProviderStatusContract.Degraded, "INVALID_PROVIDER_SCHEMA", true);
            }

            var intent = providerResult.PrimaryIntent!;
            var calls = providerResult.ToolCalls ?? new List<AiPlannerToolCall>();
            if (calls.Count > 3 || calls.Any(x => !ValidateCall(x, allowed)))
            {
                _health.RecordFailure();
                return Failed(AiProviderStatusContract.Degraded, "INVALID_PROVIDER_PLAN", true);
            }

            if (!providerResult.IsClear && string.IsNullOrWhiteSpace(providerResult.Clarification) && string.IsNullOrWhiteSpace(providerResult.ClarificationPrompt))
            {
                _health.RecordFailure();
                return Failed(AiProviderStatusContract.Degraded, "INVALID_PROVIDER_CLARIFICATION", true);
            }

            _health.RecordSuccess();

            return new AiStructuredPlannerResult
            {
                IsSuccess = true,
                ProviderCalled = true,
                ProviderState = providerState,
                Decision = new AiPlannerDecision
                {
                    PlannerMode = AiPlannerModes.Gemini,
                    Intent = intent,
                    SubIntent = providerResult.SecondaryIntent,
                    Confidence = providerResult.PlannerConfidence.Value,
                    Message = Limit(providerResult.Reply, 500),
                    Clarification = Limit(providerResult.Clarification ?? providerResult.ClarificationPrompt, 300),
                    ToolCalls = calls.ToArray()
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _health.RecordFailure();
            _logger.LogWarning(ex, "Structured AI planner failed; returning a fail-closed fallback");
            return Failed(AiProviderStatusContract.Degraded, "PROVIDER_FAILURE", true);
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

    private static AiStructuredPlannerResult Failed(string providerState, string? reason, bool called) => new()
    {
        IsSuccess = false,
        ProviderCalled = called,
        ProviderState = providerState,
        FailureReason = reason,
        Decision = new AiPlannerDecision
        {
            PlannerMode = AiPlannerModes.Fallback,
            Intent = AiChatIntentTypes.ClarificationRequired,
            Clarification = "Tôi chưa xác định được yêu cầu đủ an toàn để tra cứu. Vui lòng mô tả rõ dữ liệu hoặc workspace cần xem.",
            Message = "Tôi chưa xác định được yêu cầu đủ an toàn để tra cứu. Vui lòng mô tả rõ dữ liệu hoặc workspace cần xem."
        }
    };

    private static bool HasResource(AiResolvedResourceContext x) => x.AppointmentId.HasValue || x.VisitId.HasValue || x.DiagnosticOrderId.HasValue || x.PrescriptionId.HasValue;
    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(length, value.Trim().Length)];
}
