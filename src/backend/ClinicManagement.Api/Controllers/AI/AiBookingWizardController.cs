using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.Api.Controllers.AI;

[ApiController, Route("api/v1/ai/booking-wizard")]
[Authorize(Roles = "Patient"), EnableRateLimiting("ai_endpoint")]
public sealed class AiBookingWizardController(AiBookingWizardService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Step(AiBookingWizardRequestDto request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<AiBookingWizardResponseDto>.Ok(await service.StepAsync(request, cancellationToken)));
}
