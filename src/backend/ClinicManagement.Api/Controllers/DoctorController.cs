using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Doctors.DTOs;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Specialties.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/doctors")]
public class DoctorController : ControllerBase
{
    private readonly IDoctorService _doctorService;

    public DoctorController(IDoctorService doctorService)
    {
        _doctorService = doctorService;
    }

    [HttpGet("{doctorId}")]
    public async Task<IActionResult> GetDoctorById(long doctorId)
    {
        var doctor = await _doctorService.GetDoctorByIdAsync(doctorId);
        return Ok(ApiResponse<DoctorDetailDto>.Ok(doctor));
    }

    [HttpGet("{doctorId}/specialties")]
    public async Task<IActionResult> GetSpecialtiesByDoctor(long doctorId)
    {
        var specialties = await _doctorService.GetSpecialtiesByDoctorAsync(doctorId);
        return Ok(ApiResponse<List<SpecialtyDto>>.Ok(specialties));
    }

    [HttpGet("{doctorId}/available-slots")]
    public async Task<IActionResult> GetAvailableSlots(long doctorId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, [FromQuery] long? specialtyId)
    {
        var slots = await _doctorService.GetAvailableSlotsAsync(doctorId, fromDate, toDate, specialtyId);
        return Ok(ApiResponse<List<AvailableSlotDto>>.Ok(slots));
    }
}
