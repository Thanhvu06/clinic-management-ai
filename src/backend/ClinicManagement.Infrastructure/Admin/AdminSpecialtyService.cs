using System;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Admin.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Admin;

public class AdminSpecialtyService : IAdminSpecialtyService
{
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AdminSpecialtyService(AppDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PagedResult<AdminSpecialtyDto>> GetSpecialtiesAsync(bool? isActive, string? search, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);
        var query = _dbContext.Specialties.AsNoTracking();

        if (isActive.HasValue)
        {
            query = query.Where(s => s.IsActive == isActive.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(s => s.Name.Contains(search) || s.SpecialtyCode.Contains(search));
        }

        query = query.OrderBy(s => s.SpecialtyCode);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var dtos = items.Select(MapToDto).ToList();

        return new PagedResult<AdminSpecialtyDto>(dtos, totalItems, page, pageSize);
    }

    public async Task<AdminSpecialtyDto> GetSpecialtyByIdAsync(long id)
    {
        var specialty = await _dbContext.Specialties.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (specialty == null) throw new NotFoundException("Chuyên khoa không tồn tại.");
        return MapToDto(specialty);
    }

    public async Task<AdminSpecialtyDto> CreateSpecialtyAsync(CreateSpecialtyDto request)
    {
        var exists = await _dbContext.Specialties.AnyAsync(s => s.SpecialtyCode == request.SpecialtyCode);
        if (exists) throw new BusinessException("CODE_EXISTS", "Mã chuyên khoa đã tồn tại.");

        var specialty = new Specialty
        {
            SpecialtyCode = request.SpecialtyCode,
            Name = request.Name,
            Description = request.Description,
            IsActive = request.IsActive,
            AiEnabled = false // default, not updated via basic admin in this batch
        };

        _dbContext.Specialties.Add(specialty);
        await _dbContext.SaveChangesAsync();

        return MapToDto(specialty);
    }

    public async Task<AdminSpecialtyDto> UpdateSpecialtyAsync(long id, UpdateSpecialtyDto request)
    {
        var specialty = await _dbContext.Specialties.FirstOrDefaultAsync(s => s.Id == id);
        if (specialty == null) throw new NotFoundException("Chuyên khoa không tồn tại.");

        if (specialty.IsActive && !request.IsActive)
        {
            var today = _dateTimeProvider.VietnamToday;
            var count = await _dbContext.Appointments.CountAsync(a => a.SpecialtyId == id && a.AppointmentDate >= today &&
                AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status));
            if (count > 0)
                throw new BusinessException("SPECIALTY_HAS_ACTIVE_APPOINTMENTS", $"Chuyên khoa còn {count} lịch hẹn cần xử lý trước khi ngừng hoạt động.");
        }

        specialty.Name = request.Name;
        specialty.Description = request.Description;
        specialty.IsActive = request.IsActive;
        // Do not update Code or AiEnabled here

        await _dbContext.SaveChangesAsync();

        return MapToDto(specialty);
    }

    private static AdminSpecialtyDto MapToDto(Specialty s) => new()
    {
        Id = s.Id,
        SpecialtyCode = s.SpecialtyCode,
        Name = s.Name,
        Description = s.Description ?? string.Empty,
        IsActive = s.IsActive,
        AiEnabled = s.AiEnabled
    };
}
