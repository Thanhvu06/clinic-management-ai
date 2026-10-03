using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed class AiToolExecutor : IAiToolExecutor
{
    private readonly IAiToolRegistry _registry;
    private readonly IAiCapabilityResolver _capabilityResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAiAuditService _audit;
    private readonly ILogger<AiToolExecutor> _logger;

    public AiToolExecutor(
        IAiToolRegistry registry,
        IAiCapabilityResolver capabilityResolver,
        ICurrentUserService currentUser,
        IHttpContextAccessor httpContextAccessor,
        IAiAuditService audit,
        ILogger<AiToolExecutor> logger)
    {
        _registry = registry;
        _capabilityResolver = capabilityResolver;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _audit = audit;
        _logger = logger;
    }

    public Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(invocation, AiToolInvocationChannel.Planner, cancellationToken);

    /// <summary>
    /// Invoked only by the authenticated, server-owned prepare endpoint.  The
    /// browser may supply a tool name and arguments, but never the invocation
    /// channel itself.
    /// </summary>
    public Task<AiToolExecutionResult> ExecuteDirectPreparationAsync(
        AiToolInvocation invocation,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(invocation, AiToolInvocationChannel.DirectHumanPreparation, cancellationToken);

    public async Task<IReadOnlyList<AiToolExecutionResult>> ExecutePlannerPlanAsync(
        IReadOnlyList<AiPlannerToolCall> plannedCalls,
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        if (plannedCalls.Count > 3)
            return new[] { AiToolExecutionResult.Failed("PLANNER_TOOL_LIMIT_EXCEEDED", "Kế hoạch AI vượt quá giới hạn số công cụ cho một lượt.") };

        var invocations = new List<AiToolInvocation>(plannedCalls.Count);
        var context = BuildContext(sessionId, AiToolInvocationChannel.Planner);
        foreach (var call in plannedCalls)
        {
            var name = call.Name?.Trim().ToLowerInvariant();
            if (!AiPlannerPolicy.IsAllowed(name) || !_registry.TryGetHandler(name!, out var handler))
                return new[] { AiToolExecutionResult.Failed("PLANNER_TOOL_NOT_ALLOWED", "Kế hoạch công cụ không nằm trong allowlist.") };

            var definition = handler.Definition;
            if (!definition.Enabled)
                return new[] { AiToolExecutionResult.Failed("TOOL_DISABLED", "Công cụ AI hiện chưa được kích hoạt.") };
            if (!string.Equals(definition.Version, call.Version?.Trim(), StringComparison.OrdinalIgnoreCase))
                return new[] { AiToolExecutionResult.Failed("TOOL_VERSION_NOT_SUPPORTED", "Phiên bản công cụ không được hỗ trợ.") };
            var invocation = new AiToolInvocation
            {
                ToolName = name!,
                ToolVersion = call.Version ?? string.Empty,
                ArgumentsJson = call.Arguments.ValueKind == JsonValueKind.Undefined ? string.Empty : call.Arguments.GetRawText(),
                SessionId = sessionId
            };
            var validation = await ValidateInvocationAsync(invocation, handler, definition, context, cancellationToken);
            if (validation != null)
                return new[] { validation };
            invocations.Add(invocation);
        }

        var results = new List<AiToolExecutionResult>(invocations.Count);
        foreach (var invocation in invocations)
            results.Add(await ExecuteCoreAsync(invocation, AiToolInvocationChannel.Planner, cancellationToken));
        return results;
    }

    public Task<AiToolExecutionResult> ExecuteHumanConfirmationAsync(
        Guid actionId,
        string sessionId,
        string? concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Task.FromResult(AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần phiên hội thoại hợp lệ để xác nhận thao tác."));

        // The channel and confirmation flag are created here, never accepted from
        // the browser or from model output.
        var arguments = JsonSerializer.Serialize(
            new { actionId, confirm = true, concurrencyToken },
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        return ExecuteCoreAsync(new AiToolInvocation
        {
            ToolName = "patient.execute_confirmed_action",
            ToolVersion = "1.0",
            ArgumentsJson = arguments,
            SessionId = sessionId
        }, AiToolInvocationChannel.DirectHumanConfirmation, cancellationToken);
    }

    public Task<AiToolExecutionResult> ExecuteRoleActionConfirmationAsync(
        Guid actionId,
        string sessionId,
        string? confirmationToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Task.FromResult(AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần phiên hội thoại hợp lệ để xác nhận thao tác."));

        var arguments = JsonSerializer.Serialize(
            new { actionId, confirm = true, concurrencyToken = confirmationToken },
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        return ExecuteCoreAsync(new AiToolInvocation
        {
            ToolName = "role.execute_confirmed_action",
            ToolVersion = "1.0",
            ArgumentsJson = arguments,
            SessionId = sessionId
        }, AiToolInvocationChannel.DirectHumanConfirmation, cancellationToken);
    }

    private async Task<AiToolExecutionResult> ExecuteCoreAsync(
        AiToolInvocation invocation,
        AiToolInvocationChannel channel,
        CancellationToken cancellationToken)
    {
        var name = invocation.ToolName?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name) || !_registry.TryGetHandler(name, out var handler))
            return AiToolExecutionResult.Failed("UNKNOWN_TOOL", "Công cụ AI không được hỗ trợ.");

        var definition = handler.Definition;
        var context = BuildContext(invocation.SessionId, channel, invocation.CorrelationId);
        var result = await ValidateInvocationAsync(invocation, handler, definition, context, cancellationToken);
        if (result == null)
        {
            try
            {
                result = await handler.ExecuteAsync(invocation, context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI tool execution failed for {ToolName}", definition.Name);
                result = AiToolExecutionResult.Failed("TOOL_EXECUTION_FAILED", "Không thể hoàn tất thao tác AI lúc này.", true);
            }
        }

        var isIdempotentConfirmationReplay = channel == AiToolInvocationChannel.DirectHumanConfirmation &&
            (result.IsIdempotentReplay ||
             string.Equals(result.ResultType, "idempotent_replay", StringComparison.Ordinal) ||
             string.Equals(result.Error?.Code, "ACTION_IN_PROGRESS", StringComparison.Ordinal));
        if (!isIdempotentConfirmationReplay)
        {
            await _audit.LogActionAsync(new AiAuditLogEntry
            {
                UserId = context.ActorId,
                SessionId = context.SessionId,
                FacilityId = context.FacilityId,
                ActionType = $"Tool:{definition.Name}",
                Outcome = result.Status,
                ErrorCode = result.Error?.Code,
                CorrelationId = context.CorrelationId,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    tool = definition.Name,
                    version = definition.Version,
                    status = result.Status,
                    confirmationRequired = result.RequiresConfirmation,
                    invocationChannel = channel.ToString(),
                    source = "ai_tool_gateway"
                })
            }, cancellationToken);
        }

        result.ToolName = definition.Name;
        return result;
    }

    private async Task<AiToolExecutionResult?> ValidateInvocationAsync(
        AiToolInvocation invocation,
        IAiToolHandler handler,
        AiToolDefinition definition,
        AiToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!definition.Enabled)
            return AiToolExecutionResult.Failed("TOOL_DISABLED", "Công cụ AI hiện chưa được kích hoạt.");
        if (!string.Equals(definition.Version, invocation.ToolVersion?.Trim(), StringComparison.OrdinalIgnoreCase))
            return AiToolExecutionResult.Failed("TOOL_VERSION_NOT_SUPPORTED", "Phiên bản công cụ không được hỗ trợ.");
        if (definition.AccessMode != AiToolAccessMode.Public && !context.IsAuthenticated)
            return AiToolExecutionResult.Failed("AUTHENTICATION_REQUIRED", "Bạn cần đăng nhập để sử dụng công cụ này.");
        if (definition.AccessMode == AiToolAccessMode.RoleRestricted && !definition.AllowedRoles.Any(context.Roles.Contains))
            return AiToolExecutionResult.Failed("FORBIDDEN_TOOL", "Vai trò hiện tại không được phép dùng công cụ này.");
        if (context.InvocationChannel == AiToolInvocationChannel.Planner && !AiPlannerPolicy.IsAllowed(definition.Name))
            return AiToolExecutionResult.Failed("PLANNER_TOOL_NOT_ALLOWED", "Kế hoạch công cụ không nằm trong allowlist.");
        if (!IsSafeArguments(invocation.ArgumentsJson))
            return AiToolExecutionResult.Failed("INVALID_TOOL_ARGUMENTS", "Tham số công cụ không hợp lệ.");
        if ((definition.Name.Equals("patient.execute_confirmed_action", StringComparison.OrdinalIgnoreCase) ||
             definition.Name.Equals("role.execute_confirmed_action", StringComparison.OrdinalIgnoreCase)) &&
            context.InvocationChannel != AiToolInvocationChannel.DirectHumanConfirmation)
            return AiToolExecutionResult.Failed("DIRECT_CONFIRMATION_REQUIRED", "Thao tác này chỉ được thực hiện qua endpoint xác nhận trực tiếp.");
        if (definition.Name.StartsWith("reception.prepare_", StringComparison.OrdinalIgnoreCase) ||
            definition.Name.StartsWith("doctor.prepare_", StringComparison.OrdinalIgnoreCase) ||
            definition.Name.StartsWith("technician.prepare_", StringComparison.OrdinalIgnoreCase) ||
            definition.Name.StartsWith("pharmacist.prepare_", StringComparison.OrdinalIgnoreCase))
        {
            if (context.InvocationChannel != AiToolInvocationChannel.DirectHumanPreparation)
                return AiToolExecutionResult.Failed("DIRECT_PREPARATION_REQUIRED", "Prepare-write chỉ được gọi qua endpoint thao tác trực tiếp đã xác thực.");
        }

        var capabilities = await _capabilityResolver.ResolveAsync(context, cancellationToken);
        if (definition.Capabilities.Any(required => !capabilities.Contains(required)))
        {
            if (definition.Name is "admin.get_revenue_summary" or "reception.get_pending_payments" or
                "doctor.get_my_appointments_today" or "technician.get_completed_today" or
                "pharmacist.get_low_stock" or "patient.get_my_invoices")
                return AiToolExecutionResult.Failed("FACILITY_SCOPE_REQUIRED", "Không xác định được phạm vi được phân quyền cho công cụ đọc.");
            return AiToolExecutionResult.Failed("FORBIDDEN_CAPABILITY", "Tài khoản hiện tại không có capability cần thiết cho công cụ này.");
        }

        AiToolArgumentValidationResult argumentValidation;
        try
        {
            argumentValidation = handler.ValidateArguments(invocation, context);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Do not log exception text or raw arguments: either may contain PHI.
            _logger.LogWarning("Tool argument validator failed for {ToolName}", definition.Name);
            return AiToolExecutionResult.Failed("INVALID_TOOL_ARGUMENTS", "Tham số công cụ không hợp lệ.");
        }
        return argumentValidation.IsValid
            ? null
            : AiToolExecutionResult.Failed(argumentValidation.Code, argumentValidation.Message);
    }

    private AiToolExecutionContext BuildContext(string? sessionId, AiToolInvocationChannel channel, string? correlationId = null)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var roles = new HashSet<AiActorRole>();
        foreach (var roleClaim in principal?.FindAll(ClaimTypes.Role) ?? Enumerable.Empty<Claim>())
        {
            if (Enum.TryParse<AiActorRole>(roleClaim.Value, true, out var role))
                roles.Add(role);
            else if (string.Equals(roleClaim.Value, "Diagnostic Technician", StringComparison.OrdinalIgnoreCase))
                roles.Add(AiActorRole.DiagnosticTechnician);
        }

        var facilityClaim = principal?.FindFirst("facility_id")?.Value;
        long? facilityId = long.TryParse(facilityClaim, out var parsedFacilityId) && parsedFacilityId > 0 ? parsedFacilityId : null;
        var actorId = _currentUser.UserId;
        return new AiToolExecutionContext
        {
            ActorId = actorId,
            IsAuthenticated = principal?.Identity?.IsAuthenticated == true && actorId.HasValue && actorId.Value != Guid.Empty,
            Roles = roles,
            SessionId = sessionId,
            FacilityId = facilityId,
            InvocationChannel = channel,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId!
        };
    }

    private static bool IsSafeArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8000) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
