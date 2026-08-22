using System;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Leaves.DTOs;
using ClinicManagement.Application.Leaves.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Leaves;

public class AdminLeaveService : IAdminLeaveService
{
    private readonly AppDbContext _dbContext;

    public AdminLeaveService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<LeaveRequestDto>> GetLeaveRequestsAsync(long? doctorId, string? status, int page, int pageSize)
    {
        var query = from l in _dbContext.DoctorLeaveRequests
                    join d in _dbContext.Doctors on l.DoctorId equals d.Id
                    join u in _dbContext.Users on d.UserId equals u.Id
                    select new { l, u.FullName };

        if (doctorId.HasValue)
        {
            query = query.Where(x => x.l.DoctorId == doctorId.Value);
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<DoctorLeaveRequestStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.l.Status == parsedStatus);
        }

        query = query.OrderByDescending(x => x.l.StartDateTime);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var dtos = items.Select(x => new LeaveRequestDto
        {
            Id = x.l.Id,
            DoctorId = x.l.DoctorId,
            DoctorName = x.FullName,
            StartDateTime = x.l.StartDateTime,
            EndDateTime = x.l.EndDateTime,
            Reason = x.l.Reason,
            Status = x.l.Status.ToString(),
            AdminNote = x.l.AdminNote
        }).ToList();

        return new PagedResult<LeaveRequestDto>(dtos, totalItems, page, pageSize);
    }

    public async Task<LeaveRequestDto> GetLeaveRequestByIdAsync(long id)
    {
        var item = await (from l in _dbContext.DoctorLeaveRequests
                          join d in _dbContext.Doctors on l.DoctorId equals d.Id
                          join u in _dbContext.Users on d.UserId equals u.Id
                          where l.Id == id
                          select new { l, u.FullName }).FirstOrDefaultAsync();

        if (item == null) throw new NotFoundException("Yêu cầu không tồn tại.");

        return new LeaveRequestDto
        {
            Id = item.l.Id,
            DoctorId = item.l.DoctorId,
            DoctorName = item.FullName,
            StartDateTime = item.l.StartDateTime,
            EndDateTime = item.l.EndDateTime,
            Reason = item.l.Reason,
            Status = item.l.Status.ToString(),
            AdminNote = item.l.AdminNote
        };
    }

    public async Task ApproveLeaveRequestAsync(long id, AdminProcessLeaveRequestDto request)
    {
        // Using Serializable isolation to avoid race conditions with Appointment creation
        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var leave = await _dbContext.DoctorLeaveRequests.FirstOrDefaultAsync(l => l.Id == id);
            if (leave == null) throw new NotFoundException("Yêu cầu không tồn tại.");

            if (leave.Status != DoctorLeaveRequestStatus.Pending)
                throw new BusinessException("INVALID_STATE", "Chỉ có thể duyệt yêu cầu đang chờ duyệt.");

            if (leave.StartDateTime < DateTime.Now)
                throw new BusinessException("INVALID_TIME", "Thời gian bắt đầu đã qua, không thể duyệt.");

            var overlap = await _dbContext.DoctorLeaveRequests.AnyAsync(l => 
                l.Id != leave.Id &&
                l.DoctorId == leave.DoctorId &&
                l.Status == DoctorLeaveRequestStatus.Approved &&
                leave.StartDateTime < l.EndDateTime && leave.EndDateTime > l.StartDateTime);

            if (overlap)
                throw new BusinessException("LEAVE_OVERLAP", "Bác sĩ đã có lịch nghỉ được duyệt trong thời gian này.");

            // Check affected active appointments
            var activeStatuses = new[] 
            { 
                AppointmentStatus.Pending, 
                AppointmentStatus.Confirmed, 
                AppointmentStatus.PendingReschedule, 
                AppointmentStatus.PendingCancellation 
            };

            var affectedAppointments = await _dbContext.Appointments
                .Where(a => a.DoctorId == leave.DoctorId 
                         && activeStatuses.Contains(a.Status)
                         && a.AppointmentDate >= DateOnly.FromDateTime(leave.StartDateTime.Date)
                         && a.AppointmentDate <= DateOnly.FromDateTime(leave.EndDateTime.Date))
                .ToListAsync();

            // Refine in memory or DB based on Date+Time
            var strictlyAffected = affectedAppointments.Where(a => 
            {
                var start = a.AppointmentDate.ToDateTime(a.StartTime);
                var end = a.AppointmentDate.ToDateTime(a.EndTime);
                return start < leave.EndDateTime && end > leave.StartDateTime;
            }).Any();

            if (strictlyAffected)
            {
                throw new BusinessException("LEAVE_HAS_AFFECTED_APPOINTMENTS", "Lịch nghỉ trùng với các lịch hẹn chưa hoàn thành. Cần giải quyết lịch hẹn trước khi duyệt.");
            }

            leave.Status = DoctorLeaveRequestStatus.Approved;
            leave.AdminNote = request.AdminNote;

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task RejectLeaveRequestAsync(long id, AdminProcessLeaveRequestDto request)
    {
        var leave = await _dbContext.DoctorLeaveRequests.FirstOrDefaultAsync(l => l.Id == id);
        if (leave == null) throw new NotFoundException("Yêu cầu không tồn tại.");

        if (leave.Status != DoctorLeaveRequestStatus.Pending)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể từ chối yêu cầu đang chờ duyệt.");

        leave.Status = DoctorLeaveRequestStatus.Rejected;
        leave.AdminNote = request.AdminNote;

        await _dbContext.SaveChangesAsync();
    }
}
