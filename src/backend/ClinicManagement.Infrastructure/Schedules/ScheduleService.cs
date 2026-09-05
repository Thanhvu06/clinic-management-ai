using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Schedules.DTOs;
using ClinicManagement.Application.Schedules.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Schedules;

public class ScheduleService : IScheduleService
{
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ScheduleService(AppDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<List<WorkScheduleDto>> GetDoctorWorkSchedulesAsync(long doctorId)
    {
        var doctorExists = await _dbContext.Doctors
            .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
            .AnyAsync(x => x.d.Id == doctorId && x.d.IsActive && x.u.IsActive);

        if (!doctorExists)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        return await _dbContext.DoctorWorkSchedules
            .AsNoTracking()
            .Where(ws => ws.DoctorId == doctorId)
            .OrderBy(ws => ws.WorkDate)
            .ThenBy(ws => ws.StartTime)
            .Select(ws => new WorkScheduleDto
            {
                Id = ws.Id,
                DoctorId = ws.DoctorId,
                WorkDate = ws.WorkDate,
                StartTime = ws.StartTime,
                EndTime = ws.EndTime,
                IsActive = ws.IsActive
            })
            .ToListAsync();
    }

    public async Task<WorkScheduleDto> CreateWorkScheduleAsync(long doctorId, CreateWorkScheduleRequest request)
    {
        var doctor = await _dbContext.Doctors
            .Include(d => d.WorkSchedules)
            .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { Doctor = d, User = u })
            .FirstOrDefaultAsync(x => x.Doctor.Id == doctorId && x.Doctor.IsActive && x.User.IsActive);

        if (doctor == null)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        if (request.StartTime >= request.EndTime)
            throw new ValidationException("StartTime", "Thời gian bắt đầu phải nhỏ hơn thời gian kết thúc.");

        var vnToday = _dateTimeProvider.VietnamToday;
        var vnTime = _dateTimeProvider.VietnamTime;

        if (request.WorkDate < vnToday || (request.WorkDate == vnToday && request.StartTime <= vnTime))
            throw new BusinessException("PAST_SCHEDULE", "Không thể tạo lịch làm việc trong quá khứ.");

        // Allow multiple shift blocks, but disallow overlapping active schedules on the same day
        var hasOverlap = await _dbContext.DoctorWorkSchedules.AnyAsync(ws =>
            ws.DoctorId == doctorId &&
            ws.WorkDate == request.WorkDate &&
            ws.IsActive &&
            ws.StartTime < request.EndTime &&
            ws.EndTime > request.StartTime);

        if (hasOverlap)
            throw new BusinessException("SCHEDULE_OVERLAP", "Khung giờ làm việc bị trùng lặp với ca làm việc khác của bác sĩ trong cùng ngày.");

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var schedule = new DoctorWorkSchedule
            {
                DoctorId = doctorId,
                WorkDate = request.WorkDate,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                IsActive = true
            };

            _dbContext.DoctorWorkSchedules.Add(schedule);
            await _dbContext.SaveChangesAsync();

            await GenerateSlotsForScheduleInternalAsync(schedule);

            await transaction.CommitAsync();

            return new WorkScheduleDto
            {
                Id = schedule.Id,
                DoctorId = schedule.DoctorId,
                WorkDate = schedule.WorkDate,
                StartTime = schedule.StartTime,
                EndTime = schedule.EndTime,
                IsActive = schedule.IsActive
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<WorkScheduleDto> UpdateWorkScheduleAsync(long scheduleId, UpdateWorkScheduleRequest request)
    {
        var schedule = await _dbContext.DoctorWorkSchedules
            .Include(ws => ws.Doctor)
            .FirstOrDefaultAsync(ws => ws.Id == scheduleId);

        if (schedule == null)
            throw new NotFoundException("Lịch làm việc không tồn tại.");

        if (request.StartTime >= request.EndTime)
            throw new ValidationException("StartTime", "Thời gian bắt đầu phải nhỏ hơn thời gian kết thúc.");

        var vnToday = _dateTimeProvider.VietnamToday;
        var vnTime = _dateTimeProvider.VietnamTime;

        if (request.WorkDate < vnToday || (request.WorkDate == vnToday && request.StartTime <= vnTime))
            throw new BusinessException("PAST_SCHEDULE", "Không thể cập nhật lịch làm việc về thời gian trong quá khứ.");

        // Check overlap with other active schedules of the same doctor on the same date
        var hasOverlap = await _dbContext.DoctorWorkSchedules.AnyAsync(ws =>
            ws.Id != scheduleId &&
            ws.DoctorId == schedule.DoctorId &&
            ws.WorkDate == request.WorkDate &&
            ws.IsActive &&
            ws.StartTime < request.EndTime &&
            ws.EndTime > request.StartTime);

        if (hasOverlap)
            throw new BusinessException("SCHEDULE_OVERLAP", "Khung giờ làm việc bị trùng lặp với ca làm việc khác của bác sĩ trong cùng ngày.");

        // Check if existing booked appointments conflict with the changed schedule
        var activeStatuses = new[]
        {
            AppointmentStatus.Pending,
            AppointmentStatus.Confirmed,
            AppointmentStatus.PendingReschedule,
            AppointmentStatus.PendingCancellation
        };

        var hasConflictingAppointments = await _dbContext.Appointments
            .AnyAsync(a => a.AppointmentSlot.DoctorId == schedule.DoctorId
                        && a.AppointmentDate == schedule.WorkDate
                        && a.AppointmentSlot.StartTime >= schedule.StartTime
                        && a.AppointmentSlot.EndTime <= schedule.EndTime
                        && (a.AppointmentDate != request.WorkDate || a.AppointmentSlot.StartTime < request.StartTime || a.AppointmentSlot.EndTime > request.EndTime)
                        && activeStatuses.Contains(a.Status));

        if (hasConflictingAppointments)
            throw new BusinessException("CONFLICT_APPOINTMENTS", "Không thể thay đổi lịch làm việc vì đã có lịch hẹn của bệnh nhân trong khung giờ này.");

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // Remove future unbooked slots for the old schedule interval
            var unbookedSlots = await _dbContext.AppointmentSlots
                .Where(s => s.DoctorId == schedule.DoctorId
                         && s.SlotDate == schedule.WorkDate
                         && s.StartTime >= schedule.StartTime
                         && s.EndTime <= schedule.EndTime
                         && !s.IsBooked)
                .ToListAsync();

            _dbContext.AppointmentSlots.RemoveRange(unbookedSlots);
            await _dbContext.SaveChangesAsync();

            // Update schedule properties
            schedule.WorkDate = request.WorkDate;
            schedule.StartTime = request.StartTime;
            schedule.EndTime = request.EndTime;
            await _dbContext.SaveChangesAsync();

            // Regenerate slots for the updated schedule
            if (schedule.IsActive)
            {
                await GenerateSlotsForScheduleInternalAsync(schedule);
            }

            await transaction.CommitAsync();

            return new WorkScheduleDto
            {
                Id = schedule.Id,
                DoctorId = schedule.DoctorId,
                WorkDate = schedule.WorkDate,
                StartTime = schedule.StartTime,
                EndTime = schedule.EndTime,
                IsActive = schedule.IsActive
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateWorkScheduleStatusAsync(long scheduleId, UpdateWorkScheduleStatusRequest request)
    {
        var schedule = await _dbContext.DoctorWorkSchedules
            .Include(ws => ws.Doctor)
            .FirstOrDefaultAsync(ws => ws.Id == scheduleId);

        if (schedule == null)
            throw new NotFoundException("Lịch làm việc không tồn tại.");

        if (schedule.IsActive == request.IsActive)
            return;

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            if (!request.IsActive)
            {
                // Deactivating: check for pending/confirmed appointments
                var hasActiveAppointments = await _dbContext.Appointments
                    .AnyAsync(a => a.AppointmentSlot.DoctorId == schedule.DoctorId
                                && a.AppointmentSlot.SlotDate == schedule.WorkDate
                                && a.AppointmentSlot.StartTime >= schedule.StartTime
                                && a.AppointmentSlot.EndTime <= schedule.EndTime
                                && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed
                                    || a.Status == AppointmentStatus.PendingReschedule || a.Status == AppointmentStatus.PendingCancellation));

                if (hasActiveAppointments)
                    throw new BusinessException("INVALID_OPERATION", "Không được ngừng lịch làm việc vì còn lịch hẹn chưa xử lý.");

                // Clean up future unbooked slots
                var unbookedSlots = await _dbContext.AppointmentSlots
                    .Where(s => s.DoctorId == schedule.DoctorId
                             && s.SlotDate == schedule.WorkDate
                             && s.StartTime >= schedule.StartTime
                             && s.EndTime <= schedule.EndTime
                             && !s.IsBooked)
                    .ToListAsync();

                _dbContext.AppointmentSlots.RemoveRange(unbookedSlots);
            }
            else
            {
                // Activating: check overlap with other active schedules
                var hasOverlap = await _dbContext.DoctorWorkSchedules.AnyAsync(ws =>
                    ws.Id != scheduleId &&
                    ws.DoctorId == schedule.DoctorId &&
                    ws.WorkDate == schedule.WorkDate &&
                    ws.IsActive &&
                    ws.StartTime < schedule.EndTime &&
                    ws.EndTime > schedule.StartTime);

                if (hasOverlap)
                    throw new BusinessException("SCHEDULE_OVERLAP", "Khung giờ làm việc bị trùng lặp với ca làm việc khác đang hoạt động.");
            }

            schedule.IsActive = request.IsActive;
            await _dbContext.SaveChangesAsync();

            if (request.IsActive)
            {
                await GenerateSlotsForScheduleInternalAsync(schedule);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task GenerateSlotsAsync(long scheduleId)
    {
        var schedule = await _dbContext.DoctorWorkSchedules
            .Include(ws => ws.Doctor)
            .FirstOrDefaultAsync(ws => ws.Id == scheduleId);

        if (schedule == null)
            throw new NotFoundException("Lịch làm việc không tồn tại.");

        if (!schedule.IsActive)
            throw new BusinessException("VALIDATION_ERROR", "Lịch làm việc đang ngừng hoạt động.");

        if (!schedule.Doctor.IsActive)
            throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ không hoạt động.");

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            await GenerateSlotsForScheduleInternalAsync(schedule);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task GenerateSlotsForScheduleInternalAsync(DoctorWorkSchedule schedule)
    {
        if (schedule.StartTime >= schedule.EndTime)
            return;

        var leaves = await _dbContext.DoctorLeaveRequests
            .AsNoTracking()
            .Where(l => l.DoctorId == schedule.DoctorId && l.Status == DoctorLeaveRequestStatus.Approved)
            .ToListAsync();

        var existingSlots = await _dbContext.AppointmentSlots
            .Where(s => s.DoctorId == schedule.DoctorId && s.SlotDate == schedule.WorkDate)
            .ToListAsync();

        var currentStartTime = schedule.StartTime;
        var slotDuration = TimeSpan.FromMinutes(30);

        while (currentStartTime.ToTimeSpan().Add(slotDuration) <= schedule.EndTime.ToTimeSpan())
        {
            var currentEndTime = TimeOnly.FromTimeSpan(currentStartTime.ToTimeSpan().Add(slotDuration));
            var slotStartDateTime = schedule.WorkDate.ToDateTime(currentStartTime);
            var slotEndDateTime = schedule.WorkDate.ToDateTime(currentEndTime);

            bool isOverlappingLeave = leaves.Any(l => slotStartDateTime < l.EndDateTime && slotEndDateTime > l.StartDateTime);

            if (!isOverlappingLeave)
            {
                bool slotExists = existingSlots.Any(s => s.StartTime == currentStartTime);
                if (!slotExists)
                {
                    var newSlot = new AppointmentSlot
                    {
                        DoctorId = schedule.DoctorId,
                        SlotDate = schedule.WorkDate,
                        StartTime = currentStartTime,
                        EndTime = currentEndTime,
                        IsBooked = false
                    };
                    _dbContext.AppointmentSlots.Add(newSlot);
                }
            }

            currentStartTime = currentEndTime;
        }

        await _dbContext.SaveChangesAsync();
    }
}
