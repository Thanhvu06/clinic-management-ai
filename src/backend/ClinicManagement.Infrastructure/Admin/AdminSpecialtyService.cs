using System;
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

public class AdminSpecialtyService : IAdminSpecialtyService
{
    private readonly AppDbContext _dbContext;

    public AdminSpecialtyService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<AdminSpecialtyDto>> GetSpecialtiesAsync(bool? isActive, string? search, int page, int pageSize)
    {
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
