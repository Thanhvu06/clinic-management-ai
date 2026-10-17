using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Api.Controllers;

[ApiController, Route("api/v1/icd10")]
[Authorize(Roles = RoleNames.Doctor + "," + RoleNames.Receptionist + "," + RoleNames.Admin)]
public class Icd10Controller(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? search, [FromQuery] int limit = 20)
    {
        var key = VietnameseText.ToSearchKey(search);
        if (key.Length == 0)
            return Ok(ApiResponse<List<Icd10CodeDto>>.Ok([]));

        var codePrefix = search!.Trim().ToUpperInvariant();
        var codes = await db.Icd10Codes.AsNoTracking()
            .Where(code => code.IsActive && (code.Code.StartsWith(codePrefix) || code.SearchKey.Contains(key)))
            .OrderByDescending(code => code.Code.StartsWith(codePrefix))
            .ThenBy(code => code.Code)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(code => new Icd10CodeDto(code.Code, code.NameVi, code.ChapterCode))
            .ToListAsync();
        return Ok(ApiResponse<List<Icd10CodeDto>>.Ok(codes));
    }
}
