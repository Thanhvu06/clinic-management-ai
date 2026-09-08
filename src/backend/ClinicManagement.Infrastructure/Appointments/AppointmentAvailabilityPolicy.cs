using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Appointments;

public class AppointmentAvailabilityPolicy : IAppointmentAvailabilityPolicy
{
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AppointmentAvailabilityPolicy(AppDbContext dbContext, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<SlotAvailabilityResult> EvaluateSlotAvailabilityAsync(SlotAvailabilityRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SlotId <= 0)
        {
            return SlotAvailabilityResult.Failure("INVALID_SLOT_ID", "Mã slot không hợp lệ.");
        }

        // 1. Slot existence and status
        var slot = await _dbContext.AppointmentSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SlotId, cancellationToken);

        if (slot == null)
        {
            return SlotAvailabilityResult.Failure("SLOT_NOT_FOUND", "Khung giờ khám không tồn tại.");
        }

        if (slot.IsBooked)
        {
            return SlotAvailabilityResult.Failure("SLOT_ALREADY_BOOKED", "Khung giờ khám đã có người đặt hoặc không khả dụng.");
        }

        // 2. Doctor matching
        long targetDoctorId = request.DoctorId ?? slot.DoctorId;
        if (slot.DoctorId != targetDoctorId)
        {
            return SlotAvailabilityResult.Failure("DOCTOR_MISMATCH", "Khung giờ khám không thuộc về bác sĩ được chọn.");
        }

        // 3. Doctor and user active status
        var doctor = await _dbContext.Doctors
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == targetDoctorId && d.IsActive, cancellationToken);

        if (doctor == null)
        {
            return SlotAvailabilityResult.Failure("DOCTOR_NOT_AVAILABLE", "Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");
        }

        var doctorUser = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == doctor.UserId && u.IsActive, cancellationToken);

        if (doctorUser == null)
        {
            return SlotAvailabilityResult.Failure("DOCTOR_NOT_AVAILABLE", "Tài khoản bác sĩ đã ngừng hoạt động.");
        }

        // 4. Specialty validation
        long? targetSpecialtyId = request.SpecialtyId;
        if (!targetSpecialtyId.HasValue)
        {
            var primaryDocSpec = await _dbContext.DoctorSpecialties
                .AsNoTracking()
                .FirstOrDefaultAsync(ds => ds.DoctorId == targetDoctorId, cancellationToken);

            if (primaryDocSpec == null)
            {
                return SlotAvailabilityResult.Failure("SPECIALTY_MISMATCH", "Bác sĩ chưa được phân công chuyên khoa nào.");
            }
            targetSpecialtyId = primaryDocSpec.SpecialtyId;
        }

        var specialty = await _dbContext.Specialties
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == targetSpecialtyId.Value && s.IsActive, cancellationToken);

        if (specialty == null)
        {
            return SlotAvailabilityResult.Failure("SPECIALTY_NOT_AVAILABLE", "Chuyên khoa không tồn tại hoặc đã ngừng hoạt động.");
        }

        if (request.CheckAiEnabledSpecialty && !specialty.AiEnabled)
        {
            return SlotAvailabilityResult.Failure("SPECIALTY_AI_DISABLED", "Chuyên khoa chưa được kích hoạt hỗ trợ AI.");
        }

        var hasSpecialty = await _dbContext.DoctorSpecialties
            .AsNoTracking()
            .AnyAsync(ds => ds.DoctorId == targetDoctorId && ds.SpecialtyId == targetSpecialtyId.Value, cancellationToken);

        if (!hasSpecialty)
        {
            return SlotAvailabilityResult.Failure("SPECIALTY_MISMATCH", "Bác sĩ không thuộc chuyên khoa được chọn.");
        }

        // 5. Sunday and past time guards
        if (slot.SlotDate.DayOfWeek == DayOfWeek.Sunday)
        {
            return SlotAvailabilityResult.Failure("SUNDAY_CLOSED", "Phòng khám không mở lịch khám vào Chủ nhật.");
        }

        var vnToday = _dateTimeProvider.VietnamToday;
        var vnTime = _dateTimeProvider.VietnamTime;

        if (slot.SlotDate < vnToday || (slot.SlotDate == vnToday && slot.StartTime <= vnTime))
        {
            return SlotAvailabilityResult.Failure("PAST_TIME", "Không thể đặt lịch trong quá khứ.");
        }

        var slotStart = slot.SlotDate.ToDateTime(slot.StartTime);
        var slotEnd = slot.SlotDate.ToDateTime(slot.EndTime);

        if ((slotEnd - slotStart).TotalMinutes != 30)
        {
            return SlotAvailabilityResult.Failure("INVALID_DURATION", "Slot khám phải có thời lượng đúng 30 phút.");
        }

        // 6. Doctor active work schedule
        var hasWorkSchedule = await _dbContext.DoctorWorkSchedules
            .AsNoTracking()
            .AnyAsync(ws => ws.DoctorId == targetDoctorId
                         && ws.WorkDate == slot.SlotDate
                         && ws.StartTime <= slot.StartTime
                         && ws.EndTime >= slot.EndTime
                         && ws.IsActive, cancellationToken);

        if (!hasWorkSchedule)
        {
            return SlotAvailabilityResult.Failure("DOCTOR_NOT_SCHEDULED", "Slot không nằm trong lịch làm việc hoạt động của bác sĩ.");
        }

        // 7. Approved leave check
        var inLeave = await _dbContext.DoctorLeaveRequests
            .AsNoTracking()
            .AnyAsync(l => l.DoctorId == targetDoctorId
                        && l.Status == DoctorLeaveRequestStatus.Approved
                        && slotStart < l.EndDateTime
                        && slotEnd > l.StartDateTime, cancellationToken);

        if (inLeave)
        {
            return SlotAvailabilityResult.Failure("DOCTOR_ON_LEAVE", "Bác sĩ đang trong lịch nghỉ đã được duyệt.");
        }

        // 8. Patient overlapping appointment (if patient specified)
        if (request.PatientId.HasValue && request.PatientId.Value > 0)
        {
            var hasOverlap = await _dbContext.Appointments
                .AsNoTracking()
                .AnyAsync(a => a.PatientId == request.PatientId.Value
                            && a.AppointmentDate == slot.SlotDate
                            && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status)
                            && a.StartTime < slot.EndTime
                            && a.EndTime > slot.StartTime, cancellationToken);

            if (hasOverlap)
            {
                return SlotAvailabilityResult.Failure("PATIENT_TIME_CONFLICT", "Bạn đã có lịch khám khác trùng hoặc giao lấp thời gian với slot này.");
            }
        }

        return SlotAvailabilityResult.Success(
            slot.Id,
            targetDoctorId,
            doctorUser.FullName,
            doctor.AcademicTitle,
            specialty.Id,
            specialty.Name,
            slot.SlotDate,
            slot.StartTime,
            slot.EndTime
        );
    }
}
