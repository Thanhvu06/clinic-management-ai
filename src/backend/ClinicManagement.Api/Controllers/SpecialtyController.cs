using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Specialties.DTOs;
using ClinicManagement.Application.Specialties.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/specialties")]
public class SpecialtyController : ControllerBase
{
    private readonly ISpecialtyService _specialtyService;

    public SpecialtyController(ISpecialtyService specialtyService)
    {
        _specialtyService = specialtyService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSpecialties()
    {
        var specialties = await _specialtyService.GetSpecialtiesAsync();
        return Ok(ApiResponse<List<SpecialtyDto>>.Ok(specialties));
    }

    [HttpGet("{specialtyId}")]
    public async Task<IActionResult> GetSpecialtyById(long specialtyId)
    {
        var specialty = await _specialtyService.GetSpecialtyByIdAsync(specialtyId);
        return Ok(ApiResponse<SpecialtyDto>.Ok(specialty));
    }

    [HttpGet("{specialtyId}/doctors")]
    public async Task<IActionResult> GetDoctorsBySpecialty(long specialtyId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string sortBy = "name")
    {
        var doctors = await _specialtyService.GetDoctorsBySpecialtyAsync(specialtyId, page, pageSize, sortBy);
        return Ok(ApiResponse<PagedResult<DoctorBasicDto>>.Ok(doctors));
    }

    [HttpGet("{specialtyId}/recommended-doctors")]
    public async Task<IActionResult> GetRecommendedDoctors(long specialtyId, [FromQuery] DateOnly fromDate, [FromQuery] int days = 14)
    {
        var doctors = await _specialtyService.GetRecommendedDoctorsAsync(specialtyId, fromDate, days);
        return Ok(ApiResponse<List<RecommendedDoctorDto>>.Ok(doctors));
    }
}
