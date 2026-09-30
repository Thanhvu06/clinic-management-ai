using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ClinicManagement.Application.AI.Suggestions;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Api.Controllers.AI;

[ApiController]
[Route("api/v1/ai/copilot")]
[Authorize]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai_endpoint")]
public sealed class AiRoleCopilotController : ControllerBase
{
    private readonly IAiRoleCopilotService _service;

    public AiRoleCopilotController(IAiRoleCopilotService service) => _service = service;

    [HttpGet("catalog")]
    public IActionResult Catalog() => Ok(ApiResponse<object>.Ok(new
    {
        tools = _service.GetToolsForCurrentRole(),
        actionTools = _service.GetActionToolsForCurrentRole()
    }));

    [HttpPost("chat")]
    [TypeFilter(typeof(ServerOwnedSuggestionFilter), Order = -3000)]
    public async Task<IActionResult> Chat([FromBody] AiCopilotRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponse { ErrorCode = "INVALID_AI_REQUEST", Message = "Nội dung copilot không hợp lệ." });
        var result = await _service.ChatAsync(request, cancellationToken);
        return Ok(ApiResponse<AiCopilotResponseDto>.Ok(result));
    }

    [HttpPost("suggestions")]
    public async Task<IActionResult> Suggestions([FromBody] AiCopilotSuggestionsRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponse { ErrorCode = "INVALID_AI_REQUEST", Message = "Nội dung copilot không hợp lệ." });
        var result = await _service.GetSuggestionsAsync(request, cancellationToken);
        return Ok(ApiResponse<AiCopilotSuggestionsResponseDto>.Ok(result));
    }
}

// Runs before MVC's automatic invalid-model response. A valid, role-owned
// button ignores Message entirely, including its client-side length/content.
public sealed class ServerOwnedSuggestionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionArguments.TryGetValue("request", out var value) && value is AiCopilotRequestDto request)
        {
            var role = Enum.GetValues<AiActorRole>().FirstOrDefault(role => context.HttpContext.User.IsInRole(role.ToString()));
            var suggestion = AiSuggestionCatalog.Find(request.SuggestionCode, role);
            if (suggestion is not null)
            {
                request.Message = suggestion.Label;
                context.ModelState.Remove(nameof(request.Message));
            }
        }
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
