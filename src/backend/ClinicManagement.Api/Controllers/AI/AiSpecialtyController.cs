using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers.AI;

[ApiController]
[Route("api/v1/ai")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai_endpoint")]
public class AiSpecialtyController : ControllerBase
{
    private readonly IAiSpecialtyService _aiSpecialtyService;

    public AiSpecialtyController(IAiSpecialtyService aiSpecialtyService)
    {
        _aiSpecialtyService = aiSpecialtyService;
    }

    [HttpPost("specialty-suggestions")]
    [AllowAnonymous] // Assuming patient can use this before logging in or while logged in, up to requirements
    public async Task<IActionResult> SuggestSpecialty([FromBody] AiSuggestionRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Ok(ApiResponse<AiSuggestionResponseDto>.Ok(new AiSuggestionResponseDto { Outcome = "INVALID_INPUT" }));
        }

        var result = await _aiSpecialtyService.GetSuggestionsAsync(request, cancellationToken);
        
        return Ok(ApiResponse<AiSuggestionResponseDto>.Ok(result, "Xử lý thành công."));
    }
}
