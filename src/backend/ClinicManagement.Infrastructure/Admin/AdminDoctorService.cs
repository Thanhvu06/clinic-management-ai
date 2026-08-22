using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Admin.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Admin;

public class AdminDoctorService : IAdminDoctorService
{
    private readonly AppDbContext _dbContext;

    public AdminDoctorService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<AdminDoctorDto>> GetDoctorsAsync(bool? isActive, string? search, int page, int pageSize)
    {
        var query = from d in _dbContext.Doctors
                    join u in _dbContext.Users on d.UserId equals u.Id
                    select new
                    {
                        Doctor = d,
                        User = u
                    };

        if (isActive.HasValue)
        {
            query = query.Where(x => x.Doctor.IsActive == isActive.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(x => x.User.FullName.Contains(search) || x.User.Email!.Contains(search));
        }

        query = query.OrderBy(x => x.User.FullName);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var doctorIds = items.Select(x => x.Doctor.Id).ToList();
        var specialties = await _dbContext.DoctorSpecialties
            .Include(ds => ds.Specialty)
            .Where(ds => doctorIds.Contains(ds.DoctorId))
            .ToListAsync();

        var dtos = items.Select(x => new AdminDoctorDto
        {
            Id = x.Doctor.Id,
            UserId = x.Doctor.UserId,
            FullName = x.User.FullName,
            Email = x.User.Email ?? string.Empty,
            AcademicTitle = x.Doctor.AcademicTitle,
            ExperienceYears = x.Doctor.ExperienceYears,
            Description = x.Doctor.Description,
            IsActive = x.Doctor.IsActive,
            Specialties = specialties.Where(s => s.DoctorId == x.Doctor.Id).Select(s => new DoctorSpecialtyDto
            {
                SpecialtyId = s.SpecialtyId,
                SpecialtyName = s.Specialty.Name,
                IsPrimary = s.IsPrimary
            }).ToList()
        }).ToList();

        return new PagedResult<AdminDoctorDto>(dtos, totalItems, page, pageSize);
    }

    public async Task<AdminDoctorDto> GetDoctorByIdAsync(long id)
    {
        var data = await (from d in _dbContext.Doctors
                          join u in _dbContext.Users on d.UserId equals u.Id
                          where d.Id == id
                          select new { Doctor = d, User = u })
                          .FirstOrDefaultAsync();

        if (data == null) throw new NotFoundException("Bác sĩ không tồn tại.");

        var specialties = await _dbContext.DoctorSpecialties
            .Include(ds => ds.Specialty)
            .Where(ds => ds.DoctorId == id)
            .ToListAsync();

        return new AdminDoctorDto
        {
            Id = data.Doctor.Id,
            UserId = data.Doctor.UserId,
            FullName = data.User.FullName,
            Email = data.User.Email ?? string.Empty,
            AcademicTitle = data.Doctor.AcademicTitle,
            ExperienceYears = data.Doctor.ExperienceYears,
            Description = data.Doctor.Description,
            IsActive = data.Doctor.IsActive,
            Specialties = specialties.Select(s => new DoctorSpecialtyDto
            {
                SpecialtyId = s.SpecialtyId,
                SpecialtyName = s.Specialty.Name,
                IsPrimary = s.IsPrimary
            }).ToList()
        };
    }

    public async Task<AdminDoctorDto> CreateDoctorAsync(CreateDoctorDto request)
    {
        var existingDoctor = await _dbContext.Doctors.AnyAsync(d => d.UserId == request.UserId);
        if (existingDoctor) throw new BusinessException("DOCTOR_EXISTS", "User này đã có hồ sơ bác sĩ.");

        // Verify User exists and has Doctor role
        var userRoles = await (from ur in _dbContext.UserRoles
                               join r in _dbContext.Roles on ur.RoleId equals r.Id
                               where ur.UserId == request.UserId && r.Name == "Doctor"
                               select r).AnyAsync();
        
        if (!userRoles)
            throw new BusinessException("INVALID_USER", "Tài khoản không tồn tại hoặc không có quyền Bác sĩ.");

        if (request.Specialties.Count == 0)
            throw new BusinessException("INVALID_SPECIALTY", "Bác sĩ phải có ít nhất 1 chuyên khoa.");

        if (request.Specialties.Count(s => s.IsPrimary) != 1)
            throw new BusinessException("INVALID_SPECIALTY", "Bác sĩ phải có đúng 1 chuyên khoa chính.");

        var uniqueSpecialtyIds = request.Specialties.Select(s => s.SpecialtyId).Distinct().ToList();
        if (uniqueSpecialtyIds.Count != request.Specialties.Count)
            throw new BusinessException("INVALID_SPECIALTY", "Danh sách chuyên khoa bị trùng lặp.");

        var existingSpecialtiesCount = await _dbContext.Specialties.CountAsync(s => uniqueSpecialtyIds.Contains(s.Id) && s.IsActive);
        if (existingSpecialtiesCount != uniqueSpecialtyIds.Count)
            throw new BusinessException("INVALID_SPECIALTY", "Một số chuyên khoa không tồn tại hoặc không hoạt động.");

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var doctor = new Doctor
            {
                UserId = request.UserId,
                AcademicTitle = request.AcademicTitle,
                ExperienceYears = request.ExperienceYears,
                Description = request.Description,
                IsActive = request.IsActive
            };

            _dbContext.Doctors.Add(doctor);
            await _dbContext.SaveChangesAsync();

            foreach (var s in request.Specialties)
            {
                _dbContext.DoctorSpecialties.Add(new DoctorSpecialty
                {
                    DoctorId = doctor.Id,
                    SpecialtyId = s.SpecialtyId,
                    IsPrimary = s.IsPrimary
                });
            }
            await _dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            return await GetDoctorByIdAsync(doctor.Id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<AdminDoctorDto> UpdateDoctorAsync(long id, UpdateDoctorDto request)
    {
        var doctor = await _dbContext.Doctors.FirstOrDefaultAsync(d => d.Id == id);
        if (doctor == null) throw new NotFoundException("Bác sĩ không tồn tại.");

        doctor.AcademicTitle = request.AcademicTitle;
        doctor.ExperienceYears = request.ExperienceYears;
        doctor.Description = request.Description;
        doctor.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync();
        return await GetDoctorByIdAsync(id);
    }

    public async Task AssignSpecialtiesAsync(long doctorId, List<AssignSpecialtyDto> specialties)
    {
        if (specialties.Count == 0)
            throw new BusinessException("INVALID_SPECIALTY", "Bác sĩ phải có ít nhất 1 chuyên khoa.");

        if (specialties.Count(s => s.IsPrimary) != 1)
            throw new BusinessException("INVALID_SPECIALTY", "Bác sĩ phải có đúng 1 chuyên khoa chính.");

        var uniqueSpecialtyIds = specialties.Select(s => s.SpecialtyId).Distinct().ToList();
        if (uniqueSpecialtyIds.Count != specialties.Count)
            throw new BusinessException("INVALID_SPECIALTY", "Danh sách chuyên khoa bị trùng lặp.");

        var doctor = await _dbContext.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId);
        if (doctor == null) throw new NotFoundException("Bác sĩ không tồn tại.");

        var existingSpecialtiesCount = await _dbContext.Specialties.CountAsync(s => uniqueSpecialtyIds.Contains(s.Id) && s.IsActive);
        if (existingSpecialtiesCount != uniqueSpecialtyIds.Count)
            throw new BusinessException("INVALID_SPECIALTY", "Một số chuyên khoa không tồn tại hoặc không hoạt động.");

        // Cannot easily check all appointments related constraints via simple query, 
        // but if a specialty is removed, we should ensure no future appointments exist for it.
        // Actually, we can check if removed specialties have pending/confirmed appointments.
        var currentSpecialtyIds = await _dbContext.DoctorSpecialties.Where(ds => ds.DoctorId == doctorId).Select(ds => ds.SpecialtyId).ToListAsync();
        var removedSpecialtyIds = currentSpecialtyIds.Except(uniqueSpecialtyIds).ToList();

        if (removedSpecialtyIds.Any())
        {
            var hasActiveAppointments = await _dbContext.Appointments
                .AnyAsync(a => a.DoctorId == doctorId 
                            && removedSpecialtyIds.Contains(a.SpecialtyId)
                            && (a.Status == ClinicManagement.Domain.Enums.AppointmentStatus.Pending || 
                                a.Status == ClinicManagement.Domain.Enums.AppointmentStatus.Confirmed ||
                                a.Status == ClinicManagement.Domain.Enums.AppointmentStatus.PendingReschedule ||
                                a.Status == ClinicManagement.Domain.Enums.AppointmentStatus.PendingCancellation));
            if (hasActiveAppointments)
                throw new BusinessException("CANNOT_REMOVE_SPECIALTY", "Không thể gỡ chuyên khoa đang có lịch hẹn chưa hoàn thành.");
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var currentDs = await _dbContext.DoctorSpecialties.Where(ds => ds.DoctorId == doctorId).ToListAsync();
            _dbContext.DoctorSpecialties.RemoveRange(currentDs);
            await _dbContext.SaveChangesAsync();

            foreach (var s in specialties)
            {
                _dbContext.DoctorSpecialties.Add(new DoctorSpecialty
                {
                    DoctorId = doctorId,
                    SpecialtyId = s.SpecialtyId,
                    IsPrimary = s.IsPrimary
                });
            }
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
