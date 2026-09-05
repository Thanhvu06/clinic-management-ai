using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Doctors.DTOs;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Specialties.DTOs;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Doctors;

public class DoctorService : IDoctorService
{
    private readonly AppDbContext _dbContext;

    public DoctorService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<DoctorBasicDto>> GetAllActiveDoctorsAsync()
    {
        return await (from d in _dbContext.Doctors
                      join u in _dbContext.Users on d.UserId equals u.Id
                      where d.IsActive && u.IsActive
                      let primarySpec = (from ds in _dbContext.DoctorSpecialties
                                         join s in _dbContext.Specialties on ds.SpecialtyId equals s.Id
                                         where ds.DoctorId == d.Id && s.IsActive
                                         orderby ds.IsPrimary ? 0 : 1
                                         select s).FirstOrDefault()
                      select new DoctorBasicDto
                      {
                          Id = d.Id,
                          FullName = u.FullName,
                          AcademicTitle = d.AcademicTitle ?? "",
                          ExperienceYears = d.ExperienceYears,
                          SpecialtyId = primarySpec != null ? primarySpec.Id : null,
                          SpecialtyName = primarySpec != null ? primarySpec.Name : "Bác sĩ Đa khoa",
                          Description = d.Description ?? ""
                      }).ToListAsync();
    }

    public async Task<DoctorDetailDto> GetDoctorByIdAsync(long doctorId)
    {
        var doctor = await (from d in _dbContext.Doctors
                            join u in _dbContext.Users on d.UserId equals u.Id
                            where d.Id == doctorId && d.IsActive && u.IsActive
                            select new DoctorDetailDto
                            {
                                Id = d.Id,
                                FullName = u.FullName,
                                AcademicTitle = d.AcademicTitle ?? string.Empty,
                                ExperienceYears = d.ExperienceYears,
                                Description = d.Description ?? string.Empty
                            }).AsNoTracking().FirstOrDefaultAsync();

        if (doctor == null)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        return doctor;
    }

    public async Task<List<SpecialtyDto>> GetSpecialtiesByDoctorAsync(long doctorId)
    {
        var doctorExists = await _dbContext.Doctors.AnyAsync(d => d.Id == doctorId && d.IsActive);
        if (!doctorExists)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        return await (from ds in _dbContext.DoctorSpecialties
                      join s in _dbContext.Specialties on ds.SpecialtyId equals s.Id
                      where ds.DoctorId == doctorId && s.IsActive
                      select new SpecialtyDto
                      {
                          Id = s.Id,
                          SpecialtyCode = s.SpecialtyCode,
                          SpecialtyName = s.Name,
                          Description = s.Description ?? string.Empty,
                          AiEnabled = s.AiEnabled
                      }).AsNoTracking().ToListAsync();
    }

    public async Task<List<AvailableSlotDto>> GetAvailableSlotsAsync(long doctorId, DateOnly fromDate, DateOnly toDate, long? specialtyId)
    {
        var doctorExists = await _dbContext.Doctors.AnyAsync(d => d.Id == doctorId && d.IsActive);
        if (!doctorExists)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        if (specialtyId.HasValue)
        {
            var hasSpecialty = await _dbContext.DoctorSpecialties.AnyAsync(ds => ds.DoctorId == doctorId && ds.SpecialtyId == specialtyId.Value);
            if (!hasSpecialty)
                throw new NotFoundException("Bác sĩ không thuộc chuyên khoa này.");
            
            var specialtyActive = await _dbContext.Specialties.AnyAsync(s => s.Id == specialtyId.Value && s.IsActive);
            if (!specialtyActive)
                throw new BusinessException("SPECIALTY_NOT_AVAILABLE", "Chuyên khoa không hoạt động.");
        }

        var today = DateTime.UtcNow;
        var dateToday = DateOnly.FromDateTime(today);
        var timeNow = TimeOnly.FromDateTime(today);

        var slots = await _dbContext.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.DoctorId == doctorId
                        && !s.IsBooked
                        && s.SlotDate >= fromDate
                        && s.SlotDate <= toDate
                        && (s.SlotDate > dateToday || (s.SlotDate == dateToday && s.StartTime > timeNow)))
            .OrderBy(s => s.SlotDate).ThenBy(s => s.StartTime)
            .ToListAsync();

        var leaves = await _dbContext.DoctorLeaveRequests
            .AsNoTracking()
            .Where(l => l.DoctorId == doctorId && l.Status == ClinicManagement.Domain.Enums.DoctorLeaveRequestStatus.Approved 
                     && l.EndDateTime >= today)
            .ToListAsync();

        var validSlots = slots.Where(s => {
            var slotStart = s.SlotDate.ToDateTime(s.StartTime);
            var slotEnd = s.SlotDate.ToDateTime(s.EndTime);
            return !leaves.Any(l => slotStart < l.EndDateTime && slotEnd > l.StartDateTime);
        }).Select(s => new AvailableSlotDto
        {
            SlotId = s.Id,
            DoctorId = s.DoctorId,
            SlotDate = s.SlotDate,
            StartTime = s.StartTime,
            EndTime = s.EndTime
        }).ToList();

        return validSlots;
    }
}
