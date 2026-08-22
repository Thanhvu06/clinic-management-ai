using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Specialties.DTOs;

namespace ClinicManagement.Application.Specialties.Interfaces;

public interface ISpecialtyService
{
    Task<List<SpecialtyDto>> GetSpecialtiesAsync();
    Task<SpecialtyDto> GetSpecialtyByIdAsync(long id);
    Task<PagedResult<DoctorBasicDto>> GetDoctorsBySpecialtyAsync(long specialtyId, int page, int pageSize, string sortBy);
    Task<List<RecommendedDoctorDto>> GetRecommendedDoctorsAsync(long specialtyId, DateOnly fromDate, int days);
}
