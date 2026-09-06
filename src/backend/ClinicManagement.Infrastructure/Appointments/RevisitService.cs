using System;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Appointments.DTOs.Revisit;
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

public class RevisitService : IRevisitService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RevisitService(
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

    private async Task<Patient> GetCurrentPatientAsync()
    {
        var userId = GetUserId();
        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null) throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");
        return patient;
    }

    public async Task<PagedResult<RevisitRequestDto>> GetMyRevisitRequestsAsync(string? status, int page, int pageSize)
    {
        var patient = await GetCurrentPatientAsync();

        var query = from r in _dbContext.RevisitRequests
                    join d in _dbContext.Doctors on r.DoctorId equals d.Id
                    join du in _dbContext.Users on d.UserId equals du.Id
                    join a in _dbContext.Appointments on r.AppointmentId equals a.Id
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    where r.PatientId == patient.Id
                    select new
                    {
                        Request = r,
                        DoctorName = du.FullName,
                        SpecialtyId = s.Id,
                        SpecialtyName = s.Name
                    };

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RevisitRequestStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Request.Status == parsedStatus);
        }

        query = query.OrderByDescending(x => x.Request.SuggestedDate);

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var resultItems = items.Select(x => MapToDto(x.Request, x.DoctorName, x.SpecialtyId, x.SpecialtyName)).ToList();

        return new PagedResult<RevisitRequestDto>(resultItems, totalItems, page, pageSize);
    }

    public async Task<RevisitRequestDto> GetRevisitRequestByIdAsync(long id)
    {
        var patient = await GetCurrentPatientAsync();

        var query = from r in _dbContext.RevisitRequests
                    join d in _dbContext.Doctors on r.DoctorId equals d.Id
                    join du in _dbContext.Users on d.UserId equals du.Id
                    join a in _dbContext.Appointments on r.AppointmentId equals a.Id
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    where r.Id == id && r.PatientId == patient.Id
                    select new
                    {
                        Request = r,
                        DoctorName = du.FullName,
                        SpecialtyId = s.Id,
                        SpecialtyName = s.Name
                    };

        var item = await query.FirstOrDefaultAsync();
        if (item == null) throw new NotFoundException("Đề xuất tái khám không tồn tại.");

        return MapToDto(item.Request, item.DoctorName, item.SpecialtyId, item.SpecialtyName);
    }

    public async Task<AppointmentDto> AcceptRevisitRequestAsync(long id, AcceptRevisitRequestDto request)
    {
        var patient = await GetCurrentPatientAsync();
        var userId = GetUserId();
        var normalizedReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Tái khám theo đề xuất của bác sĩ"
            : request.Reason.Trim();

        if (normalizedReason.Length is < 10 or > 500)
            throw new BusinessException("VALIDATION_ERROR", "Lý do khám phải từ 10 đến 500 ký tự.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var revisitReq = await _dbContext.RevisitRequests
                .Include(r => r.OriginalAppointment)
                .FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.Id);

            if (revisitReq == null)
                throw new NotFoundException("Đề xuất tái khám không tồn tại.");

            if (revisitReq.Status != RevisitRequestStatus.PendingPatientResponse)
                throw new BusinessException("INVALID_STATE", "Chỉ có thể chấp nhận đề xuất đang chờ phản hồi.");

            var targetSlot = await _dbContext.AppointmentSlots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.TargetSlotId);

            if (targetSlot == null)
                throw new NotFoundException("Khung giờ khám không tồn tại.");

            if (targetSlot.DoctorId != revisitReq.DoctorId)
                throw new BusinessException("INVALID_TARGET", "Khung giờ phải thuộc về bác sĩ đề xuất tái khám.");

            if ((targetSlot.SlotDate.ToDateTime(targetSlot.EndTime) -
                 targetSlot.SlotDate.ToDateTime(targetSlot.StartTime)).TotalMinutes != 30)
                throw new BusinessException("INVALID_TARGET", "Khung giờ tái khám phải có thời lượng đúng 30 phút.");

            if (targetSlot.SlotDate.DayOfWeek == DayOfWeek.Sunday)
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Phòng khám không mở lịch khám vào Chủ nhật.");

            var vietnamToday = _dateTimeProvider.VietnamToday;
            if (targetSlot.SlotDate < vietnamToday ||
                (targetSlot.SlotDate == vietnamToday && targetSlot.StartTime <= _dateTimeProvider.VietnamTime))
                throw new BusinessException("INVALID_TARGET", "Khung giờ tái khám phải ở trong tương lai.");

            var doctor = await _dbContext.Doctors.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == targetSlot.DoctorId && d.IsActive);
            if (doctor == null)
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

            var specialtyId = revisitReq.OriginalAppointment.SpecialtyId;
            var specialty = await _dbContext.Specialties.AsNoTracking()
                .FirstOrDefaultAsync(sp => sp.Id == specialtyId && sp.IsActive);
            if (specialty == null)
                throw new BusinessException("SPECIALTY_NOT_AVAILABLE", "Chuyên khoa không tồn tại hoặc đã ngừng hoạt động.");

            var doctorHasSpecialty = await _dbContext.DoctorSpecialties
                .AnyAsync(ds => ds.DoctorId == doctor.Id && ds.SpecialtyId == specialtyId);
            if (!doctorHasSpecialty)
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ không còn phụ trách chuyên khoa của lịch tái khám.");

            var slotStart = targetSlot.SlotDate.ToDateTime(targetSlot.StartTime);
            var slotEnd = targetSlot.SlotDate.ToDateTime(targetSlot.EndTime);

            var hasWorkSchedule = await _dbContext.DoctorWorkSchedules
                .AnyAsync(ws =>
                    ws.DoctorId == doctor.Id &&
                    ws.WorkDate == targetSlot.SlotDate &&
                    ws.StartTime <= targetSlot.StartTime &&
                    ws.EndTime >= targetSlot.EndTime &&
                    ws.IsActive);
            if (!hasWorkSchedule)
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Khung giờ không nằm trong lịch làm việc đang hoạt động của bác sĩ.");

            var isOnApprovedLeave = await _dbContext.DoctorLeaveRequests
                .AnyAsync(leave =>
                    leave.DoctorId == doctor.Id &&
                    leave.Status == DoctorLeaveRequestStatus.Approved &&
                    slotStart < leave.EndDateTime &&
                    slotEnd > leave.StartDateTime);
            if (isOnApprovedLeave)
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ đang trong lịch nghỉ đã được duyệt.");

            var affectedRows = await _dbContext.AppointmentSlots
                .Where(slot => slot.Id == targetSlot.Id && !slot.IsBooked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(slot => slot.IsBooked, true));

            if (affectedRows != 1)
                throw new ConflictException("SLOT_ALREADY_BOOKED", "Khung giờ vừa được đặt bởi người khác. Vui lòng chọn giờ khác.");

            var hasPatientTimeConflict = await _dbContext.Appointments
                .AnyAsync(appointment =>
                    appointment.PatientId == patient.Id &&
                    appointment.AppointmentDate == targetSlot.SlotDate &&
                    AppointmentStatusExtensions.HoldingSlotStatuses.Contains(appointment.Status) &&
                    appointment.StartTime < targetSlot.EndTime &&
                    appointment.EndTime > targetSlot.StartTime);
            if (hasPatientTimeConflict)
                throw new BusinessException("PATIENT_TIME_CONFLICT", "Bạn đã có lịch khám khác trùng hoặc giao lấp với khung giờ này.");

            var doctorUser = await _dbContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == doctor.UserId);

            var newAppointment = new Appointment
            {
                AppointmentCode = $"APT-{_dateTimeProvider.VietnamNow:yyMMdd}-{Guid.NewGuid():N}"[..18].ToUpper(),
                PatientId = patient.Id,
                DoctorId = doctor.Id,
                SpecialtyId = specialtyId,
                AppointmentSlotId = targetSlot.Id,
                AppointmentDate = targetSlot.SlotDate,
                StartTime = targetSlot.StartTime,
                EndTime = targetSlot.EndTime,
                Reason = normalizedReason,
                Status = AppointmentStatus.Pending
            };

            _dbContext.Appointments.Add(newAppointment);
            await _dbContext.SaveChangesAsync();

            revisitReq.Status = RevisitRequestStatus.Accepted;
            revisitReq.NewAppointmentId = newAppointment.Id;

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = newAppointment.Id,
                Action = AppointmentHistoryAction.Created,
                OldStatus = null,
                NewStatus = AppointmentStatus.Pending,
                Note = "Bệnh nhân đặt lịch tái khám theo đề xuất của bác sĩ",
                PerformedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });

            _dbContext.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.Appointment,
                Title = "Đặt lịch tái khám thành công",
                Message = $"Lịch tái khám #{newAppointment.AppointmentCode} ngày {newAppointment.AppointmentDate:dd/MM/yyyy} lúc {newAppointment.StartTime:HH\:mm} đã được tiếp nhận.",
                Route = "/patient/appointments",
                RelatedEntityType = "Appointment",
                RelatedEntityId = newAppointment.Id.ToString(),
                DedupeKey = $"revisit_booked_pat_{revisitReq.Id}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });

            var receptionistRole = await _dbContext.Roles
                .FirstOrDefaultAsync(role => role.Name == ClinicManagement.Application.Common.Constants.RoleNames.Receptionist);
            if (receptionistRole != null)
            {
                var receptionistUserIds = await _dbContext.UserRoles
                    .Where(userRole => userRole.RoleId == receptionistRole.Id)
                    .Select(userRole => userRole.UserId)
                    .ToListAsync();

                var activeReceptionistIds = await _dbContext.Users
                    .Where(user => receptionistUserIds.Contains(user.Id) && user.IsActive)
                    .Select(user => user.Id)
                    .ToListAsync();

                foreach (var receptionistId in activeReceptionistIds)
                {
                    _dbContext.Notifications.Add(new Notification
                    {
                        UserId = receptionistId,
                        Type = NotificationType.Appointment,
                        Title = "Lịch tái khám mới chờ xử lý",
                        Message = $"Bệnh nhân đã đặt lịch tái khám #{newAppointment.AppointmentCode} ngày {newAppointment.AppointmentDate:dd/MM/yyyy}.",
                        Route = "/reception/appointments",
                        RelatedEntityType = "Appointment",
                        RelatedEntityId = newAppointment.Id.ToString(),
                        DedupeKey = $"revisit_booked_rec_{revisitReq.Id}_{receptionistId}",
                        IsRead = false,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new AppointmentDto
            {
                Id = newAppointment.Id,
                AppointmentCode = newAppointment.AppointmentCode,
                PatientId = newAppointment.PatientId,
                DoctorId = newAppointment.DoctorId,
                DoctorName = string.IsNullOrWhiteSpace(doctor.AcademicTitle)
                    ? doctorUser?.FullName ?? string.Empty
                    : $"{doctor.AcademicTitle}. {doctorUser?.FullName}".Trim(),
                SpecialtyId = newAppointment.SpecialtyId,
                SpecialtyName = specialty.Name,
                AppointmentSlotId = newAppointment.AppointmentSlotId,
                AppointmentDate = newAppointment.AppointmentDate,
                StartTime = newAppointment.StartTime,
                EndTime = newAppointment.EndTime,
                Reason = newAppointment.Reason,
                Status = newAppointment.Status.ToString()
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task RejectRevisitRequestAsync(long id, RejectRevisitRequestDto request)
    {
        var patient = await GetCurrentPatientAsync();

        var revisitReq = await _dbContext.RevisitRequests
            .FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.Id);

        if (revisitReq == null) throw new NotFoundException("Đề xuất tái khám không tồn tại.");
        
        if (revisitReq.Status != RevisitRequestStatus.PendingPatientResponse)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể từ chối đề xuất đang chờ phản hồi.");

        revisitReq.Status = RevisitRequestStatus.Rejected;
        // Optionally store the reason if RevisitRequest entity supports it, else ignored.
        
        await _dbContext.SaveChangesAsync();
    }

    private static RevisitRequestDto MapToDto(
        RevisitRequest request,
        string doctorName,
        long specialtyId,
        string specialtyName) => new()
    {
        Id = request.Id,
        AppointmentId = request.AppointmentId,
        PatientId = request.PatientId,
        DoctorId = request.DoctorId,
        SpecialtyId = specialtyId,
        SuggestedDate = request.SuggestedDate,
        Note = request.Note,
        Status = request.Status.ToString(),
        NewAppointmentId = request.NewAppointmentId,
        DoctorName = doctorName ?? string.Empty,
        SpecialtyName = specialtyName ?? string.Empty
    };
}
