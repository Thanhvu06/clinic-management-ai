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

using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Appointments;

public class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AppointmentService(AppDbContext dbContext, ICurrentUserService currentUserService, IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest request)
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var normalizedReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (normalizedReason != null && (normalizedReason.Length < 10 || normalizedReason.Length > 500))
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

        var doctorUser = await _dbContext.Users.FindAsync(doctor.UserId);
        var doctorName = doctorUser?.FullName ?? "Bác sĩ";

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

        var vnToday = _dateTimeProvider.VietnamToday;
        var vnTime = _dateTimeProvider.VietnamTime;

        if (slot.SlotDate < vnToday || (slot.SlotDate == vnToday && slot.StartTime <= vnTime))
            throw new BusinessException("VALIDATION_ERROR", "Không thể đặt lịch trong quá khứ.");

        if (slot.SlotDate.DayOfWeek == DayOfWeek.Sunday)
            throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Phòng khám không mở lịch khám vào Chủ nhật.");

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
                throw new ConflictException("SLOT_ALREADY_BOOKED", "Slot đã được đặt hoặc không khả dụng.");

            // 12. Check overlap for Patient using the canonical slot-holding policy
            var overlappingAppointment = await _dbContext.Appointments
                .Where(a => a.PatientId == patient.Id 
                         && a.AppointmentDate == slot.SlotDate
                         && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status)
                         && a.StartTime < slot.EndTime 
                         && a.EndTime > slot.StartTime)
                .FirstOrDefaultAsync();

            if (overlappingAppointment != null)
                throw new BusinessException("PATIENT_TIME_CONFLICT", "Bạn đã có lịch khám khác trùng hoặc giao lấp thời gian với slot này.");

            // Create Appointment
            var appointmentCode = $"APT-{_dateTimeProvider.VietnamNow:yyMMdd}-{Guid.NewGuid():N}"[..18].ToUpper();

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
                Reason = normalizedReason,
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

            // Create in-app notification for patient
            _dbContext.Notifications.Add(new Notification
            {
                UserId = currentUserId.Value,
                Type = NotificationType.Appointment,
                Title = "Đặt lịch khám thành công",
                Message = $"Lịch khám #{appointment.AppointmentCode} ngày {appointment.AppointmentDate:dd/MM/yyyy} lúc {appointment.StartTime:HH\\:mm} đã được tiếp nhận.",
                Route = "/patient/appointments",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"appt_booked_pat_{appointment.Id}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });

            // Notify active receptionists
            var recRole = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == ClinicManagement.Application.Common.Constants.RoleNames.Receptionist);
            if (recRole != null)
            {
                var recUserIds = await _dbContext.UserRoles
                    .Where(ur => ur.RoleId == recRole.Id)
                    .Select(ur => ur.UserId)
                    .ToListAsync();

                var activeRecUserIds = await _dbContext.Users
                    .Where(u => recUserIds.Contains(u.Id) && u.IsActive)
                    .Select(u => u.Id)
                    .ToListAsync();

                foreach (var recUserId in activeRecUserIds)
                {
                    _dbContext.Notifications.Add(new Notification
                    {
                        UserId = recUserId,
                        Type = NotificationType.Appointment,
                        Title = "Lịch khám mới chờ xử lý",
                        Message = $"Bệnh nhân đã đặt lịch khám #{appointment.AppointmentCode} ngày {appointment.AppointmentDate:dd/MM/yyyy}.",
                        Route = "/reception/appointments",
                        RelatedEntityType = "Appointment",
                        RelatedEntityId = appointment.Id.ToString(),
                        DedupeKey = $"appt_booked_rec_{appointment.Id}_{recUserId}",
                        IsRead = false,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            await _dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            return new AppointmentDto
            {
                Id = appointment.Id,
                AppointmentCode = appointment.AppointmentCode,
                PatientId = appointment.PatientId,
                DoctorId = appointment.DoctorId,
                DoctorName = doctorName,
                SpecialtyId = appointment.SpecialtyId,
                SpecialtyName = specialty.Name,
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
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

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
        var items = await (from a in query
                           join d in _dbContext.Doctors on a.DoctorId equals d.Id
                           join u in _dbContext.Users on d.UserId equals u.Id
                           join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                           select new AppointmentDto
                           {
                               Id = a.Id,
                               AppointmentCode = a.AppointmentCode,
                               PatientId = a.PatientId,
                               DoctorId = a.DoctorId,
                               DoctorName = u.FullName,
                               SpecialtyId = a.SpecialtyId,
                               SpecialtyName = s.Name,
                               AppointmentSlotId = a.AppointmentSlotId,
                               AppointmentDate = a.AppointmentDate,
                               StartTime = a.StartTime,
                               EndTime = a.EndTime,
                               Reason = a.Reason,
                               Status = a.Status.ToString()
                           })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        var appointment = await (from a in _dbContext.Appointments.AsNoTracking()
                                 join d in _dbContext.Doctors on a.DoctorId equals d.Id
                                 join u in _dbContext.Users on d.UserId equals u.Id
                                 join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                                 where a.Id == appointmentId && a.PatientId == patient.Id
                                 select new AppointmentDto
                                 {
                                     Id = a.Id,
                                     AppointmentCode = a.AppointmentCode,
                                     PatientId = a.PatientId,
                                     DoctorId = a.DoctorId,
                                     DoctorName = u.FullName,
                                     SpecialtyId = a.SpecialtyId,
                                     SpecialtyName = s.Name,
                                     AppointmentSlotId = a.AppointmentSlotId,
                                     AppointmentDate = a.AppointmentDate,
                                     StartTime = a.StartTime,
                                     EndTime = a.EndTime,
                                     Reason = a.Reason,
                                     Status = a.Status.ToString()
                                 }).FirstOrDefaultAsync();

        if (appointment == null)
            throw new NotFoundException("Lịch hẹn không tồn tại hoặc bạn không có quyền xem.");

        return appointment;
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

    public async Task<List<AppointmentLookupDto>> LookupAppointmentsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 4)
            return new List<AppointmentLookupDto>();

        var cleanQuery = query.Trim();

        var queryable = from a in _dbContext.Appointments
                        join p in _dbContext.Patients on a.PatientId equals p.Id
                        join pu in _dbContext.Users on p.UserId equals pu.Id
                        join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                        join d in _dbContext.Doctors on a.DoctorId equals d.Id
                        join du in _dbContext.Users on d.UserId equals du.Id
                        where a.AppointmentCode == cleanQuery.ToUpper() || pu.PhoneNumber == cleanQuery
                        orderby a.AppointmentDate descending, a.StartTime descending
                        select new
                        {
                            a.AppointmentCode,
                            a.AppointmentDate,
                            a.StartTime,
                            a.EndTime,
                            SpecialtyName = s.Name,
                            DoctorName = (string.IsNullOrWhiteSpace(d.AcademicTitle) ? "" : d.AcademicTitle + ". ") + du.FullName,
                            Status = a.Status.ToString(),
                            FullName = pu.FullName,
                            PhoneNumber = pu.PhoneNumber
                        };

        var items = await queryable.Take(10).ToListAsync();

        return items.Select(x => new AppointmentLookupDto
        {
            AppointmentCode = x.AppointmentCode,
            AppointmentDate = x.AppointmentDate,
            StartTime = x.StartTime,
            EndTime = x.EndTime,
            SpecialtyName = x.SpecialtyName,
            DoctorName = x.DoctorName,
            Status = x.Status,
            MaskedPatientName = MaskName(x.FullName),
            MaskedPhoneNumber = MaskPhone(x.PhoneNumber)
        }).ToList();
    }

    private static string MaskName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "***";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return name.Length > 2 ? name[0] + "***" + name[^1] : "***";
        return parts[0] + " " + string.Join(" ", parts.Skip(1).Select(p => p[0] + "***"));
    }

    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 6) return "***";
        return phone.Substring(0, 3) + "****" + phone.Substring(phone.Length - 3);
    }
}
