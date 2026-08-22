using ClinicManagement.Application.Common.Exceptions;
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

    public ScheduleService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<WorkScheduleDto>> GetDoctorWorkSchedulesAsync(long doctorId)
    {
        var doctorExists = await _dbContext.Doctors.AnyAsync(d => d.Id == doctorId);
        if (!doctorExists)
            throw new NotFoundException("Bác sĩ không tồn tại.");

        return await _dbContext.DoctorWorkSchedules
            .AsNoTracking()
            .Where(ws => ws.DoctorId == doctorId)
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
        var doctorExists = await _dbContext.Doctors.AnyAsync(d => d.Id == doctorId);
        if (!doctorExists)
            throw new NotFoundException("Bác sĩ không tồn tại.");

        if (request.StartTime >= request.EndTime)
            throw new ValidationException("Thời gian bắt đầu phải nhỏ hơn thời gian kết thúc.");

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

    public async Task<WorkScheduleDto> UpdateWorkScheduleAsync(long scheduleId, UpdateWorkScheduleRequest request)
    {
        var schedule = await _dbContext.DoctorWorkSchedules.FirstOrDefaultAsync(ws => ws.Id == scheduleId);
        if (schedule == null)
            throw new NotFoundException("Lịch làm việc không tồn tại.");

        if (request.StartTime >= request.EndTime)
            throw new ValidationException("Thời gian bắt đầu phải nhỏ hơn thời gian kết thúc.");

        schedule.WorkDate = request.WorkDate;
        schedule.StartTime = request.StartTime;
        schedule.EndTime = request.EndTime;

        await _dbContext.SaveChangesAsync();

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

    public async Task UpdateWorkScheduleStatusAsync(long scheduleId, UpdateWorkScheduleStatusRequest request)
    {
        var schedule = await _dbContext.DoctorWorkSchedules.FirstOrDefaultAsync(ws => ws.Id == scheduleId);
        if (schedule == null)
            throw new NotFoundException("Lịch làm việc không tồn tại.");

        if (!request.IsActive)
        {
            // Kiểm tra xem có lịch hẹn nào chưa xử lý (Pending/Confirmed) trong lịch làm việc này không.
            // "Không được ngừng lịch làm việc nếu còn lịch hẹn chưa được xử lý."
            // Một lịch hẹn liên kết với AppointmentSlot.
            var hasPendingAppointments = await _dbContext.Appointments
                .AnyAsync(a => a.AppointmentSlot.DoctorId == schedule.DoctorId
                            && a.AppointmentSlot.SlotDate == schedule.WorkDate
                            && a.AppointmentSlot.StartTime >= schedule.StartTime
                            && a.AppointmentSlot.EndTime <= schedule.EndTime
                            && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed 
                                || a.Status == AppointmentStatus.PendingReschedule || a.Status == AppointmentStatus.PendingCancellation));
            
            if (hasPendingAppointments)
                throw new BusinessException("INVALID_OPERATION", "Không được ngừng lịch làm việc vì còn lịch hẹn chưa xử lý.");
        }

        schedule.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync();
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

        if (schedule.StartTime >= schedule.EndTime)
            throw new BusinessException("VALIDATION_ERROR", "Thời gian bắt đầu phải nhỏ hơn thời gian kết thúc.");

        // Lấy danh sách lịch nghỉ Approved của bác sĩ
        var leaves = await _dbContext.DoctorLeaveRequests
            .Where(l => l.DoctorId == schedule.DoctorId && l.Status == DoctorLeaveRequestStatus.Approved)
            .ToListAsync();

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
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

                // Kiểm tra overlap với LeaveRequest
                bool isOverlappingLeave = leaves.Any(l => slotStartDateTime < l.EndDateTime && slotEndDateTime > l.StartDateTime);

                if (!isOverlappingLeave)
                {
                    // Tránh trùng lặp (DoctorId, SlotDate, StartTime)
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
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
