using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace ClinicManagement.Infrastructure.Appointments;

public class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public AppointmentService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest request)
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");

        if (request.Reason != null && (request.Reason.Length < 10 || request.Reason.Length > 500))
            throw new BusinessException("VALIDATION_ERROR", "Lý do khám phải từ 10 đến 500 ký tự.");

        // 1 & 2 & 3. Validate Patient
        var patient = await _dbContext.Patients
            .FirstOrDefaultAsync(p => p.UserId == currentUserId.Value);
            
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var user = await _dbContext.Users.FindAsync(currentUserId.Value);
        if (user == null || !user.IsActive)
            throw new NotFoundException("Tài khoản không tồn tại hoặc đã ngừng hoạt động.");

        if (patient.Gender == null || patient.DateOfBirth == null)
            throw new BusinessException("VALIDATION_ERROR", "Vui lòng cập nhật đầy đủ Giới tính và Ngày sinh trước khi đặt lịch.");

        // 4. Validate Doctor
        var doctor = await _dbContext.Doctors.FirstOrDefaultAsync(d => d.Id == request.DoctorId && d.IsActive);
        if (doctor == null)
            throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        // 5 & 6. Validate Specialty
        var specialty = await _dbContext.Specialties.FirstOrDefaultAsync(s => s.Id == request.SpecialtyId && s.IsActive);
        if (specialty == null)
            throw new BusinessException("SPECIALTY_NOT_AVAILABLE", "Chuyên khoa không tồn tại hoặc đã ngừng hoạt động.");

        var hasSpecialty = await _dbContext.DoctorSpecialties
            .AnyAsync(ds => ds.DoctorId == request.DoctorId && ds.SpecialtyId == request.SpecialtyId);
        if (!hasSpecialty)
            throw new BusinessException("VALIDATION_ERROR", "Bác sĩ không thuộc chuyên khoa này.");

        // 7, 8, 9, 10. Validate Slot basic (time, existence)
        var slot = await _dbContext.AppointmentSlots
            .FirstOrDefaultAsync(s => s.Id == request.AppointmentSlotId && s.DoctorId == request.DoctorId);
            
        if (slot == null)
            throw new NotFoundException("Slot không tồn tại.");

        var slotStart = slot.SlotDate.ToDateTime(slot.StartTime);
        var slotEnd = slot.SlotDate.ToDateTime(slot.EndTime);

        if (slotStart <= DateTime.UtcNow)
            throw new BusinessException("VALIDATION_ERROR", "Không thể đặt lịch trong quá khứ.");

        if ((slotEnd - slotStart).TotalMinutes != 30)
            throw new BusinessException("VALIDATION_ERROR", "Slot khám phải có thời lượng đúng 30 phút.");

        var hasWorkSchedule = await _dbContext.DoctorWorkSchedules
            .AnyAsync(ws => ws.DoctorId == request.DoctorId 
                         && ws.WorkDate == slot.SlotDate 
                         && ws.StartTime <= slot.StartTime 
                         && ws.EndTime >= slot.EndTime 
                         && ws.IsActive);
        if (!hasWorkSchedule)
            throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Slot không nằm trong lịch làm việc hoạt động của bác sĩ.");

        var inLeave = await _dbContext.DoctorLeaveRequests
            .AnyAsync(l => l.DoctorId == request.DoctorId 
                        && l.Status == DoctorLeaveRequestStatus.Approved 
                        && slotStart < l.EndDateTime 
                        && slotEnd > l.StartDateTime);
        if (inLeave)
            throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ đang trong lịch nghỉ đã được duyệt.");

        // TRANSACTION: Serializable to prevent overlapping inserts
        using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // 11. Lock and update slot atomically
            var affectedRows = await _dbContext.AppointmentSlots
                .Where(s => s.Id == request.AppointmentSlotId && !s.IsBooked)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, true));

            if (affectedRows == 0)
                throw new BusinessException("SLOT_ALREADY_BOOKED", "Slot đã được đặt hoặc không khả dụng.");

            // 12. Check overlap for Patient
            var activeStatuses = new[] 
            { 
                AppointmentStatus.Pending, 
                AppointmentStatus.Confirmed, 
                AppointmentStatus.PendingReschedule, 
                AppointmentStatus.PendingCancellation 
            };

            var overlappingAppointment = await _dbContext.Appointments
                .Where(a => a.PatientId == patient.Id 
                         && a.AppointmentDate == slot.SlotDate
                         && activeStatuses.Contains(a.Status)
                         && a.StartTime < slot.EndTime 
                         && a.EndTime > slot.StartTime)
                .FirstOrDefaultAsync();

            if (overlappingAppointment != null)
                throw new BusinessException("PATIENT_TIME_CONFLICT", "Bạn đã có lịch khám khác trùng hoặc giao lấp thời gian với slot này.");

            // Create Appointment
            var appointmentCode = "APT-" + DateTime.UtcNow.ToString("yyMMdd") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();

            var appointment = new Appointment
            {
                AppointmentCode = appointmentCode,
                PatientId = patient.Id,
                DoctorId = request.DoctorId,
                SpecialtyId = request.SpecialtyId,
                AppointmentSlotId = slot.Id,
                AppointmentDate = slot.SlotDate,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Reason = request.Reason,
                Status = AppointmentStatus.Pending
            };

            _dbContext.Appointments.Add(appointment);
            await _dbContext.SaveChangesAsync(); // Save to generate ID

            // Create History
            var history = new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.Created,
                OldStatus = null,
                NewStatus = AppointmentStatus.Pending,
                PerformedByUserId = currentUserId.Value,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AppointmentHistories.Add(history);
            await _dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            return new AppointmentDto
            {
                Id = appointment.Id,
                AppointmentCode = appointment.AppointmentCode,
                PatientId = appointment.PatientId,
                DoctorId = appointment.DoctorId,
                SpecialtyId = appointment.SpecialtyId,
                AppointmentSlotId = appointment.AppointmentSlotId,
                AppointmentDate = appointment.AppointmentDate,
                StartTime = appointment.StartTime,
                EndTime = appointment.EndTime,
                Reason = appointment.Reason,
                Status = appointment.Status.ToString()
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<PagedResult<AppointmentDto>> GetPatientAppointmentsAsync(string? status, int page, int pageSize)
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == currentUserId.Value);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var query = _dbContext.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patient.Id);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(a => a.Status == parsedStatus);
        }

        query = query.OrderByDescending(a => a.AppointmentDate).ThenByDescending(a => a.StartTime);

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AppointmentDto
            {
                Id = a.Id,
                AppointmentCode = a.AppointmentCode,
                PatientId = a.PatientId,
                DoctorId = a.DoctorId,
                SpecialtyId = a.SpecialtyId,
                AppointmentSlotId = a.AppointmentSlotId,
                AppointmentDate = a.AppointmentDate,
                StartTime = a.StartTime,
                EndTime = a.EndTime,
                Reason = a.Reason,
                Status = a.Status.ToString()
            })
            .ToListAsync();

        return new PagedResult<AppointmentDto>(items, totalItems, page, pageSize);
    }

    public async Task<AppointmentDto> GetPatientAppointmentByIdAsync(long appointmentId)
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == currentUserId.Value);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientId == patient.Id);

        if (appointment == null)
            throw new NotFoundException("Lịch hẹn không tồn tại hoặc bạn không có quyền xem.");

        return new AppointmentDto
        {
            Id = appointment.Id,
            AppointmentCode = appointment.AppointmentCode,
            PatientId = appointment.PatientId,
            DoctorId = appointment.DoctorId,
            SpecialtyId = appointment.SpecialtyId,
            AppointmentSlotId = appointment.AppointmentSlotId,
            AppointmentDate = appointment.AppointmentDate,
            StartTime = appointment.StartTime,
            EndTime = appointment.EndTime,
            Reason = appointment.Reason,
            Status = appointment.Status.ToString()
        };
    }

    public async Task<List<AppointmentHistoryDto>> GetAppointmentHistoryAsync(long appointmentId)
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == currentUserId.Value);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var appointmentExists = await _dbContext.Appointments
            .AnyAsync(a => a.Id == appointmentId && a.PatientId == patient.Id);

        if (!appointmentExists)
            throw new NotFoundException("Lịch hẹn không tồn tại hoặc bạn không có quyền xem.");

        return await _dbContext.AppointmentHistories
            .AsNoTracking()
            .Where(h => h.AppointmentId == appointmentId)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new AppointmentHistoryDto
            {
                Id = h.Id,
                AppointmentId = h.AppointmentId,
                Action = h.Action.ToString(),
                OldStatus = h.OldStatus.HasValue ? h.OldStatus.Value.ToString() : null,
                NewStatus = h.NewStatus.HasValue ? h.NewStatus.Value.ToString() : null,
                Note = h.Note,
                PerformedByUserId = h.PerformedByUserId,
                CreatedAt = h.CreatedAt
            })
            .ToListAsync();
    }
}
