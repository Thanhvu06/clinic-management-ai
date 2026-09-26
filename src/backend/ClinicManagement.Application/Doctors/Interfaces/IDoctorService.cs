using ClinicManagement.Application.Doctors.DTOs;
using ClinicManagement.Application.Specialties.DTOs;

namespace ClinicManagement.Application.Doctors.Interfaces;

public interface IDoctorService
{
    Task<List<DoctorBasicDto>> GetAllActiveDoctorsAsync();
    Task<DoctorDetailDto> GetDoctorByIdAsync(long doctorId);
    Task<List<SpecialtyDto>> GetSpecialtiesByDoctorAsync(long doctorId);
    Task<List<AvailableSlotDto>> GetAvailableSlotsAsync(long doctorId, DateOnly fromDate, DateOnly toDate, long? specialtyId);
    Task<DoctorAvailabilityDto> GetDoctorAvailabilityAsync(long doctorId, DateOnly fromDate, DateOnly toDate, long? specialtyId);
}
