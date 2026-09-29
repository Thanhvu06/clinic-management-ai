using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>
/// Hybrid role-aware orchestrator: safety and explicit commands remain local;
/// Gemini is used only for ambiguous semantic planning and never as authority.
/// </summary>
public sealed class RoleAwareCopilotOrchestrator : IAiRoleCopilotService
{
    private readonly IHttpContextAccessor _http;
    private readonly ICurrentUserService _currentUser;
    private readonly IAiConversationPipeline _pipeline;
    private readonly IAiDeterministicPlanner _deterministicPlanner;
    private readonly IAiStructuredPlanner _structuredPlanner;
    private readonly IAiCopilotContextResolver _contextResolver;
    private readonly IAiConversationMemoryStore _memoryStore;
    private readonly IAiGroundedResponseComposer _composer;
    private readonly IAiToolExecutor _executor;
    private readonly IAiAuditService _audit;

    public RoleAwareCopilotOrchestrator(
        IHttpContextAccessor http,
        ICurrentUserService currentUser,
        IAiConversationPipeline pipeline,
        IAiDeterministicPlanner deterministicPlanner,
        IAiStructuredPlanner structuredPlanner,
        IAiCopilotContextResolver contextResolver,
        IAiConversationMemoryStore memoryStore,
        IAiGroundedResponseComposer composer,
        IAiToolExecutor executor,
        IAiAuditService audit)
    {
        _http = http;
        _currentUser = currentUser;
        _pipeline = pipeline;
        _deterministicPlanner = deterministicPlanner;
        _structuredPlanner = structuredPlanner;
        _contextResolver = contextResolver;
        _memoryStore = memoryStore;
        _composer = composer;
        _executor = executor;
        _audit = audit;
    }

    public IReadOnlyList<AiToolDefinition> GetToolsForCurrentRole()
    {
        var role = ResolveRole();
        return ToolsForRole(role);
    }

    public IReadOnlyList<AiToolDefinition> GetActionToolsForCurrentRole()
    {
        var role = ResolveRole();
        return AiRoleActionCatalog.Definitions
            .Where(x => x.AllowedRoles.Contains(role) && !x.Name.Equals("role.execute_confirmed_action", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public async Task<AiCopilotResponseDto> ChatAsync(AiCopilotRequestDto request, CancellationToken cancellationToken = default)
    {
        var role = ResolveRole();
        var tools = ToolsForRole(role);
        var conversationId = NormalizeId(request.ConversationId ?? request.SessionId, "conv");
        var sessionId = NormalizeId(request.SessionId ?? request.ConversationId, "sess");
        var turnId = NormalizeId(request.ClientTurnId, "turn");
        var memory = await _memoryStore.LoadAsync(sessionId, _currentUser.UserId, role, cancellationToken);
        var analysis = _pipeline.Analyze(request.Message);

        if (analysis.Safety.IsEmergency || analysis.Safety.IsPromptInjection)
        {
            var emergency = analysis.Safety.IsEmergency;
            var response = new AiCopilotResponseDto
            {
                ConversationId = conversationId,
                TurnId = turnId,
                Role = role.ToString(),
                AssistantMode = AiAssistantModes.SafetyBlocked,
                ProviderState = AiProviderStatusContract.SafetyBlocked,
                PlannerMode = AiPlannerModes.Safety,
                Intent = emergency ? AiChatIntentTypes.EmergencyEscalation : AiChatIntentTypes.PromptInjection,
                Confidence = 1m,
                Message = emergency
                    ? "Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi 115 hoặc kích hoạt quy trình cấp cứu theo quy định của cơ sở."
                    : "Yêu cầu điều khiển tool hoặc truy cập vượt quyền đã bị từ chối.",
                SafetyNotice = "Safety guard đã chặn provider và mọi tool call trước khi truy cập dữ liệu.",
                SuggestedPrompts = SuggestedPrompts(role),
                AvailableTools = ToolsForUi(tools)
            };
            await PersistAndAudit(response, sessionId, role, null, 0, cancellationToken);
            return response;
        }

        if (AiMedicalScopeGuard.IsPrescriptionRequest(role, request.Message))
        {
            var response = new AiCopilotResponseDto
            {
                ConversationId = conversationId,
                TurnId = turnId,
                Role = role.ToString(),
                AssistantMode = AiAssistantModes.Clarifying,
                ProviderState = AiProviderStatusContract.NotCalled,
                PlannerMode = AiPlannerModes.Deterministic,
                Intent = AiChatIntentTypes.UnclearOrOutOfScope,
                Confidence = 1m,
                Message = "Tôi không thể kê đơn hoặc hướng dẫn liều dùng qua cuộc trò chuyện. Bạn có thể đặt lịch để được bác sĩ thăm khám, hoặc xem toa thuốc đã được cơ sở xác nhận trong tài khoản của mình.",
                ErrorCode = "MEDICAL_PRESCRIPTION_OUT_OF_SCOPE",
                ExecutionMode = AiProviderStatusContract.ExecutionDeterministicFallback,
                FallbackActive = true,
                SuggestedPrompts = SuggestedPrompts(role),
                AvailableTools = ToolsForUi(tools)
            };
            await PersistAndAudit(response, sessionId, role, null, 0, cancellationToken);
            return response;
        }

        var resolved = await _contextResolver.ResolveAsync(request, memory, role, _currentUser.UserId, cancellationToken);
        if (!resolved.IsValid)
        {
            var response = ClarifyingResponse(conversationId, turnId, role, tools, resolved.ErrorMessage!, AiProviderStatusContract.NotCalled, AiPlannerModes.Deterministic, resolved.ErrorCode);
            await PersistAndAudit(response, sessionId, role, null, 0, cancellationToken);
            return response;
        }

        var decision = _deterministicPlanner.Plan(new AiCopilotPlanningContext
        {
            Role = role,
            NormalizedMessage = analysis.NormalizedText,
            Analysis = analysis,
            Resource = resolved.Context,
            Memory = memory
        });
        var providerState = AiProviderStatusContract.NotCalled;
        var providerFailureCode = AiProviderStatusContract.FailureNone;
        var providerWasCalled = false;
        var retryable = false;
        DateTimeOffset? retryAfterUtc = null;
        int? retryAfterSeconds = null;
        string? correlationId = null;
        var providerAttemptCount = 0;

        if (decision.RequiresProvider)
        {
            if (resolved.HasPendingConfirmation)
            {
                decision = FallbackDecision("Bạn đang có một thao tác chờ xác nhận. Hãy hoàn tất hoặc hủy thao tác đó trước khi lập kế hoạch mới.", "PendingConfirmation");
            }
            else
            {
                var planned = await _structuredPlanner.PlanAsync(new AiStructuredPlannerRequest
                {
                    Role = role,
                    Message = analysis.NormalizedText,
                    LocalIntent = analysis.Intent.Intent,
                    LocalConfidence = (decimal)analysis.Intent.Confidence,
                    ConversationId = conversationId,
                    Resource = resolved.Context,
                    Memory = memory,
                    AllowedToolNames = tools.Select(x => x.Name).ToArray(),
                    AllowedTools = tools
                }, cancellationToken);
                providerState = planned.ProviderState;
                providerFailureCode = planned.FailureCode;
                providerWasCalled = planned.ProviderCalled;
                retryable = planned.Retryable;
                retryAfterUtc = planned.RetryAfterUtc;
                retryAfterSeconds = planned.RetryAfterSeconds;
                correlationId = planned.CorrelationId;
                providerAttemptCount = planned.ProviderAttemptCount;
                decision = planned.Decision;
                if (planned.ProviderCalled && planned.IsSuccess && decision.ToolCalls.Count > 0)
                {
                    var refreshed = await _contextResolver.ResolveAsync(request, memory, role, _currentUser.UserId, cancellationToken);
                    if (!refreshed.IsValid || !AiToolBindingRegistry.IsSameResourceContext(resolved.Context, refreshed.Context))
                    {
                        decision = FallbackDecision(
                            "Resource hiện tại đã thay đổi trong lúc lập kế hoạch. Vui lòng chọn lại resource rồi thử lại.",
                            "RESOURCE_CONTEXT_CHANGED",
                            "RESOURCE_CONTEXT_CHANGED");
                    }
                    else
                    {
                        resolved = refreshed;
                    }
                }
            }
        }

        if (decision.ToolCalls.Count > 0)
        {
            var preflight = AiToolBindingRegistry.ValidateAndBindPlan(decision.ToolCalls, tools, resolved.Context);
            if (!preflight.IsValid)
            {
                decision = FallbackDecision(preflight.Message, preflight.Code, preflight.Code);
            }
            else
            {
                decision = CopyWithCalls(decision, preflight.BoundCalls);
            }
        }

        var results = decision.ToolCalls.Count == 0
            ? Array.Empty<AiToolExecutionResult>()
            : (await _executor.ExecutePlannerPlanAsync(decision.ToolCalls, sessionId, cancellationToken)).ToArray();
        var grounded = _composer.Compose(decision, results);
        var hasFailure = results.Any(x => x.Status != "completed");
        var assistantMode = hasFailure || providerState is AiProviderStatusContract.Degraded or AiProviderStatusContract.Disabled
            ? AiAssistantModes.Degraded
            : providerState == AiProviderStatusContract.Unavailable
                ? AiAssistantModes.Unavailable
                : !string.IsNullOrWhiteSpace(decision.Clarification) ? AiAssistantModes.Clarifying : AiAssistantModes.Ready;

        var executionMode = providerWasCalled && providerState == AiProviderStatusContract.Online
            ? AiProviderStatusContract.ExecutionProviderAssisted
            : decision.PlannerMode == AiPlannerModes.Deterministic
                ? AiProviderStatusContract.ExecutionDeterministicFallback
                : AiProviderStatusContract.ExecutionManualHandoff;

        var final = new AiCopilotResponseDto
        {
            ConversationId = conversationId,
            TurnId = turnId,
            Role = role.ToString(),
            AssistantMode = assistantMode,
            ProviderState = providerState,
            PlannerMode = decision.PlannerMode,
            Intent = decision.Intent,
            SubIntent = decision.SubIntent,
            Confidence = decision.Confidence,
            Message = grounded.Message,
            ErrorCode = decision.ErrorCode,
            Clarification = decision.Clarification,
            NavigationRoute = grounded.NavigationRoute,
            Navigation = grounded.NavigationRoute,
            SuggestedPrompts = SuggestedPrompts(role),
            Cards = grounded.Cards,
            Sources = grounded.Sources,
            AvailableTools = ToolsForUi(tools),
            ProviderFailureCode = providerFailureCode,
            ExecutionMode = executionMode,
            FallbackActive = executionMode != AiProviderStatusContract.ExecutionProviderAssisted,
            Retryable = retryable,
            RetryAfterUtc = retryAfterUtc,
            RetryAfterSeconds = retryAfterSeconds,
            CorrelationId = correlationId,
            ProviderWasCalled = providerWasCalled,
            ProviderAttemptCount = providerAttemptCount,
            ExecutedToolNames = results
                .Select(x => x.ToolName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray()
        };
        await PersistAndAudit(final, sessionId, role, resolved.Context, decision.ToolCalls.Count, cancellationToken);
        return final;
    }

    private async Task PersistAndAudit(AiCopilotResponseDto response, string sessionId, AiActorRole role, AiResolvedResourceContext? resource, int toolCount, CancellationToken ct)
    {
        var memory = await _memoryStore.SaveTurnAsync(new AiConversationMemoryWriteRequest
        {
            UserId = _currentUser.UserId,
            SessionId = sessionId,
            ConversationId = response.ConversationId,
            Role = role,
            Intent = response.Intent,
            SubIntent = response.SubIntent,
            PendingClarification = response.Clarification,
            CurrentResource = resource
        }, ct);
        await _audit.LogActionAsync(new AiAuditLogEntry
        {
            UserId = _currentUser.UserId,
            SessionId = sessionId,
            ActionType = "CopilotTurn",
            Outcome = response.AssistantMode,
            MetadataJson = JsonSerializer.Serialize(new
            {
                source = "role-copilot",
                intent = response.Intent,
                subIntent = response.SubIntent,
                errorCode = response.ErrorCode,
                plannerMode = response.PlannerMode,
                providerState = response.ProviderState,
                role = response.Role,
                toolCount,
                conversationVersion = memory.Version
            })
        }, ct);
    }

    private AiActorRole ResolveRole()
    {
        var claims = _http.HttpContext?.User?.FindAll(ClaimTypes.Role).Select(x => x.Value) ?? Enumerable.Empty<string>();
        foreach (var claim in claims)
        {
            if (Enum.TryParse<AiActorRole>(claim, true, out var role)) return role;
            if (claim.Equals("Diagnostic Technician", StringComparison.OrdinalIgnoreCase)) return AiActorRole.DiagnosticTechnician;
        }
        return AiActorRole.Patient;
    }

    private static IReadOnlyList<AiToolDefinition> ToolsForRole(AiActorRole role)
    {
        var roleTools = AiRoleToolCatalog.Definitions
            .Where(x => x.AllowedRoles.Contains(role));

        // Patient read tools are registered by the patient dispatcher, not by
        // the professional workspace catalog. They still belong in the
        // authenticated patient Copilot contract so the UI and a structured
        // planner do not advertise a smaller tool set than the deterministic
        // patient planner can safely execute. Patient write tools remain on
        // the dedicated prepare/confirm surface and are deliberately omitted.
        var patientReadTools = role == AiActorRole.Patient
            ? ClinicManagement.Infrastructure.AI.Tools.PatientCopilotToolHandler.Definitions()
                .Where(x => x.Name.StartsWith("patient.get_", StringComparison.OrdinalIgnoreCase) ||
                            x.AccessMode == AiToolAccessMode.Public)
            : Enumerable.Empty<AiToolDefinition>();

        return roleTools
            .Concat(patientReadTools)
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    private static IReadOnlyList<AiToolDefinition> ToolsForUi(IReadOnlyList<AiToolDefinition> tools) =>
        tools.Select(tool => new AiToolDefinition
        {
            Name = tool.Name,
            Version = tool.Version,
            Description = tool.Description,
            AccessMode = tool.AccessMode,
            RiskLevel = tool.RiskLevel,
            Confirmation = tool.Confirmation,
            Enabled = tool.Enabled,
            AllowedRoles = tool.AllowedRoles,
            Capabilities = tool.Capabilities,
            DataSources = tool.DataSources,
            // The UI can display capabilities, but never receives resource
            // binding field names or server-owned resource metadata.
            ArgumentSchema = tool.ArgumentSchema.Where(x => !x.ServerBound).ToArray(),
            ResourceBinding = AiToolResourceBinding.None
        }).ToArray();

    private static AiPlannerDecision FallbackDecision(string message, string subIntent, string? errorCode = null) => new()
    {
        PlannerMode = AiPlannerModes.Fallback,
        Intent = AiChatIntentTypes.ClarificationRequired,
        SubIntent = subIntent,
        ErrorCode = errorCode,
        Clarification = message,
        Message = message,
        Confidence = 1m
    };

    private static AiPlannerDecision CopyWithCalls(AiPlannerDecision decision, IReadOnlyList<AiPlannerToolCall> calls)
    {
        return new AiPlannerDecision
        {
            PlannerMode = decision.PlannerMode,
            Intent = decision.Intent,
            SubIntent = decision.SubIntent,
            Confidence = decision.Confidence,
            RequiresProvider = decision.RequiresProvider,
            Message = decision.Message,
            Clarification = decision.Clarification,
            NavigationRoute = decision.NavigationRoute,
            ErrorCode = decision.ErrorCode,
            ToolCalls = calls
        };
    }

    private static AiCopilotResponseDto ClarifyingResponse(string conversationId, string turnId, AiActorRole role, IReadOnlyList<AiToolDefinition> tools, string message, string providerState, string plannerMode, string? subIntent) => new()
    {
        ConversationId = conversationId,
        TurnId = turnId,
        Role = role.ToString(),
        AssistantMode = AiAssistantModes.Clarifying,
        ProviderState = providerState,
        PlannerMode = plannerMode,
        Intent = AiChatIntentTypes.ClarificationRequired,
        SubIntent = subIntent,
        ErrorCode = subIntent,
        Confidence = 1m,
        Message = message,
        Clarification = message,
        SuggestedPrompts = SuggestedPrompts(role),
        AvailableTools = ToolsForUi(tools)
    };

    private static string NormalizeId(string? value, string prefix) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 128 ? value.Trim() : $"{prefix}_{Guid.NewGuid():N}";

    private static IReadOnlyList<string> SuggestedPrompts(AiActorRole role) => role switch
    {
        AiActorRole.Receptionist => new[] { "Xem lịch hẹn hôm nay", "Xem hàng đợi tiếp nhận", "Tra cứu mã lịch hẹn" },
        AiActorRole.Doctor => new[] { "Xem hàng đợi của tôi", "Xem chỉ định cận lâm sàng", "Tóm tắt bệnh nhân hiện tại" },
        AiActorRole.DiagnosticTechnician => new[] { "Xem danh sách chỉ định đang chờ" },
        AiActorRole.Pharmacist => new[] { "Xem đơn thuốc chờ cấp", "Xem tồn kho" },
        AiActorRole.Admin => new[] { "Xem thống kê hôm nay", "Xem tình trạng AI" },
        _ => Array.Empty<string>()
    };
}
