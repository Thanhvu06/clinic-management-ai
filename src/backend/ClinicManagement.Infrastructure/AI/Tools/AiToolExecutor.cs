using System.Security.Claims;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed class AiToolExecutor : IAiToolExecutor
{
    private readonly IAiToolRegistry _registry;
    private readonly ICurrentUserService _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAiAuditService _audit;
    private readonly ILogger<AiToolExecutor> _logger;

    public AiToolExecutor(
        IAiToolRegistry registry,
        ICurrentUserService currentUser,
        IHttpContextAccessor httpContextAccessor,
        IAiAuditService audit,
        ILogger<AiToolExecutor> logger)
    {
        _registry = registry;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _audit = audit;
        _logger = logger;
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var name = invocation.ToolName?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name) || !_registry.TryGetHandler(name, out var handler))
            return AiToolExecutionResult.Failed("UNKNOWN_TOOL", "Công cụ AI không được hỗ trợ.");

        var definition = handler.Definition;
        var context = BuildContext(invocation);
        AiToolExecutionResult result;

        if (!definition.Enabled)
            result = AiToolExecutionResult.Failed("TOOL_DISABLED", "Công cụ AI hiện chưa được kích hoạt.");
        else if (!string.Equals(definition.Version, invocation.ToolVersion?.Trim(), StringComparison.OrdinalIgnoreCase))
            result = AiToolExecutionResult.Failed("TOOL_VERSION_NOT_SUPPORTED", "Phiên bản công cụ không được hỗ trợ.");
        else if (definition.AccessMode != AiToolAccessMode.Public && !context.IsAuthenticated)
            result = AiToolExecutionResult.Failed("AUTHENTICATION_REQUIRED", "Bạn cần đăng nhập để sử dụng công cụ này.");
        else if (definition.AccessMode == AiToolAccessMode.RoleRestricted &&
                 !definition.AllowedRoles.Any(context.Roles.Contains))
            result = AiToolExecutionResult.Failed("FORBIDDEN_TOOL", "Vai trò hiện tại không được phép dùng công cụ này.");
        else if (!IsSafeArguments(invocation.ArgumentsJson))
            result = AiToolExecutionResult.Failed("INVALID_TOOL_ARGUMENTS", "Tham số công cụ không hợp lệ.");
        else
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
                source = "ai_tool_gateway"
            })
        }, cancellationToken);

        return result;
    }

    private AiToolExecutionContext BuildContext(AiToolInvocation invocation)
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

        var actorId = _currentUser.UserId;
        var facilityClaim = principal?.FindFirst("facility_id")?.Value;
        long? facilityId = long.TryParse(facilityClaim, out var parsedFacilityId) && parsedFacilityId > 0 ? parsedFacilityId : null;
        return new AiToolExecutionContext
        {
            ActorId = actorId,
            IsAuthenticated = principal?.Identity?.IsAuthenticated == true && actorId.HasValue && actorId.Value != Guid.Empty,
            Roles = roles,
            SessionId = invocation.SessionId,
            FacilityId = facilityId,
            CorrelationId = string.IsNullOrWhiteSpace(invocation.CorrelationId) ? Guid.NewGuid().ToString("N") : invocation.CorrelationId!
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
