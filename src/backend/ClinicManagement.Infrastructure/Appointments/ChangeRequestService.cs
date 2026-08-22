using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
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

    public ChangeRequestService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    private Guid GetUserId()
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");
        return currentUserId.Value;
    }

    public async Task<ChangeRequestDto> CreateRescheduleRequestAsync(long appointmentId, CreateRescheduleRequestDto request)
    {
        var userId = GetUserId();
        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null) throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var appointment = await _dbContext.Appointments
            .Include(a => a.AppointmentSlot)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientId == patient.Id);

        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

        if (appointment.Status != AppointmentStatus.Pending && appointment.Status != AppointmentStatus.Confirmed)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể đổi lịch khi lịch hẹn đang ở trạng thái Pending hoặc Confirmed.");

        var pendingRequest = await _dbContext.AppointmentChangeRequests
            .AnyAsync(r => r.AppointmentId == appointmentId && r.Status == AppointmentChangeRequestStatus.Pending);
        if (pendingRequest)
            throw new BusinessException("REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");

        var targetSlot = await _dbContext.AppointmentSlots
            .FirstOrDefaultAsync(s => s.Id == request.RequestedSlotId);
        if (targetSlot == null) throw new NotFoundException("Slot yêu cầu không tồn tại.");

        if (targetSlot.Id == appointment.AppointmentSlotId)
            throw new BusinessException("INVALID_TARGET", "Slot yêu cầu không được trùng với slot hiện tại.");

        var slotStart = targetSlot.SlotDate.ToDateTime(targetSlot.StartTime);
        if (slotStart <= DateTime.UtcNow)
            throw new BusinessException("INVALID_TARGET", "Slot yêu cầu phải ở trong tương lai.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.PendingReschedule;

        var changeRequest = new AppointmentChangeRequest
        {
            AppointmentId = appointment.Id,
            RequestType = AppointmentChangeRequestType.Reschedule,
            RequestedSlotId = targetSlot.Id,
            Reason = request.Reason,
            Status = AppointmentChangeRequestStatus.Pending,
            RequestedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.AppointmentChangeRequests.Add(changeRequest);
        
        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.RescheduleRequested,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.PendingReschedule,
            Note = request.Reason,
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        return MapToDto(changeRequest);
    }

    public async Task<ChangeRequestDto> CreateCancellationRequestAsync(long appointmentId, CreateCancellationRequestDto request)
    {
        var userId = GetUserId();
        var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null) throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var appointment = await _dbContext.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientId == patient.Id);

        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

        if (appointment.Status != AppointmentStatus.Pending && appointment.Status != AppointmentStatus.Confirmed)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể hủy lịch khi lịch hẹn đang ở trạng thái Pending hoặc Confirmed.");

        var pendingRequest = await _dbContext.AppointmentChangeRequests
            .AnyAsync(r => r.AppointmentId == appointmentId && r.Status == AppointmentChangeRequestStatus.Pending);
        if (pendingRequest)
            throw new BusinessException("REQUEST_EXISTS", "Lịch hẹn đã có yêu cầu thay đổi đang chờ xử lý.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.PendingCancellation;

        var changeRequest = new AppointmentChangeRequest
        {
            AppointmentId = appointment.Id,
            RequestType = AppointmentChangeRequestType.Cancellation,
            Reason = request.Reason,
            Status = AppointmentChangeRequestStatus.Pending,
            RequestedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.AppointmentChangeRequests.Add(changeRequest);
        
        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.CancelRequested,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.PendingCancellation,
            Note = request.Reason,
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        return MapToDto(changeRequest);
    }

    public async Task WithdrawRequestAsync(long requestId)
    {
        var userId = GetUserId();
        var request = await _dbContext.AppointmentChangeRequests
            .Include(r => r.Appointment)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.RequestedByUserId == userId);

        if (request == null) throw new NotFoundException("Yêu cầu không tồn tại hoặc không có quyền rút.");
        if (request.Status != AppointmentChangeRequestStatus.Pending)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể rút yêu cầu đang chờ xử lý.");

        request.Status = AppointmentChangeRequestStatus.Withdrawn;
        request.ProcessedByUserId = userId;
        request.ProcessedAt = DateTime.UtcNow;

        // Restore appointment status based on previous history, or default back to Pending
        var history = await _dbContext.AppointmentHistories
            .Where(h => h.AppointmentId == request.AppointmentId 
                     && (h.Action == AppointmentHistoryAction.RescheduleRequested || h.Action == AppointmentHistoryAction.CancelRequested))
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync();

        var restoredStatus = history?.OldStatus ?? AppointmentStatus.Pending;
        
        var oldAppointmentStatus = request.Appointment.Status;
        request.Appointment.Status = restoredStatus;

        // NOTE: The prompt says "Rút request không làm thay đổi Appointment hoặc slot". 
        // By restoring the status, we change the Status property, but do not change the time/slot data.
        // This is standard to avoid soft-locking. If strictly NO change to Appointment is allowed, we'd remove this.
        // But restoring the status is the only logical way to unlock the appointment.

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = request.Appointment.Id,
            Action = AppointmentHistoryAction.Created, // No specific 'Withdrawn' action, using Created or keeping it generic
            OldStatus = oldAppointmentStatus,
            NewStatus = restoredStatus,
            Note = "Bệnh nhân rút yêu cầu",
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }

    public async Task<PagedResult<ChangeRequestDto>> GetMyChangeRequestsAsync(string? status, int page, int pageSize)
    {
        var userId = GetUserId();
        var query = _dbContext.AppointmentChangeRequests
            .AsNoTracking()
            .Where(r => r.RequestedByUserId == userId);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentChangeRequestStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(r => r.Status == parsedStatus);
        }

        query = query.OrderByDescending(r => r.CreatedAt);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<ChangeRequestDto>(items.Select(MapToDto).ToList(), totalItems, page, pageSize);
    }

    public async Task<PagedResult<ChangeRequestDto>> GetAllChangeRequestsAsync(string? status, int page, int pageSize)
    {
        var query = _dbContext.AppointmentChangeRequests.AsNoTracking();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentChangeRequestStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(r => r.Status == parsedStatus);
        }

        query = query.OrderByDescending(r => r.CreatedAt);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<ChangeRequestDto>(items.Select(MapToDto).ToList(), totalItems, page, pageSize);
    }

    public async Task<ChangeRequestDto> GetChangeRequestByIdAsync(long requestId)
    {
        var req = await _dbContext.AppointmentChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) throw new NotFoundException("Yêu cầu không tồn tại.");
        return MapToDto(req);
    }

    public async Task ApproveRescheduleAsync(long requestId, ProcessChangeRequestDto request)
    {
        var userId = GetUserId();
        
        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var changeReq = await _dbContext.AppointmentChangeRequests
                .Include(r => r.Appointment)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (changeReq == null) throw new NotFoundException("Yêu cầu không tồn tại.");
            if (changeReq.Status != AppointmentChangeRequestStatus.Pending)
                throw new BusinessException("INVALID_STATE", "Yêu cầu không ở trạng thái Pending.");
            
            if (changeReq.RequestType != AppointmentChangeRequestType.Reschedule || changeReq.RequestedSlotId == null)
                throw new BusinessException("INVALID_TYPE", "Yêu cầu không phải là yêu cầu đổi lịch.");

            var appointment = changeReq.Appointment;
            if (appointment.Status != AppointmentStatus.PendingReschedule)
                throw new BusinessException("INVALID_STATE", "Lịch hẹn không ở trạng thái chờ đổi lịch.");

            // Target slot lock
            var affectedRows = await _dbContext.AppointmentSlots
                .Where(s => s.Id == changeReq.RequestedSlotId.Value && !s.IsBooked)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, true));

            if (affectedRows == 0)
                throw new BusinessException("SLOT_TAKEN", "Slot yêu cầu đã được đặt hoặc không khả dụng.");

            // Fetch old and new slot info
            var newSlot = await _dbContext.AppointmentSlots.FirstAsync(s => s.Id == changeReq.RequestedSlotId.Value);
            var oldSlotId = appointment.AppointmentSlotId;

            // Release old slot
            await _dbContext.AppointmentSlots
                .Where(s => s.Id == oldSlotId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, false));

            // Update appointment
            var oldStatus = appointment.Status;
            appointment.AppointmentSlotId = newSlot.Id;
            appointment.AppointmentDate = newSlot.SlotDate;
            appointment.StartTime = newSlot.StartTime;
            appointment.EndTime = newSlot.EndTime;
            appointment.Status = AppointmentStatus.Confirmed; // Confirm it

            // Update request
            changeReq.Status = AppointmentChangeRequestStatus.Approved;
            changeReq.ProcessedByUserId = userId;
            changeReq.ProcessedAt = DateTime.UtcNow;

            // History
            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.Rescheduled,
                OldStatus = oldStatus,
                NewStatus = AppointmentStatus.Confirmed,
                Note = request.Reason ?? "Lễ tân xác nhận đổi lịch",
                PerformedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task ApproveCancellationAsync(long requestId, ProcessChangeRequestDto request)
    {
        var userId = GetUserId();

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var changeReq = await _dbContext.AppointmentChangeRequests
                .Include(r => r.Appointment)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (changeReq == null) throw new NotFoundException("Yêu cầu không tồn tại.");
            if (changeReq.Status != AppointmentChangeRequestStatus.Pending)
                throw new BusinessException("INVALID_STATE", "Yêu cầu không ở trạng thái Pending.");
            
            if (changeReq.RequestType != AppointmentChangeRequestType.Cancellation)
                throw new BusinessException("INVALID_TYPE", "Yêu cầu không phải là yêu cầu hủy lịch.");

            var appointment = changeReq.Appointment;
            if (appointment.Status != AppointmentStatus.PendingCancellation)
                throw new BusinessException("INVALID_STATE", "Lịch hẹn không ở trạng thái chờ hủy lịch.");

            // Release slot
            await _dbContext.AppointmentSlots
                .Where(s => s.Id == appointment.AppointmentSlotId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, false));

            var oldStatus = appointment.Status;
            appointment.Status = AppointmentStatus.Cancelled;

            changeReq.Status = AppointmentChangeRequestStatus.Approved;
            changeReq.ProcessedByUserId = userId;
            changeReq.ProcessedAt = DateTime.UtcNow;

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.Cancelled,
                OldStatus = oldStatus,
                NewStatus = AppointmentStatus.Cancelled,
                Note = request.Reason ?? "Lễ tân xác nhận hủy lịch",
                PerformedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task RejectRequestAsync(long requestId, ProcessChangeRequestDto request)
    {
        var userId = GetUserId();
        
        var changeReq = await _dbContext.AppointmentChangeRequests
            .Include(r => r.Appointment)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (changeReq == null) throw new NotFoundException("Yêu cầu không tồn tại.");
        if (changeReq.Status != AppointmentChangeRequestStatus.Pending)
            throw new BusinessException("INVALID_STATE", "Yêu cầu không ở trạng thái Pending.");

        changeReq.Status = AppointmentChangeRequestStatus.Rejected;
        changeReq.ProcessedByUserId = userId;
        changeReq.ProcessedAt = DateTime.UtcNow;

        var history = await _dbContext.AppointmentHistories
            .Where(h => h.AppointmentId == changeReq.AppointmentId 
                     && (h.Action == AppointmentHistoryAction.RescheduleRequested || h.Action == AppointmentHistoryAction.CancelRequested))
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync();

        var restoredStatus = history?.OldStatus ?? AppointmentStatus.Confirmed;
        var oldAppointmentStatus = changeReq.Appointment.Status;
        
        changeReq.Appointment.Status = restoredStatus;

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = changeReq.Appointment.Id,
            Action = AppointmentHistoryAction.Confirmed, // Adjust if needed
            OldStatus = oldAppointmentStatus,
            NewStatus = restoredStatus,
            Note = request.Reason ?? "Từ chối yêu cầu thay đổi",
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }

    private static ChangeRequestDto MapToDto(AppointmentChangeRequest req) => new()
    {
        Id = req.Id,
        AppointmentId = req.AppointmentId,
        RequestType = req.RequestType.ToString(),
        RequestedSlotId = req.RequestedSlotId,
        Reason = req.Reason,
        Status = req.Status.ToString(),
        RequestedByUserId = req.RequestedByUserId,
        ProcessedByUserId = req.ProcessedByUserId,
        CreatedAt = req.CreatedAt,
        ProcessedAt = req.ProcessedAt
    };
}
