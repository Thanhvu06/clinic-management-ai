using System;
using System.Threading.Tasks;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Admin.Interfaces;

public interface IAdminUserService
{
    Task<PagedResult<UserDto>> GetUsersAsync(string? role, bool? isActive, string? search, int page, int pageSize);
    Task<UserDto> GetUserByIdAsync(Guid id);
    Task<UserDto> CreateStaffUserAsync(CreateStaffUserDto request);
    Task ToggleUserStatusAsync(Guid id, ToggleUserStatusDto request);
}

public interface IAdminSpecialtyService
{
    Task<PagedResult<AdminSpecialtyDto>> GetSpecialtiesAsync(bool? isActive, string? search, int page, int pageSize);
    Task<AdminSpecialtyDto> GetSpecialtyByIdAsync(long id);
    Task<AdminSpecialtyDto> CreateSpecialtyAsync(CreateSpecialtyDto request);
    Task<AdminSpecialtyDto> UpdateSpecialtyAsync(long id, UpdateSpecialtyDto request);
}

public interface IAdminDoctorService
{
    Task<PagedResult<AdminDoctorDto>> GetDoctorsAsync(bool? isActive, string? search, int page, int pageSize);
    Task<AdminDoctorDto> GetDoctorByIdAsync(long id);
    Task<AdminDoctorDto> CreateDoctorAsync(CreateDoctorDto request);
    Task<AdminDoctorDto> UpdateDoctorAsync(long id, UpdateDoctorDto request);
    Task AssignSpecialtiesAsync(long doctorId, System.Collections.Generic.List<AssignSpecialtyDto> specialties);
}
