using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.DTOs;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Specialties.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Doctors;

public class DoctorService : IDoctorService
{
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DoctorService(AppDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
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
                          SpecialtyName = primarySpec != null ? primarySpec.Name : string.Empty,
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
        var doctorExists = await _dbContext.Doctors
            .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
            .AnyAsync(x => x.d.Id == doctorId && x.d.IsActive && x.u.IsActive);

        if (!doctorExists)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        return await (from ds in _dbContext.DoctorSpecialties
                      join s in _dbContext.Specialties on ds.SpecialtyId equals s.Id
                      where ds.DoctorId == doctorId && s.IsActive
                      orderby ds.IsPrimary ? 0 : 1
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
        var doctorExists = await _dbContext.Doctors
            .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
            .AnyAsync(x => x.d.Id == doctorId && x.d.IsActive && x.u.IsActive);

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

        var dateToday = _dateTimeProvider.VietnamToday;
        var timeNow = _dateTimeProvider.VietnamTime;
        var vnNow = _dateTimeProvider.VietnamNow;

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
            .Where(l => l.DoctorId == doctorId && l.Status == DoctorLeaveRequestStatus.Approved 
                     && l.EndDateTime >= vnNow)
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

    public async Task<DoctorAvailabilityDto> GetDoctorAvailabilityAsync(long doctorId, DateOnly fromDate, DateOnly toDate, long? specialtyId)
    {
        var doctor = await (from d in _dbContext.Doctors
                            join u in _dbContext.Users on d.UserId equals u.Id
                            where d.Id == doctorId && d.IsActive && u.IsActive
                            select new
                            {
                                d.Id,
                                u.FullName,
                                AcademicTitle = d.AcademicTitle ?? string.Empty
                            }).AsNoTracking().FirstOrDefaultAsync();

        if (doctor == null)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        if (toDate < fromDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        // Limit range to max 30 days
        if (toDate.DayNumber - fromDate.DayNumber > 30)
        {
            toDate = fromDate.AddDays(30);
        }

        var availableSlots = await GetAvailableSlotsAsync(doctorId, fromDate, toDate, specialtyId);

        var schedules = await _dbContext.DoctorWorkSchedules
            .AsNoTracking()
            .Where(ws => ws.DoctorId == doctorId && ws.IsActive && ws.WorkDate >= fromDate && ws.WorkDate <= toDate)
            .OrderBy(ws => ws.WorkDate).ThenBy(ws => ws.StartTime)
            .ToListAsync();

        var scheduleLookup = schedules
            .GroupBy(s => s.WorkDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var slotLookup = availableSlots
            .GroupBy(s => s.SlotDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var days = new List<DoctorDayAvailabilityDto>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            scheduleLookup.TryGetValue(d, out var daySchedules);
            slotLookup.TryGetValue(d, out var daySlots);

            var blocks = daySchedules != null
                ? daySchedules.Select(s => new ScheduleBlockDto { StartTime = s.StartTime, EndTime = s.EndTime }).ToList()
                : new List<ScheduleBlockDto>();

            days.Add(new DoctorDayAvailabilityDto
            {
                Date = d,
                DayOfWeek = d.DayOfWeek.ToString(),
                HasWorkSchedule = blocks.Count > 0,
                ScheduleBlocks = blocks,
                AvailableSlots = daySlots ?? new List<AvailableSlotDto>()
            });
        }

        return new DoctorAvailabilityDto
        {
            DoctorId = doctor.Id,
            DoctorName = doctor.FullName,
            AcademicTitle = doctor.AcademicTitle,
            Timezone = "Asia/Ho_Chi_Minh",
            FromDate = fromDate,
            ToDate = toDate,
            Days = days
        };
    }
}
