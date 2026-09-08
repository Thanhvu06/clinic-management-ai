using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Specialties.DTOs;
using ClinicManagement.Application.Specialties.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Specialties;

public class SpecialtyService : ISpecialtyService
{
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAppointmentAvailabilityPolicy _availabilityPolicy;

    public SpecialtyService(
        AppDbContext dbContext,
        IDateTimeProvider dateTimeProvider,
        IAppointmentAvailabilityPolicy availabilityPolicy)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
        _availabilityPolicy = availabilityPolicy;
    }

    public async Task<List<SpecialtyDto>> GetSpecialtiesAsync()
    {
        return await _dbContext.Specialties
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => new SpecialtyDto
            {
                Id = s.Id,
                SpecialtyCode = s.SpecialtyCode,
                SpecialtyName = s.Name,
                Description = s.Description ?? string.Empty,
                AiEnabled = s.AiEnabled
            })
            .ToListAsync();
    }

    public async Task<SpecialtyDto> GetSpecialtyByIdAsync(long id)
    {
        var specialty = await _dbContext.Specialties
            .AsNoTracking()
            .Where(s => s.IsActive && s.Id == id)
            .Select(s => new SpecialtyDto
            {
                Id = s.Id,
                SpecialtyCode = s.SpecialtyCode,
                SpecialtyName = s.Name,
                Description = s.Description ?? string.Empty,
                AiEnabled = s.AiEnabled
            })
            .FirstOrDefaultAsync();

        if (specialty == null)
            throw new NotFoundException("Chuyên khoa không tồn tại hoặc đã ngừng hoạt động.");

        return specialty;
    }

    public async Task<PagedResult<DoctorBasicDto>> GetDoctorsBySpecialtyAsync(long specialtyId, int page, int pageSize, string sortBy)
    {
        var specialtyExists = await _dbContext.Specialties.AnyAsync(s => s.Id == specialtyId && s.IsActive);
        if (!specialtyExists)
            throw new NotFoundException("Chuyên khoa không tồn tại hoặc đã ngừng hoạt động.");

        var query = from ds in _dbContext.DoctorSpecialties
                    join d in _dbContext.Doctors on ds.DoctorId equals d.Id
                    join u in _dbContext.Users on d.UserId equals u.Id
                    where ds.SpecialtyId == specialtyId && d.IsActive && u.IsActive
                    select new
                    {
                        d.Id,
                        u.FullName,
                        d.AcademicTitle,
                        d.ExperienceYears
                    };

        if (sortBy?.ToLower() == "name")
        {
            query = query.OrderBy(x => x.FullName);
        }
        else
        {
            query = query.OrderBy(x => x.Id);
        }

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new DoctorBasicDto
            {
                Id = x.Id,
                FullName = x.FullName,
                AcademicTitle = x.AcademicTitle ?? string.Empty,
                ExperienceYears = x.ExperienceYears
            })
            .ToListAsync();

        return new PagedResult<DoctorBasicDto>(items, totalItems, page, pageSize);
    }

    public async Task<List<RecommendedDoctorDto>> GetRecommendedDoctorsAsync(long specialtyId, DateOnly fromDate, int days)
    {
        var specialtyExists = await _dbContext.Specialties.AnyAsync(s => s.Id == specialtyId && s.IsActive);
        if (!specialtyExists)
            throw new NotFoundException("Chuyên khoa không tồn tại hoặc đã ngừng hoạt động.");

        var doctors = await (from ds in _dbContext.DoctorSpecialties
                             join d in _dbContext.Doctors on ds.DoctorId equals d.Id
                             join u in _dbContext.Users on d.UserId equals u.Id
                             where ds.SpecialtyId == specialtyId && d.IsActive && u.IsActive
                             select new
                             {
                                 d.Id,
                                 u.FullName,
                                 d.ExperienceYears
                             }).AsNoTracking().ToListAsync();

        var toDate = fromDate.AddDays(days - 1);
        var doctorIds = doctors.Select(d => d.Id).ToList();

        var validSlots = await _availabilityPolicy.GetAvailableSlotsAsync(new BatchSlotAvailabilityRequest
        {
            DoctorIds = doctorIds,
            SpecialtyId = specialtyId,
            FromDate = fromDate,
            ToDate = toDate
        });

        var earliestSlots = validSlots
            .GroupBy(s => s.DoctorId)
            .Select(g => new
            {
                DoctorId = g.Key,
                EarliestSlot = g.OrderBy(s => s.SlotDate).ThenBy(s => s.StartTime).First()
            })
            .ToList();

        var recommendations = new List<RecommendedDoctorDto>();

        foreach (var doc in doctors)
        {
            var earliest = earliestSlots.FirstOrDefault(s => s.DoctorId == doc.Id)?.EarliestSlot;
            
            double experienceScore = Math.Min(doc.ExperienceYears, 20) / 20.0;
            double availabilityScore = 0;
            DateTimeOffset? earliestTime = null;
            
            if (earliest != null)
            {
                earliestTime = new DateTimeOffset(earliest.SlotDate.ToDateTime(earliest.StartTime), TimeSpan.FromHours(7)); 
                var diffDays = earliest.SlotDate.DayNumber - fromDate.DayNumber;
                if (diffDays >= 0 && diffDays <= 2) availabilityScore = 1.0;
                else if (diffDays >= 3 && diffDays <= 5) availabilityScore = 0.7;
                else if (diffDays >= 6 && diffDays <= 10) availabilityScore = 0.4;
                else if (diffDays >= 11 && diffDays <= 14) availabilityScore = 0.1;
            }

            double totalScore = (0.4 * experienceScore) + (0.6 * availabilityScore);

            recommendations.Add(new RecommendedDoctorDto
            {
                DoctorId = doc.Id,
                FullName = doc.FullName,
                ExperienceYears = doc.ExperienceYears,
                ExperienceScore = Math.Round(experienceScore, 2),
                AvailabilityScore = Math.Round(availabilityScore, 2),
                TotalScore = Math.Round(totalScore, 2),
                EarliestAvailableSlot = earliestTime
            });
        }

        return recommendations
            .OrderByDescending(r => r.TotalScore)
            .ThenBy(r => r.EarliestAvailableSlot ?? DateTimeOffset.MaxValue)
            .ThenBy(r => r.FullName)
            .ToList();
    }
}
