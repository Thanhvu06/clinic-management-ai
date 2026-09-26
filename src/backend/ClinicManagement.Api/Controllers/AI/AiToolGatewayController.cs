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
}
