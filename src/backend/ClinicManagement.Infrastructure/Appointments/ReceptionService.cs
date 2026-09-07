using System;
using System.Collections.Generic;
using ClinicManagement.Application.Appointments.DTOs;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Reception;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Appointments;

public class ReceptionService : IReceptionService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ReceptionService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    private Guid GetUserId()
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");
        return currentUserId.Value;
    }

    public async Task<PagedResult<ReceptionAppointmentDto>> GetAppointmentsAsync(string? status, string? search, int page, int pageSize)
    {
        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals pu.Id
                    join d in _dbContext.Doctors on a.DoctorId equals d.Id
                    join du in _dbContext.Users on d.UserId equals du.Id
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    select new
                    {
                        Appointment = a,
                        PatientName = pu.FullName,
                        PatientPhone = pu.PhoneNumber,
                        DoctorName = du.FullName,
                        SpecialtyName = s.Name
                    };

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Appointment.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(x => x.Appointment.AppointmentCode.Contains(search) 
                                  || x.PatientName.Contains(search) 
                                  || x.PatientPhone.Contains(search));
        }

        query = query.OrderBy(x => x.Appointment.AppointmentDate).ThenBy(x => x.Appointment.StartTime);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var resultItems = items.Select(x => MapToDto(x.Appointment, x.PatientName, x.PatientPhone, x.DoctorName, x.SpecialtyName)).ToList();

        return new PagedResult<ReceptionAppointmentDto>(resultItems, totalItems, page, pageSize);
    }

    public async Task<ReceptionAppointmentDto> GetAppointmentByIdAsync(long appointmentId)
    {
        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals pu.Id
                    join d in _dbContext.Doctors on a.DoctorId equals d.Id
                    join du in _dbContext.Users on d.UserId equals du.Id
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    where a.Id == appointmentId
                    select new
                    {
                        Appointment = a,
                        PatientName = pu.FullName,
                        PatientPhone = pu.PhoneNumber,
                        DoctorName = du.FullName,
                        SpecialtyName = s.Name
                    };

        var item = await query.FirstOrDefaultAsync();

        if (item == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

        return MapToDto(item.Appointment, item.PatientName, item.PatientPhone, item.DoctorName, item.SpecialtyName);
    }

    public async Task<List<AppointmentHistoryDto>> GetAppointmentHistoryAsync(long appointmentId)
    {
        var appointmentExists = await _dbContext.Appointments
            .AnyAsync(a => a.Id == appointmentId);

        if (!appointmentExists)
            throw new NotFoundException("Lịch hẹn không tồn tại.");

        return await _dbContext.AppointmentHistories
            .AsNoTracking()
            .Where(h => h.AppointmentId == appointmentId)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new AppointmentHistoryDto
            {
                Id = h.Id,
                Action = h.Action.ToString(),
                OldStatus = h.OldStatus != null ? h.OldStatus.ToString() : null,
                NewStatus = h.NewStatus.ToString(),
                Note = h.Note,
                CreatedAt = h.CreatedAt
            })
            .ToListAsync();
    }

    public async Task ConfirmAppointmentAsync(long appointmentId)
    {
        var userId = GetUserId();

        var appointment = await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

        if (appointment.Status != AppointmentStatus.Pending)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể xác nhận lịch hẹn ở trạng thái Pending.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.Confirmed;

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.Confirmed,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.Confirmed,
            Note = "Lễ tân xác nhận lịch",
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        // Notify patient
        var patientUserId = await _dbContext.Patients
            .Where(p => p.Id == appointment.PatientId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync();

        if (patientUserId != Guid.Empty)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = patientUserId,
                Type = NotificationType.Appointment,
                Title = "Lịch khám đã được xác nhận",
                Message = $"Lịch khám #{appointment.AppointmentCode} ngày {appointment.AppointmentDate:dd/MM/yyyy} lúc {appointment.StartTime:HH\\:mm} đã được tiếp nhận và xác nhận.",
                Route = "/patient/appointments",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"appt_confirmed_pat_{appointment.Id}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Notify doctor
        var doctorUserId = await _dbContext.Doctors
            .Where(d => d.Id == appointment.DoctorId)
            .Select(d => d.UserId)
            .FirstOrDefaultAsync();

        if (doctorUserId != Guid.Empty)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = doctorUserId,
                Type = NotificationType.Appointment,
                Title = "Lịch khám đã được xác nhận",
                Message = $"Lịch khám #{appointment.AppointmentCode} ngày {appointment.AppointmentDate:dd/MM/yyyy} đã được xác nhận.",
                Route = $"/doctor/appointments/{appointment.Id}",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"appt_confirmed_doc_{appointment.Id}_{doctorUserId}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task CheckInAppointmentAsync(long appointmentId)
    {
        var userId = GetUserId();

        var appointment = await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

        if (appointment.Status != AppointmentStatus.Confirmed)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể check-in lịch hẹn ở trạng thái Confirmed.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.CheckedIn;

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.CheckedIn,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.CheckedIn,
            Note = "Bệnh nhân đã có mặt tại phòng khám và check-in vào hàng đợi",
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        // Notify doctor
        var doctorUserId = await _dbContext.Doctors
            .Where(d => d.Id == appointment.DoctorId)
            .Select(d => d.UserId)
            .FirstOrDefaultAsync();

        if (doctorUserId != Guid.Empty)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = doctorUserId,
                Type = NotificationType.Appointment,
                Title = "Bệnh nhân đã đến phòng khám",
                Message = $"Bệnh nhân cho lịch khám #{appointment.AppointmentCode} đã có mặt tại phòng chờ.",
                Route = $"/doctor/appointments/{appointment.Id}",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"appt_checkin_doc_{appointment.Id}_{doctorUserId}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<ReceptionStatsDto> GetStatsAsync()
    {
        var today = _dateTimeProvider.VietnamToday;
        var appointmentsToday = await _dbContext.Appointments
            .Where(a => a.AppointmentDate == today)
            .ToListAsync();

        var pendingChangeRequests = await _dbContext.AppointmentChangeRequests
            .CountAsync(c => c.Status == AppointmentChangeRequestStatus.Pending);

        return new ReceptionStatsDto
        {
            AppointmentsToday = appointmentsToday.Count,
            PendingAppointmentsToday = appointmentsToday.Count(a => a.Status == AppointmentStatus.Pending),
            ConfirmedAppointmentsToday = appointmentsToday.Count(a => a.Status == AppointmentStatus.Confirmed),
            CompletedAppointmentsToday = appointmentsToday.Count(a => a.Status == AppointmentStatus.Completed),
            PendingChangeRequests = pendingChangeRequests
        };
    }

    private static ReceptionAppointmentDto MapToDto(Appointment a, string patientName, string patientPhone, string doctorName, string specialtyName) => new()
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
        Status = a.Status.ToString(),
        PatientName = patientName ?? string.Empty,
        PatientPhone = patientPhone ?? string.Empty,
        DoctorName = doctorName ?? string.Empty,
        SpecialtyName = specialtyName ?? string.Empty
    };
}
