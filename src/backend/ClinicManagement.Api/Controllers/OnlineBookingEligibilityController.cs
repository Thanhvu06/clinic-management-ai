using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/appointments/online-eligibility")]
[Authorize(Roles = "Patient")]
public sealed class OnlineBookingEligibilityController(AppDbContext db, ICurrentUserService currentUser,
    IDateTimeProvider clock, IOptions<ExpiryRulesOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var patientId = await db.Patients.Where(p => p.UserId == currentUser.UserId).Select(p => (long?)p.Id).FirstOrDefaultAsync();
        var until = patientId.HasValue ? await NoShowRestrictionRule.GetBlockedUntilAsync(db, clock, options.Value, patientId.Value) : null;
        return Ok(ApiResponse<object>.Ok(new { IsBlocked = until.HasValue, BlockedUntil = until,
            Message = until.HasValue ? NoShowRestrictionRule.Message(until.Value, options.Value) : null }));
    }
}
