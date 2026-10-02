using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.DTOs;
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

        var isHeldByAppointment = await _dbContext.Appointments
            .AsNoTracking()
            .AnyAsync(a => a.AppointmentSlotId == slot.Id
                        && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status), cancellationToken);

        if (slot.IsBooked || isHeldByAppointment)
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

    public async Task<List<AvailableSlotDto>> GetAvailableSlotsAsync(BatchSlotAvailabilityRequest request, CancellationToken cancellationToken = default)
    {
        var fromDate = request.FromDate;
        var toDate = request.ToDate;
        if (toDate < fromDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var vnToday = _dateTimeProvider.VietnamToday;
        var vnTime = _dateTimeProvider.VietnamTime;

        // 1. Resolve Target Doctor IDs
        var targetDoctorIds = new List<long>();

        if (request.DoctorId.HasValue)
        {
            var docId = request.DoctorId.Value;
            var doctorExists = await _dbContext.Doctors
                .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
                .AnyAsync(x => x.d.Id == docId && x.d.IsActive && x.u.IsActive, cancellationToken);

            if (!doctorExists)
            {
                if (request.ThrowOnValidationFailure)
                    throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");
                return new List<AvailableSlotDto>();
            }

            if (request.SpecialtyId.HasValue)
            {
                var specialty = await _dbContext.Specialties
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == request.SpecialtyId.Value && s.IsActive, cancellationToken);

                if (specialty == null)
                    throw new BusinessException("SPECIALTY_NOT_AVAILABLE", "Chuyên khoa không hoạt động.");

                if (request.CheckAiEnabledSpecialty && !specialty.AiEnabled)
                    throw new BusinessException("SPECIALTY_AI_DISABLED", "Chuyên khoa chưa được kích hoạt hỗ trợ AI.");

                var hasSpecialty = await _dbContext.DoctorSpecialties
                    .AnyAsync(ds => ds.DoctorId == docId && ds.SpecialtyId == request.SpecialtyId.Value, cancellationToken);

                if (!hasSpecialty)
                {
                    if (request.ThrowOnValidationFailure)
                        throw new NotFoundException("Bác sĩ không thuộc chuyên khoa này.");
                    return new List<AvailableSlotDto>();
                }
            }

            targetDoctorIds.Add(docId);
        }
        else if (request.DoctorIds != null && request.DoctorIds.Count > 0)
        {
            var activeDocs = await _dbContext.Doctors
                .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
                .Where(x => request.DoctorIds.Contains(x.d.Id) && x.d.IsActive && x.u.IsActive)
                .Select(x => x.d.Id)
                .ToListAsync(cancellationToken);

            if (request.SpecialtyId.HasValue)
            {
                var specialty = await _dbContext.Specialties
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == request.SpecialtyId.Value && s.IsActive, cancellationToken);

                if (specialty == null)
                {
                    if (request.ThrowOnValidationFailure)
                        throw new BusinessException("SPECIALTY_NOT_AVAILABLE", "Chuyên khoa không hoạt động.");
                    return new List<AvailableSlotDto>();
                }

                if (request.CheckAiEnabledSpecialty && !specialty.AiEnabled)
                {
                    if (request.ThrowOnValidationFailure)
                        throw new BusinessException("SPECIALTY_AI_DISABLED", "Chuyên khoa chưa được kích hoạt hỗ trợ AI.");
                    return new List<AvailableSlotDto>();
                }

                var docIdsWithSpec = await _dbContext.DoctorSpecialties
                    .Where(ds => activeDocs.Contains(ds.DoctorId) && ds.SpecialtyId == request.SpecialtyId.Value)
                    .Select(ds => ds.DoctorId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                targetDoctorIds.AddRange(docIdsWithSpec);
            }
            else
            {
                targetDoctorIds.AddRange(activeDocs);
            }
        }
        else if (request.SpecialtyId.HasValue)
        {
            var specialty = await _dbContext.Specialties
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.SpecialtyId.Value && s.IsActive, cancellationToken);

            if (specialty == null)
            {
                if (request.ThrowOnValidationFailure)
                    throw new BusinessException("SPECIALTY_NOT_AVAILABLE", "Chuyên khoa không hoạt động.");
                return new List<AvailableSlotDto>();
            }

            if (request.CheckAiEnabledSpecialty && !specialty.AiEnabled)
            {
                if (request.ThrowOnValidationFailure)
                    throw new BusinessException("SPECIALTY_AI_DISABLED", "Chuyên khoa chưa được kích hoạt hỗ trợ AI.");
                return new List<AvailableSlotDto>();
            }

            var docIdsWithSpec = await _dbContext.DoctorSpecialties
                .Join(_dbContext.Doctors, ds => ds.DoctorId, d => d.Id, (ds, d) => new { ds, d })
                .Join(_dbContext.Users, x => x.d.UserId, u => u.Id, (x, u) => new { x.ds, x.d, u })
                .Where(x => x.ds.SpecialtyId == request.SpecialtyId.Value && x.d.IsActive && x.u.IsActive)
                .Select(x => x.d.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            targetDoctorIds.AddRange(docIdsWithSpec);
        }
        else
        {
            var allActive = await _dbContext.Doctors
                .Join(_dbContext.Users, d => d.UserId, u => u.Id, (d, u) => new { d, u })
                .Where(x => x.d.IsActive && x.u.IsActive)
                .Select(x => x.d.Id)
                .ToListAsync(cancellationToken);
            targetDoctorIds.AddRange(allActive);
        }

        if (targetDoctorIds.Count == 0)
        {
            return new List<AvailableSlotDto>();
        }

        // 2. Query Candidate Slots in bulk
        var slots = await _dbContext.AppointmentSlots
            .AsNoTracking()
            .Where(s => targetDoctorIds.Contains(s.DoctorId)
                        && !s.IsBooked
                        && s.SlotDate >= fromDate
                        && s.SlotDate <= toDate
                        && (s.SlotDate > vnToday || (s.SlotDate == vnToday && s.StartTime > vnTime)))
            .OrderBy(s => s.SlotDate)
            .ThenBy(s => s.StartTime)
            .ThenBy(s => s.DoctorId)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        if (slots.Count == 0)
        {
            return new List<AvailableSlotDto>();
        }

        // 3. Bulk Query Active Schedules
        var activeSchedules = await _dbContext.DoctorWorkSchedules
            .AsNoTracking()
            .Where(ws => targetDoctorIds.Contains(ws.DoctorId)
                      && ws.IsActive
                      && ws.WorkDate >= fromDate
                      && ws.WorkDate <= toDate)
            .ToListAsync(cancellationToken);

        // 4. Bulk Query Approved Leaves
        var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
        var toDateTime = toDate.ToDateTime(TimeOnly.MaxValue);
        var leaves = await _dbContext.DoctorLeaveRequests
            .AsNoTracking()
            .Where(l => targetDoctorIds.Contains(l.DoctorId)
                     && l.Status == DoctorLeaveRequestStatus.Approved
                     && l.StartDateTime <= toDateTime
                     && l.EndDateTime >= fromDateTime)
            .ToListAsync(cancellationToken);

        // 5. Bulk Query Holding Appointments (all 6 statuses)
        var holdingSlotIds = await _dbContext.Appointments
            .AsNoTracking()
            .Where(a => targetDoctorIds.Contains(a.DoctorId)
                     && a.AppointmentDate >= fromDate
                     && a.AppointmentDate <= toDate
                     && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
            .Select(a => a.AppointmentSlotId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var holdingSlotSet = new HashSet<long>(holdingSlotIds);

        // 6. Bulk Query Patient Overlapping Appointments (if patient provided)
        List<Appointment>? patientAppointments = null;
        if (request.PatientId.HasValue && request.PatientId.Value > 0)
        {
            patientAppointments = await _dbContext.Appointments
                .AsNoTracking()
                .Where(a => a.PatientId == request.PatientId.Value
                         && a.AppointmentDate >= fromDate
                         && a.AppointmentDate <= toDate
                         && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
                .ToListAsync(cancellationToken);
        }

        // 7. In-Memory Filter
        var validSlots = new List<AvailableSlotDto>();
        foreach (var s in slots)
        {
            // Sunday check
            if (s.SlotDate.DayOfWeek == DayOfWeek.Sunday) continue;

            // Holding appointment check
            if (holdingSlotSet.Contains(s.Id)) continue;

            // Duration 30 mins
            var slotStart = s.SlotDate.ToDateTime(s.StartTime);
            var slotEnd = s.SlotDate.ToDateTime(s.EndTime);
            if ((slotEnd - slotStart).TotalMinutes != 30) continue;

            // Must be within active work schedule
            var inSchedule = activeSchedules.Any(ws =>
                ws.DoctorId == s.DoctorId &&
                ws.WorkDate == s.SlotDate &&
                ws.StartTime <= s.StartTime &&
                ws.EndTime >= s.EndTime);
            if (!inSchedule) continue;

            // Must not overlap with approved leave
            var onLeave = leaves.Any(l =>
                l.DoctorId == s.DoctorId &&
                slotStart < l.EndDateTime &&
                slotEnd > l.StartDateTime);
            if (onLeave) continue;

            // Must not overlap with patient appointment
            if (patientAppointments != null)
            {
                var patientOverlap = patientAppointments.Any(pa =>
                    pa.AppointmentDate == s.SlotDate &&
                    pa.StartTime < s.EndTime &&
                    pa.EndTime > s.StartTime);
                if (patientOverlap) continue;
            }

            // Time preference filter if specified
            if (!string.IsNullOrWhiteSpace(request.TimePreference))
            {
                var pref = request.TimePreference.ToLowerInvariant();
                if (pref.Contains("sáng") && s.StartTime >= new TimeOnly(12, 0, 0)) continue;
                if (pref.Contains("chiều") && s.StartTime < new TimeOnly(12, 0, 0)) continue;
            }

            validSlots.Add(new AvailableSlotDto
            {
                SlotId = s.Id,
                DoctorId = s.DoctorId,
                SlotDate = s.SlotDate,
                StartTime = s.StartTime,
                EndTime = s.EndTime
            });

            if (request.Limit.HasValue && request.Limit.Value > 0 && validSlots.Count >= request.Limit.Value)
            {
                break;
            }
        }

        return validSlots;
    }
}
