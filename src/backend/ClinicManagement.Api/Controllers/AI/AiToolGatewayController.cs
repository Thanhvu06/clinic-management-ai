using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Common.Models;
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

    public AiToolGatewayController(IAiToolRegistry registry, IAiToolExecutor executor)
    {
        _registry = registry;
        _executor = executor;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Catalog() => Ok(ApiResponse<IReadOnlyCollection<AiToolDefinition>>.Ok(_registry.GetDefinitions()));

    [HttpPost("execute")]
    [AllowAnonymous]
    public async Task<IActionResult> Execute([FromBody] AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync(invocation, cancellationToken);
        if (result.Error?.Code is "AUTHENTICATION_REQUIRED") return Unauthorized(result);
        if (result.Error?.Code is "FORBIDDEN_TOOL") return Forbid();
        if (result.Error != null && result.Error.Code is "UNKNOWN_TOOL" or "TOOL_DISABLED" or "TOOL_VERSION_NOT_SUPPORTED") return BadRequest(result);
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

        var result = await _executor.ExecuteHumanConfirmationAsync(
            actionId,
            request.SessionId.Trim(),
            request.ConcurrencyToken,
            cancellationToken);
        if (result.Error?.Code is "AUTHENTICATION_REQUIRED" or "FORBIDDEN_TOOL" or "FORBIDDEN_CAPABILITY")
            return Forbid();
        if (result.Error?.Code is "ACTION_NOT_FOUND" or "SESSION_MISMATCH")
            return NotFound(result);
        return Ok(result);
    }
}

public sealed class ConfirmAiToolActionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string? ConcurrencyToken { get; set; }
}
