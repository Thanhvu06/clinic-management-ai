using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers.AI;

[ApiController]
[Route("api/v1/ai/copilot")]
[Authorize]
public sealed class AiRoleCopilotController : ControllerBase
{
    private readonly IAiRoleCopilotService _service;

    public AiRoleCopilotController(IAiRoleCopilotService service) => _service = service;

    [HttpGet("catalog")]
    public IActionResult Catalog() => Ok(ApiResponse<object>.Ok(new { tools = _service.GetToolsForCurrentRole() }));

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AiCopilotRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponse { ErrorCode = "INVALID_AI_REQUEST", Message = "Nội dung copilot không hợp lệ." });
        var result = await _service.ChatAsync(request, cancellationToken);
        return Ok(ApiResponse<AiCopilotResponseDto>.Ok(result));
    }
}
