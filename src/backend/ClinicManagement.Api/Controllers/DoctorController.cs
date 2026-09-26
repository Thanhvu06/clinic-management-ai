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

    [HttpGet]
    public async Task<IActionResult> GetAllDoctors()
    {
        var doctors = await _doctorService.GetAllActiveDoctorsAsync();
        return Ok(ApiResponse<List<DoctorBasicDto>>.Ok(doctors));
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

    [HttpGet("{doctorId}/availability")]
    public async Task<IActionResult> GetDoctorAvailability(
        long doctorId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] long? specialtyId,
        [FromServices] ClinicManagement.Application.Common.Interfaces.IDateTimeProvider dateTimeProvider)
    {
        var start = fromDate ?? dateTimeProvider.VietnamToday;
        var end = toDate ?? start.AddDays(13);
        var availability = await _doctorService.GetDoctorAvailabilityAsync(doctorId, start, end, specialtyId);
        return Ok(ApiResponse<DoctorAvailabilityDto>.Ok(availability));
    }
}
