using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers.AI;

[ApiController]
[Route("api/v1/ai/tools")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai_endpoint")]
public sealed class AiToolGatewayController : ControllerBase
{
    private readonly IAiToolRegistry _registry;
    private readonly IAiToolExecutor _executor;
    private readonly IAiPendingActionCancellationService _cancellation;
    private readonly IAiCapabilityResolver _capabilityResolver;
    private readonly ICurrentUserService _currentUser;

    public AiToolGatewayController(IAiToolRegistry registry, IAiToolExecutor executor, IAiPendingActionCancellationService cancellation,
        IAiCapabilityResolver capabilityResolver, ICurrentUserService currentUser)
    {
        _registry = registry;
        _executor = executor;
        _cancellation = cancellation;
        _capabilityResolver = capabilityResolver;
        _currentUser = currentUser;
    }

    [HttpPost("/api/v1/ai/tool-actions/{actionId:guid}/cancel")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> CancelPatientAction(Guid actionId, [FromBody] CancelAiToolActionRequest request, CancellationToken cancellationToken)
    {
        var result = await _cancellation.CancelPatientActionAsync(actionId, request.SessionId, cancellationToken);
        return ToCancelResponse(result);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        var definitions = _registry.GetDefinitions();
        var actorId = _currentUser.UserId;
        if (User.Identity?.IsAuthenticated != true || !actorId.HasValue || actorId.Value == Guid.Empty)
            return Ok(ApiResponse<IReadOnlyCollection<AiToolDefinition>>.Ok(definitions.Where(d => d.AccessMode == AiToolAccessMode.Public).ToArray()));

        var roles = new HashSet<AiActorRole>();
        foreach (var claim in User.FindAll(ClaimTypes.Role))
        {
            if (Enum.TryParse<AiActorRole>(claim.Value, true, out var role)) roles.Add(role);
            else if (string.Equals(claim.Value, "Diagnostic Technician", StringComparison.OrdinalIgnoreCase)) roles.Add(AiActorRole.DiagnosticTechnician);
        }
        var facilityClaim = User.FindFirst("facility_id")?.Value;
        var capabilities = await _capabilityResolver.ResolveAsync(new AiToolExecutionContext
        {
            ActorId = actorId,
            IsAuthenticated = true,
            Roles = roles,
            FacilityId = long.TryParse(facilityClaim, out var facilityId) && facilityId > 0 ? facilityId : null
        }, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<AiToolDefinition>>.Ok(definitions
            .Where(d => (d.AccessMode != AiToolAccessMode.RoleRestricted || d.AllowedRoles.Any(roles.Contains)) &&
                        d.Capabilities.All(capabilities.Contains)).ToArray()));
    }

    [HttpPost("execute")]
    [AllowAnonymous]
    public async Task<IActionResult> Execute([FromBody] AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync(invocation, cancellationToken);
        if (result.Error?.Code is "AUTHENTICATION_REQUIRED") return Unauthorized(result);
        if (result.Error?.Code is "FORBIDDEN_TOOL") return Forbid();
        if (result.Error != null && result.Error.Code is
            "UNKNOWN_TOOL" or "TOOL_DISABLED" or "TOOL_VERSION_NOT_SUPPORTED" or
            "PLANNER_TOOL_NOT_ALLOWED" or "DIRECT_CONFIRMATION_REQUIRED" or
            "INVALID_TOOL_ARGUMENTS" or "UNKNOWN_TOOL_ARGUMENT" or "FORBIDDEN_TOOL_ARGUMENT" or
            "MISSING_TOOL_ARGUMENT" or "INVALID_CONCURRENCY_TOKEN" or "CONCURRENCY_TOKEN_REQUIRED" or
            "INVALID_IDENTIFIER" or "INVALID_LIMIT" or "INVALID_PAGE" or "INVALID_PAGE_SIZE")
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("/api/v1/ai/tool-actions/{actionId:guid}/confirm")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Confirm(
        Guid actionId,
        [FromBody] ConfirmAiToolActionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return BadRequest(AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần phiên hội thoại hợp lệ để xác nhận thao tác."));
        if (string.IsNullOrWhiteSpace(request.ConcurrencyToken))
            return BadRequest(AiToolExecutionResult.Failed("CONCURRENCY_TOKEN_REQUIRED", "Cần mã đồng bộ do hệ thống cấp để xác nhận thao tác."));

        var result = await _executor.ExecuteHumanConfirmationAsync(
            actionId,
            request.SessionId.Trim(),
            request.ConcurrencyToken,
            cancellationToken);
        if (result.Error?.Code is "AUTHENTICATION_REQUIRED" or "FORBIDDEN_TOOL" or "FORBIDDEN_CAPABILITY")
            return Forbid();
        if (result.Error?.Code is "ACTION_NOT_FOUND" or "SESSION_MISMATCH")
            return NotFound(result);
        if (result.Error?.Code is "CONCURRENCY_CONFLICT" or "ACTION_IN_PROGRESS")
            return Conflict(result);
        if (result.Error?.Code is "ACTION_EXPIRED" or "ACTION_CANCELLED")
            return StatusCode(StatusCodes.Status410Gone, result);
        if (result.Error != null && result.Error.Code is "CONCURRENCY_TOKEN_REQUIRED" or "INVALID_CONCURRENCY_TOKEN" or "INVALID_PENDING_ACTION")
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("/api/v1/ai/copilot/actions/prepare")]
    [Authorize]
    public async Task<IActionResult> PrepareRoleAction([FromBody] AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteDirectPreparationAsync(invocation, cancellationToken);
        return ToRoleActionResponse(result);
    }

    [HttpPost("/api/v1/ai/copilot/actions/{actionId:guid}/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmRoleAction(
        Guid actionId,
        [FromBody] ConfirmAiToolActionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return BadRequest(AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần phiên hội thoại hợp lệ để xác nhận thao tác."));
        if (string.IsNullOrWhiteSpace(request.ConcurrencyToken))
            return BadRequest(AiToolExecutionResult.Failed("CONCURRENCY_TOKEN_REQUIRED", "Cần mã xác nhận do hệ thống cấp để thực hiện thao tác."));

        var result = await _executor.ExecuteRoleActionConfirmationAsync(
            actionId,
            request.SessionId.Trim(),
            request.ConcurrencyToken,
            cancellationToken);
        return ToRoleActionResponse(result);
    }

    [HttpPost("/api/v1/ai/copilot/actions/{actionId:guid}/cancel")]
    [Authorize(Roles = "Receptionist,Doctor,DiagnosticTechnician,Pharmacist")]
    public async Task<IActionResult> CancelRoleAction(Guid actionId, [FromBody] CancelAiToolActionRequest request, CancellationToken cancellationToken)
    {
        var result = await _cancellation.CancelRoleActionAsync(actionId, request.SessionId, cancellationToken);
        return ToCancelResponse(result);
    }

    private IActionResult ToCancelResponse(AiToolExecutionResult result)
    {
        if (result.Error?.Code is "AUTHENTICATION_REQUIRED") return Unauthorized(result);
        if (result.Error?.Code is "ROLE_MISMATCH") return Forbid();
        if (result.Error?.Code is "ACTION_NOT_FOUND" or "SESSION_MISMATCH") return NotFound(result);
        if (result.Error?.Code is "ACTION_EXPIRED") return StatusCode(StatusCodes.Status410Gone, result);
        if (result.Error?.Code is "ACTION_NOT_CANCELLABLE") return Conflict(result);
        if (result.Error != null) return BadRequest(result);
        return Ok(result);
    }

    private IActionResult ToRoleActionResponse(AiToolExecutionResult result)
    {
        if (result.Error?.Code is "AUTHENTICATION_REQUIRED") return Unauthorized(result);
        if (result.Error?.Code is "FORBIDDEN_TOOL" or "FORBIDDEN_CAPABILITY" or "ROLE_MISMATCH" or "FACILITY_SCOPE_DENIED" or "RESOURCE_SCOPE_DENIED")
            return Forbid();
        if (result.Error?.Code is "ACTION_NOT_FOUND" or "SESSION_MISMATCH") return NotFound(result);
        if (result.Error?.Code is "ACTION_EXPIRED" or "ACTION_CANCELLED") return StatusCode(StatusCodes.Status410Gone, result);
        if (result.Error?.Code is "CONCURRENCY_CONFLICT" or "ACTION_IN_PROGRESS" or "RESOURCE_VERSION_CHANGED" or "ACTIVE_ACTION_EXISTS")
            return Conflict(result);
        if (result.Error != null) return BadRequest(result);
        return Ok(result);
    }
}

public sealed class ConfirmAiToolActionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string? ConcurrencyToken { get; set; }
}

public sealed class CancelAiToolActionRequest
{
    public string SessionId { get; set; } = string.Empty;
}
