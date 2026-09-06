using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
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

public class ChangeRequestService : IChangeRequestService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ChangeRequestService(
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

    private static bool IsTransientContentionException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
            {
                return true;
            }

            var typeFullName = current.GetType().FullName ?? string.Empty;
            if (typeFullName.Contains("SqliteException", StringComparison.OrdinalIgnoreCase))
            {
                var errCodeProp = current.GetType().GetProperty("SqliteErrorCode");
                if (errCodeProp != null && errCodeProp.GetValue(current) is int errCode && (errCode == 5 || errCode == 6))
                {
                    return true;
                }
            }

            if (typeFullName.Contains("SqlException", StringComparison.OrdinalIgnoreCase))
            {
                var numberProp = current.GetType().GetProperty("Number");
                if (numberProp != null && numberProp.GetValue(current) is int num && num == 1205)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string conflictErrorCode, string conflictErrorMessage)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (IsTransientContentionException(ex))
            {
                _dbContext.ChangeTracker.Clear();
                if (attempt == 2)
                {
                    throw new ConflictException(conflictErrorCode, conflictErrorMessage);
                }
                await Task.Delay(35 * (attempt + 1));
            }
        }

        throw new ConflictException(conflictErrorCode, conflictErrorMessage);
    }

    private async Task ExecuteWithRetryAsync(Func<Task> operation, string conflictErrorCode, string conflictErrorMessage)
    {
        await ExecuteWithRetryAsync<bool>(async () =>
        {
            await operation();
            return true;
        }, conflictErrorCode, conflictErrorMessage);
    }

    public async Task<ChangeRequestDto> CreateRescheduleRequestAsync(long appointmentId, CreateRescheduleRequestDto request)
    {
        var userId = GetUserId();
        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null) throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var normalizedReason = request.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length < 5)
            throw new ValidationException("Reason", "Lý do đổi lịch phải có ít nhất 5 ký tự.");
        if (normalizedReason.Length > 500)
            throw new ValidationException("Reason", "Lý do đổi lịch không được vượt quá 500 ký tự.");

        return await ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var appointment = await _dbContext.Appointments
                    .Include(a => a.AppointmentSlot)
                    .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientId == patient.Id);

                if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

                if (appointment.Status == AppointmentStatus.PendingReschedule || appointment.Status == AppointmentStatus.PendingCancellation)
                    throw new ConflictException("ACTIVE_CHANGE_REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");

                if (appointment.Status != AppointmentStatus.Pending && appointment.Status != AppointmentStatus.Confirmed)
                    throw new BusinessException("INVALID_STATE", "Chỉ có thể đổi lịch khi lịch hẹn đang ở trạng thái Pending hoặc Confirmed.");

                var pendingRequest = await _dbContext.AppointmentChangeRequests
                    .AnyAsync(r => r.AppointmentId == appointmentId && r.Status == AppointmentChangeRequestStatus.Pending);
                if (pendingRequest)
                    throw new ConflictException("ACTIVE_CHANGE_REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");

                var targetSlot = await _dbContext.AppointmentSlots
                    .Include(s => s.Doctor)
                    .FirstOrDefaultAsync(s => s.Id == request.RequestedSlotId);
                if (targetSlot == null) throw new NotFoundException("Slot yêu cầu không tồn tại.");

                if (targetSlot.Id == appointment.AppointmentSlotId)
                    throw new BusinessException("INVALID_TARGET", "Slot yêu cầu không được trùng với slot hiện tại.");

                if (targetSlot.DoctorId != appointment.DoctorId)
                    throw new BusinessException("DOCTOR_MISMATCH", "Slot yêu cầu phải thuộc cùng bác sĩ với lịch hẹn ban đầu.");

                if (targetSlot.SlotDate.DayOfWeek == DayOfWeek.Sunday)
                    throw new BusinessException("INVALID_SCHEDULE", "Phòng khám không làm việc vào Chủ nhật.");

                var slotStart = targetSlot.SlotDate.ToDateTime(targetSlot.StartTime);
                var slotEnd = targetSlot.SlotDate.ToDateTime(targetSlot.EndTime);

                if (slotStart <= _dateTimeProvider.VietnamNow)
                    throw new BusinessException("INVALID_TARGET", "Slot yêu cầu phải ở trong tương lai.");

                if ((slotEnd - slotStart).TotalMinutes != 30)
                    throw new BusinessException("INVALID_SCHEDULE", "Slot khám phải có thời lượng đúng 30 phút.");

                if (!targetSlot.Doctor.IsActive)
                    throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ hiện không hoạt động.");

                var hasWorkSchedule = await _dbContext.DoctorWorkSchedules
                    .AnyAsync(ws => ws.DoctorId == targetSlot.DoctorId
                                 && ws.WorkDate == targetSlot.SlotDate
                                 && ws.StartTime <= targetSlot.StartTime
                                 && ws.EndTime >= targetSlot.EndTime
                                 && ws.IsActive);
                if (!hasWorkSchedule)
                    throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Slot không nằm trong lịch làm việc hoạt động của bác sĩ.");

                var onLeave = await _dbContext.DoctorLeaveRequests
                    .AnyAsync(l => l.DoctorId == targetSlot.DoctorId
                                && l.Status == DoctorLeaveRequestStatus.Approved
                                && l.StartDateTime < slotEnd
                                && l.EndDateTime > slotStart);
                if (onLeave)
                    throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ có lịch nghỉ trong khung giờ này.");

                if (targetSlot.IsBooked)
                    throw new ConflictException("TARGET_SLOT_ALREADY_BOOKED", "Slot yêu cầu đã được đặt hoặc không khả dụng.");

                var hasTimeConflict = await _dbContext.Appointments
                    .AnyAsync(a => a.PatientId == patient.Id
                                && a.Id != appointment.Id
                                && a.AppointmentDate == targetSlot.SlotDate
                                && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status)
                                && a.StartTime < targetSlot.EndTime
                                && a.EndTime > targetSlot.StartTime);
                if (hasTimeConflict)
                    throw new ConflictException("PATIENT_TIME_CONFLICT", "Bạn đã có lịch khám khác trong khung giờ này.");

                var oldStatus = appointment.Status;

                // Atomic conditional update on appointment status
                var appAffected = await _dbContext.Appointments
                    .Where(a => a.Id == appointmentId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed))
                    .ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, AppointmentStatus.PendingReschedule));

                if (appAffected == 0)
                    throw new ConflictException("ACTIVE_CHANGE_REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");

                var changeRequest = new AppointmentChangeRequest
                {
                    AppointmentId = appointment.Id,
                    RequestType = AppointmentChangeRequestType.Reschedule,
                    RequestedSlotId = targetSlot.Id,
                    Reason = normalizedReason,
                    Status = AppointmentChangeRequestStatus.Pending,
                    OriginalAppointmentStatus = oldStatus,
                    RequestedByUserId = userId,
                    CreatedAt = _dateTimeProvider.UtcNow
                };

                _dbContext.AppointmentChangeRequests.Add(changeRequest);

                _dbContext.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = appointment.Id,
                    Action = AppointmentHistoryAction.RescheduleRequested,
                    OldStatus = oldStatus,
                    NewStatus = AppointmentStatus.PendingReschedule,
                    Note = normalizedReason,
                    PerformedByUserId = userId,
                    CreatedAt = _dateTimeProvider.UtcNow
                });

                await _dbContext.SaveChangesAsync();

                await NotifyReceptionistsAsync(
                    "Yêu cầu dời lịch khám mới",
                    $"Bệnh nhân yêu cầu dời lịch khám #{appointment.AppointmentCode}.",
                    changeRequest.Id.ToString(),
                    $"chg_req_resched_{changeRequest.Id}");

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return await GetChangeRequestByIdAsync(changeRequest.Id);
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { }
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }, "ACTIVE_CHANGE_REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");
    }

    public async Task<ChangeRequestDto> CreateCancellationRequestAsync(long appointmentId, CreateCancellationRequestDto request)
    {
        var userId = GetUserId();
        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null) throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var normalizedReason = request.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length < 5)
            throw new ValidationException("Reason", "Lý do hủy lịch phải có ít nhất 5 ký tự.");
        if (normalizedReason.Length > 500)
            throw new ValidationException("Reason", "Lý do hủy lịch không được vượt quá 500 ký tự.");

        return await ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var appointment = await _dbContext.Appointments
                    .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientId == patient.Id);

                if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

                if (appointment.Status == AppointmentStatus.PendingReschedule || appointment.Status == AppointmentStatus.PendingCancellation)
                    throw new ConflictException("ACTIVE_CHANGE_REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");

                if (appointment.Status != AppointmentStatus.Pending && appointment.Status != AppointmentStatus.Confirmed)
                    throw new BusinessException("INVALID_STATE", "Chỉ có thể hủy lịch khi lịch hẹn đang ở trạng thái Pending hoặc Confirmed.");

                var pendingRequest = await _dbContext.AppointmentChangeRequests
                    .AnyAsync(r => r.AppointmentId == appointmentId && r.Status == AppointmentChangeRequestStatus.Pending);
                if (pendingRequest)
                    throw new ConflictException("ACTIVE_CHANGE_REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");

                var oldStatus = appointment.Status;

                // Atomic conditional update on appointment status
                var appAffected = await _dbContext.Appointments
                    .Where(a => a.Id == appointmentId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed))
                    .ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, AppointmentStatus.PendingCancellation));

                if (appAffected == 0)
                    throw new ConflictException("ACTIVE_CHANGE_REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");

                var changeRequest = new AppointmentChangeRequest
                {
                    AppointmentId = appointment.Id,
                    RequestType = AppointmentChangeRequestType.Cancellation,
                    Reason = normalizedReason,
                    Status = AppointmentChangeRequestStatus.Pending,
                    OriginalAppointmentStatus = oldStatus,
                    RequestedByUserId = userId,
                    CreatedAt = _dateTimeProvider.UtcNow
                };

                _dbContext.AppointmentChangeRequests.Add(changeRequest);

                _dbContext.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = appointment.Id,
                    Action = AppointmentHistoryAction.CancelRequested,
                    OldStatus = oldStatus,
                    NewStatus = AppointmentStatus.PendingCancellation,
                    Note = normalizedReason,
                    PerformedByUserId = userId,
                    CreatedAt = _dateTimeProvider.UtcNow
                });

                await _dbContext.SaveChangesAsync();

                await NotifyReceptionistsAsync(
                    "Yêu cầu hủy lịch khám mới",
                    $"Bệnh nhân yêu cầu hủy lịch khám #{appointment.AppointmentCode}.",
                    changeRequest.Id.ToString(),
                    $"chg_req_cancel_{changeRequest.Id}");

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return await GetChangeRequestByIdAsync(changeRequest.Id);
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { }
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }, "ACTIVE_CHANGE_REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");
    }

    public async Task WithdrawRequestAsync(long requestId)
    {
        var userId = GetUserId();

        await ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var request = await _dbContext.AppointmentChangeRequests
                    .Include(r => r.Appointment)
                    .FirstOrDefaultAsync(r => r.Id == requestId && r.RequestedByUserId == userId);

                if (request == null) throw new NotFoundException("Yêu cầu không tồn tại hoặc không có quyền rút.");

                if (request.Status != AppointmentChangeRequestStatus.Pending)
                    throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý hoặc đã rút.");

                var reqAffected = await _dbContext.AppointmentChangeRequests
                    .Where(r => r.Id == requestId && r.RequestedByUserId == userId && r.Status == AppointmentChangeRequestStatus.Pending)
                    .ExecuteUpdateAsync(r => r
                        .SetProperty(x => x.Status, AppointmentChangeRequestStatus.Withdrawn)
                        .SetProperty(x => x.ProcessedByUserId, userId)
                        .SetProperty(x => x.ProcessedAt, _dateTimeProvider.UtcNow));

                if (reqAffected == 0)
                    throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý hoặc đã rút.");

                var appointment = request.Appointment ?? await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == request.AppointmentId);
                if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

                var restoredStatus = request.OriginalAppointmentStatus ?? AppointmentStatus.Confirmed;
                if (!request.OriginalAppointmentStatus.HasValue)
                {
                    var history = await _dbContext.AppointmentHistories
                        .Where(h => h.AppointmentId == request.AppointmentId
                                 && (h.Action == AppointmentHistoryAction.RescheduleRequested || h.Action == AppointmentHistoryAction.CancelRequested))
                        .OrderByDescending(h => h.CreatedAt)
                        .FirstOrDefaultAsync();
                    restoredStatus = history?.OldStatus ?? AppointmentStatus.Confirmed;
                }

                var oldAppointmentStatus = appointment.Status;
                appointment.Status = restoredStatus;

                _dbContext.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = appointment.Id,
                    Action = restoredStatus == AppointmentStatus.Confirmed ? AppointmentHistoryAction.Confirmed : AppointmentHistoryAction.Created,
                    OldStatus = oldAppointmentStatus,
                    NewStatus = restoredStatus,
                    Note = "Bệnh nhân rút yêu cầu thay đổi",
                    PerformedByUserId = userId,
                    CreatedAt = _dateTimeProvider.UtcNow
                });

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { }
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }, "CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý hoặc đã rút.");
    }

    public async Task<PagedResult<ChangeRequestDto>> GetMyChangeRequestsAsync(string? status, int page, int pageSize)
    {
        var userId = GetUserId();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = from cr in _dbContext.AppointmentChangeRequests
                    join a in _dbContext.Appointments on cr.AppointmentId equals a.Id
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals pu.Id
                    join d in _dbContext.Doctors on a.DoctorId equals d.Id
                    join du in _dbContext.Users on d.UserId equals du.Id
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    join rs in _dbContext.AppointmentSlots on cr.RequestedSlotId equals rs.Id into rsg
                    from reqSlot in rsg.DefaultIfEmpty()
                    where cr.RequestedByUserId == userId
                    select new ChangeRequestDto
                    {
                        Id = cr.Id,
                        AppointmentId = cr.AppointmentId,
                        RequestType = cr.RequestType.ToString(),
                        RequestedSlotId = cr.RequestedSlotId,
                        Reason = cr.Reason,
                        Status = cr.Status.ToString(),
                        OriginalAppointmentStatus = cr.OriginalAppointmentStatus.HasValue ? cr.OriginalAppointmentStatus!.Value.ToString() : null,
                        RequestedByUserId = cr.RequestedByUserId,
                        ProcessedByUserId = cr.ProcessedByUserId,
                        CreatedAt = cr.CreatedAt,
                        ProcessedAt = cr.ProcessedAt,
                        AppointmentCode = a.AppointmentCode,
                        PatientName = pu.FullName,
                        DoctorName = du.FullName,
                        SpecialtyName = s.Name,
                        CurrentSlotDate = a.AppointmentDate,
                        CurrentStartTime = a.StartTime,
                        CurrentEndTime = a.EndTime,
                        RequestedSlotDate = reqSlot != null ? reqSlot.SlotDate : (DateOnly?)null,
                        RequestedStartTime = reqSlot != null ? reqSlot.StartTime : (TimeOnly?)null,
                        RequestedEndTime = reqSlot != null ? reqSlot.EndTime : (TimeOnly?)null
                    };

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AppointmentChangeRequestStatus>(status.Trim(), true, out var parsedStatus))
                throw new ValidationException("status", "Trạng thái yêu cầu không hợp lệ.");
            var parsedStatusStr = parsedStatus.ToString();
            query = query.Where(r => r.Status == parsedStatusStr);
        }

        query = query.OrderByDescending(r => r.CreatedAt);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<ChangeRequestDto>(items, totalItems, page, pageSize);
    }

    public async Task<PagedResult<ChangeRequestDto>> GetAllChangeRequestsAsync(string? requestType, string? status, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = from cr in _dbContext.AppointmentChangeRequests
                    join a in _dbContext.Appointments on cr.AppointmentId equals a.Id
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals pu.Id
                    join d in _dbContext.Doctors on a.DoctorId equals d.Id
                    join du in _dbContext.Users on d.UserId equals du.Id
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    join rs in _dbContext.AppointmentSlots on cr.RequestedSlotId equals rs.Id into rsg
                    from reqSlot in rsg.DefaultIfEmpty()
                    select new ChangeRequestDto
                    {
                        Id = cr.Id,
                        AppointmentId = cr.AppointmentId,
                        RequestType = cr.RequestType.ToString(),
                        RequestedSlotId = cr.RequestedSlotId,
                        Reason = cr.Reason,
                        Status = cr.Status.ToString(),
                        OriginalAppointmentStatus = cr.OriginalAppointmentStatus.HasValue ? cr.OriginalAppointmentStatus!.Value.ToString() : null,
                        RequestedByUserId = cr.RequestedByUserId,
                        ProcessedByUserId = cr.ProcessedByUserId,
                        CreatedAt = cr.CreatedAt,
                        ProcessedAt = cr.ProcessedAt,
                        AppointmentCode = a.AppointmentCode,
                        PatientName = pu.FullName,
                        DoctorName = du.FullName,
                        SpecialtyName = s.Name,
                        CurrentSlotDate = a.AppointmentDate,
                        CurrentStartTime = a.StartTime,
                        CurrentEndTime = a.EndTime,
                        RequestedSlotDate = reqSlot != null ? reqSlot.SlotDate : (DateOnly?)null,
                        RequestedStartTime = reqSlot != null ? reqSlot.StartTime : (TimeOnly?)null,
                        RequestedEndTime = reqSlot != null ? reqSlot.EndTime : (TimeOnly?)null
                    };

        if (!string.IsNullOrWhiteSpace(requestType))
        {
            if (!Enum.TryParse<AppointmentChangeRequestType>(requestType.Trim(), true, out var parsedType))
                throw new ValidationException("requestType", "Loại yêu cầu không hợp lệ.");
            var parsedTypeStr = parsedType.ToString();
            query = query.Where(r => r.RequestType == parsedTypeStr);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AppointmentChangeRequestStatus>(status.Trim(), true, out var parsedStatus))
                throw new ValidationException("status", "Trạng thái yêu cầu không hợp lệ.");
            var parsedStatusStr = parsedStatus.ToString();
            query = query.Where(r => r.Status == parsedStatusStr);
        }

        query = query.OrderByDescending(r => r.CreatedAt);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<ChangeRequestDto>(items, totalItems, page, pageSize);
    }

    public async Task<ChangeRequestDto> GetChangeRequestByIdAsync(long requestId)
    {
        var query = from cr in _dbContext.AppointmentChangeRequests
                    join a in _dbContext.Appointments on cr.AppointmentId equals a.Id
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals pu.Id
                    join d in _dbContext.Doctors on a.DoctorId equals d.Id
                    join du in _dbContext.Users on d.UserId equals du.Id
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    join rs in _dbContext.AppointmentSlots on cr.RequestedSlotId equals rs.Id into rsg
                    from reqSlot in rsg.DefaultIfEmpty()
                    where cr.Id == requestId
                    select new ChangeRequestDto
                    {
                        Id = cr.Id,
                        AppointmentId = cr.AppointmentId,
                        RequestType = cr.RequestType.ToString(),
                        RequestedSlotId = cr.RequestedSlotId,
                        Reason = cr.Reason,
                        Status = cr.Status.ToString(),
                        OriginalAppointmentStatus = cr.OriginalAppointmentStatus.HasValue ? cr.OriginalAppointmentStatus!.Value.ToString() : null,
                        RequestedByUserId = cr.RequestedByUserId,
                        ProcessedByUserId = cr.ProcessedByUserId,
                        CreatedAt = cr.CreatedAt,
                        ProcessedAt = cr.ProcessedAt,
                        AppointmentCode = a.AppointmentCode,
                        PatientName = pu.FullName,
                        DoctorName = du.FullName,
                        SpecialtyName = s.Name,
                        CurrentSlotDate = a.AppointmentDate,
                        CurrentStartTime = a.StartTime,
                        CurrentEndTime = a.EndTime,
                        RequestedSlotDate = reqSlot != null ? reqSlot.SlotDate : (DateOnly?)null,
                        RequestedStartTime = reqSlot != null ? reqSlot.StartTime : (TimeOnly?)null,
                        RequestedEndTime = reqSlot != null ? reqSlot.EndTime : (TimeOnly?)null
                    };

        var req = await query.FirstOrDefaultAsync();
        if (req == null) throw new NotFoundException("Yêu cầu không tồn tại.");
        return req;
    }

    public async Task ApproveRescheduleAsync(long requestId, ProcessChangeRequestDto request)
    {
        var userId = GetUserId();

        await ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var changeReq = await _dbContext.AppointmentChangeRequests
                .Include(r => r.Appointment)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (changeReq == null) throw new NotFoundException("Yêu cầu không tồn tại.");

            if (changeReq.Status != AppointmentChangeRequestStatus.Pending)
                throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");

            if (changeReq.RequestType != AppointmentChangeRequestType.Reschedule || !changeReq.RequestedSlotId.HasValue)
                throw new BusinessException("INVALID_TYPE", "Yêu cầu không phải là yêu cầu đổi lịch.");

            if (request.NewSlotId.HasValue && request.NewSlotId.Value != changeReq.RequestedSlotId.Value)
                throw new BusinessException("INVALID_TARGET", "Không được thay đổi slot khám đã yêu cầu.");

            var appointment = changeReq.Appointment ?? await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == changeReq.AppointmentId);
            if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

            if (appointment.Status != AppointmentStatus.PendingReschedule)
                throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Lịch hẹn không còn ở trạng thái chờ đổi lịch hoặc đã được xử lý.");

            var targetSlotId = changeReq.RequestedSlotId.Value;

            var targetSlot = await _dbContext.AppointmentSlots
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == targetSlotId);
            if (targetSlot == null)
                throw new NotFoundException("Slot yêu cầu không tồn tại.");

            if (targetSlot.DoctorId != appointment.DoctorId)
                throw new BusinessException("DOCTOR_MISMATCH", "Slot yêu cầu không thuộc bác sĩ của lịch hẹn.");

            if (targetSlot.SlotDate.DayOfWeek == DayOfWeek.Sunday)
                throw new BusinessException("INVALID_SCHEDULE", "Phòng khám không làm việc vào Chủ nhật.");

            var slotStart = targetSlot.SlotDate.ToDateTime(targetSlot.StartTime);
            var slotEnd = targetSlot.SlotDate.ToDateTime(targetSlot.EndTime);

            if (slotStart <= _dateTimeProvider.VietnamNow)
                throw new BusinessException("INVALID_TARGET", "Slot yêu cầu phải ở trong tương lai.");

            if ((slotEnd - slotStart).TotalMinutes != 30)
                throw new BusinessException("INVALID_SCHEDULE", "Slot khám phải có thời lượng đúng 30 phút.");

            if (!targetSlot.Doctor.IsActive)
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ hiện không hoạt động.");

            var hasWorkSchedule = await _dbContext.DoctorWorkSchedules
                .AnyAsync(ws => ws.DoctorId == targetSlot.DoctorId
                             && ws.WorkDate == targetSlot.SlotDate
                             && ws.StartTime <= targetSlot.StartTime
                             && ws.EndTime >= targetSlot.EndTime
                             && ws.IsActive);
            if (!hasWorkSchedule)
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Slot không nằm trong lịch làm việc hoạt động của bác sĩ.");

            var onLeave = await _dbContext.DoctorLeaveRequests
                .AnyAsync(l => l.DoctorId == targetSlot.DoctorId
                            && l.Status == DoctorLeaveRequestStatus.Approved
                            && l.StartDateTime < slotEnd
                            && l.EndDateTime > slotStart);
            if (onLeave)
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", "Bác sĩ có lịch nghỉ trong khung giờ này.");

            var hasPatientConflict = await _dbContext.Appointments
                .AnyAsync(a => a.PatientId == appointment.PatientId
                            && a.Id != appointment.Id
                            && a.AppointmentDate == targetSlot.SlotDate
                            && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status)
                            && a.StartTime < targetSlot.EndTime
                            && a.EndTime > targetSlot.StartTime);
            if (hasPatientConflict)
                throw new ConflictException("PATIENT_TIME_CONFLICT", "Bệnh nhân đã có lịch khám khác trong khung giờ này.");

            var reqAffected = await _dbContext.AppointmentChangeRequests
                .Where(r => r.Id == requestId && r.Status == AppointmentChangeRequestStatus.Pending)
                .ExecuteUpdateAsync(r => r
                    .SetProperty(x => x.Status, AppointmentChangeRequestStatus.Approved)
                    .SetProperty(x => x.ProcessedByUserId, userId)
                    .SetProperty(x => x.ProcessedAt, _dateTimeProvider.UtcNow));

            if (reqAffected == 0)
                throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");

            // Atomic conditional update on target slot
            var slotAffected = await _dbContext.AppointmentSlots
                .Where(s => s.Id == targetSlotId && !s.IsBooked)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, true));

            if (slotAffected == 0)
                throw new ConflictException("TARGET_SLOT_ALREADY_BOOKED", "Slot yêu cầu đã được đặt hoặc không khả dụng.");

            // Release old slot
            var oldSlotId = appointment.AppointmentSlotId;
            await _dbContext.AppointmentSlots
                .Where(s => s.Id == oldSlotId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, false));

            // Update appointment
            var oldStatus = appointment.Status;
            appointment.AppointmentSlotId = targetSlot.Id;
            appointment.AppointmentDate = targetSlot.SlotDate;
            appointment.StartTime = targetSlot.StartTime;
            appointment.EndTime = targetSlot.EndTime;
            appointment.Status = AppointmentStatus.Confirmed;

            var userNote = !string.IsNullOrWhiteSpace(request.Note) ? request.Note.Trim() : (!string.IsNullOrWhiteSpace(request.Reason) ? request.Reason.Trim() : "Lễ tân xác nhận đổi lịch");
            var historyNote = $"Đổi lịch sang ngày {targetSlot.SlotDate:dd/MM/yyyy} ({targetSlot.StartTime:HH\\:mm} - {targetSlot.EndTime:HH\\:mm}). Ghi chú: {userNote}";

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.Rescheduled,
                OldStatus = oldStatus,
                NewStatus = AppointmentStatus.Confirmed,
                Note = historyNote,
                PerformedByUserId = userId,
                CreatedAt = _dateTimeProvider.UtcNow
            });

            await NotifyPatientForAppointmentAsync(
                appointment.Id,
                "Yêu cầu dời lịch khám đã được duyệt",
                $"Yêu cầu dời lịch khám #{appointment.AppointmentCode} của bạn đã được chấp thuận. Lịch mới: {targetSlot.SlotDate:dd/MM/yyyy} ({targetSlot.StartTime:HH\\:mm} - {targetSlot.EndTime:HH\\:mm}).",
                $"appt_chg_proc_{requestId}_approved");

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }, "CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");
    }

    public async Task ApproveCancellationAsync(long requestId, ProcessChangeRequestDto request)
    {
        var userId = GetUserId();

        await ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var changeReq = await _dbContext.AppointmentChangeRequests
                .Include(r => r.Appointment)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (changeReq == null) throw new NotFoundException("Yêu cầu không tồn tại.");

            if (changeReq.Status != AppointmentChangeRequestStatus.Pending)
                throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");

            if (changeReq.RequestType != AppointmentChangeRequestType.Cancellation)
                throw new BusinessException("INVALID_TYPE", "Yêu cầu không phải là yêu cầu hủy lịch.");

            var appointment = changeReq.Appointment ?? await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == changeReq.AppointmentId);
            if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

            if (appointment.Status != AppointmentStatus.PendingCancellation)
                throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Lịch hẹn không còn ở trạng thái chờ hủy lịch hoặc đã được xử lý.");

            var reqAffected = await _dbContext.AppointmentChangeRequests
                .Where(r => r.Id == requestId && r.Status == AppointmentChangeRequestStatus.Pending)
                .ExecuteUpdateAsync(r => r
                    .SetProperty(x => x.Status, AppointmentChangeRequestStatus.Approved)
                    .SetProperty(x => x.ProcessedByUserId, userId)
                    .SetProperty(x => x.ProcessedAt, _dateTimeProvider.UtcNow));

            if (reqAffected == 0)
                throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");

            // Release slot
            await _dbContext.AppointmentSlots
                .Where(s => s.Id == appointment.AppointmentSlotId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, false));

            var oldStatus = appointment.Status;
            appointment.Status = AppointmentStatus.Cancelled;

            var processNote = !string.IsNullOrWhiteSpace(request.Note) ? request.Note.Trim() : (!string.IsNullOrWhiteSpace(request.Reason) ? request.Reason.Trim() : "Lễ tân xác nhận hủy lịch");

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.Cancelled,
                OldStatus = oldStatus,
                NewStatus = AppointmentStatus.Cancelled,
                Note = processNote,
                PerformedByUserId = userId,
                CreatedAt = _dateTimeProvider.UtcNow
            });

            await NotifyPatientForAppointmentAsync(
                appointment.Id,
                "Yêu cầu hủy lịch khám đã được duyệt",
                $"Yêu cầu hủy lịch khám #{appointment.AppointmentCode} của bạn đã được chấp thuận.",
                $"appt_chg_proc_{requestId}_approved");

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }, "CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");
    }

    public async Task RejectRequestAsync(long requestId, ProcessChangeRequestDto request)
    {
        var userId = GetUserId();

        var rejectReason = !string.IsNullOrWhiteSpace(request.Note) ? request.Note.Trim() : (!string.IsNullOrWhiteSpace(request.Reason) ? request.Reason.Trim() : string.Empty);
        if (string.IsNullOrWhiteSpace(rejectReason) || rejectReason.Length < 5)
            throw new ValidationException("Note", "Lý do từ chối phải có ít nhất 5 ký tự.");

        await ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var changeReq = await _dbContext.AppointmentChangeRequests
                .Include(r => r.Appointment)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (changeReq == null) throw new NotFoundException("Yêu cầu không tồn tại.");

            if (changeReq.Status != AppointmentChangeRequestStatus.Pending)
                throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");

            var reqAffected = await _dbContext.AppointmentChangeRequests
                .Where(r => r.Id == requestId && r.Status == AppointmentChangeRequestStatus.Pending)
                .ExecuteUpdateAsync(r => r
                    .SetProperty(x => x.Status, AppointmentChangeRequestStatus.Rejected)
                    .SetProperty(x => x.ProcessedByUserId, userId)
                    .SetProperty(x => x.ProcessedAt, _dateTimeProvider.UtcNow));

            if (reqAffected == 0)
                throw new ConflictException("CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");

            var appointment = changeReq.Appointment ?? await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == changeReq.AppointmentId);
            if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

            var restoredStatus = changeReq.OriginalAppointmentStatus ?? AppointmentStatus.Confirmed;
            if (!changeReq.OriginalAppointmentStatus.HasValue)
            {
                var history = await _dbContext.AppointmentHistories
                    .Where(h => h.AppointmentId == changeReq.AppointmentId
                             && (h.Action == AppointmentHistoryAction.RescheduleRequested || h.Action == AppointmentHistoryAction.CancelRequested))
                    .OrderByDescending(h => h.CreatedAt)
                    .FirstOrDefaultAsync();
                restoredStatus = history?.OldStatus ?? AppointmentStatus.Confirmed;
            }

            var oldAppointmentStatus = appointment.Status;
            appointment.Status = restoredStatus;

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = restoredStatus == AppointmentStatus.Confirmed ? AppointmentHistoryAction.Confirmed : AppointmentHistoryAction.Created,
                OldStatus = oldAppointmentStatus,
                NewStatus = restoredStatus,
                Note = $"Từ chối yêu cầu: {rejectReason}",
                PerformedByUserId = userId,
                CreatedAt = _dateTimeProvider.UtcNow
            });

            await NotifyPatientForAppointmentAsync(
                changeReq.AppointmentId,
                "Yêu cầu thay đổi lịch khám bị từ chối",
                $"Yêu cầu thay đổi cho lịch khám #{appointment.AppointmentCode} đã bị từ chối. Lý do: {rejectReason}.",
                $"appt_chg_proc_{requestId}_rejected");

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }, "CHANGE_REQUEST_ALREADY_PROCESSED", "Yêu cầu thay đổi đã được xử lý bởi người dùng khác.");
    }

    private async Task NotifyReceptionistsAsync(string title, string message, string relatedEntityId, string dedupeKeyPrefix)
    {
        var recRole = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == ClinicManagement.Application.Common.Constants.RoleNames.Receptionist);
        if (recRole == null) return;

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
                Type = NotificationType.AppointmentChangeRequest,
                Title = title,
                Message = message,
                Route = "/reception/change-requests",
                RelatedEntityType = "AppointmentChangeRequest",
                RelatedEntityId = relatedEntityId,
                DedupeKey = $"{dedupeKeyPrefix}_{recUserId}",
                IsRead = false,
                CreatedAtUtc = _dateTimeProvider.UtcNow
            });
        }
    }

    private async Task NotifyPatientForAppointmentAsync(long appointmentId, string title, string message, string dedupeKey)
    {
        var patientUserId = await (from a in _dbContext.Appointments
                                   join p in _dbContext.Patients on a.PatientId equals p.Id
                                   where a.Id == appointmentId
                                   select p.UserId).FirstOrDefaultAsync();

        if (patientUserId != Guid.Empty)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = patientUserId,
                Type = NotificationType.AppointmentChangeRequest,
                Title = title,
                Message = message,
                Route = "/patient/appointments",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointmentId.ToString(),
                DedupeKey = dedupeKey,
                IsRead = false,
                CreatedAtUtc = _dateTimeProvider.UtcNow
            });
        }
    }
}
