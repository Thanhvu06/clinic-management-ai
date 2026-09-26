using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Leaves.DTOs;
using ClinicManagement.Application.Leaves.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Leaves;

public class DoctorLeaveService : IDoctorLeaveService
{
    private readonly AppDbContext _dbContext;
    private readonly IDoctorContextService _doctorContextService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DoctorLeaveService(
        AppDbContext dbContext, 
        IDoctorContextService doctorContextService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _doctorContextService = doctorContextService;
        _dateTimeProvider = dateTimeProvider;
    }

    private Task<Doctor> GetCurrentDoctorAsync()
    {
        return _doctorContextService.GetCurrentActiveDoctorAsync();
    }

    public async Task<PagedResult<LeaveRequestDto>> GetMyLeaveRequestsAsync(string? status, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var doctor = await GetCurrentDoctorAsync();

        var query = from l in _dbContext.DoctorLeaveRequests
                    join d in _dbContext.Doctors on l.DoctorId equals d.Id
                    join u in _dbContext.Users on d.UserId equals u.Id
                    where l.DoctorId == doctor.Id
                    select new { l, u.FullName };

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
        var doctor = await GetCurrentDoctorAsync();

        var item = await (from l in _dbContext.DoctorLeaveRequests
                          join d in _dbContext.Doctors on l.DoctorId equals d.Id
                          join u in _dbContext.Users on d.UserId equals u.Id
                          where l.Id == id && l.DoctorId == doctor.Id
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

    public async Task<LeavePreviewDto> PreviewLeaveAffectedAppointmentsAsync(DateTime start, DateTime end)
    {
        if (end <= start)
            throw new BusinessException("INVALID_TIME", "Thời gian kết thúc phải sau thời gian bắt đầu.");

        if ((end - start).TotalDays > 366)
            throw new BusinessException("DATE_RANGE_TOO_LARGE", "Khoảng xem trước lịch nghỉ không được vượt quá 366 ngày.");

        var doctor = await GetCurrentDoctorAsync();
        
        var startDateOnly = DateOnly.FromDateTime(start.Date);
        var endDateOnly = DateOnly.FromDateTime(end.Date);

        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join u in _dbContext.Users on p.UserId equals u.Id
                    where a.DoctorId == doctor.Id
                       && a.AppointmentDate >= startDateOnly
                       && a.AppointmentDate <= endDateOnly
                       && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)
                    select new
                    {
                        a.Id,
                        a.AppointmentCode,
                        a.AppointmentDate,
                        a.StartTime,
                        a.EndTime,
                        PatientName = u.FullName,
                        Status = a.Status.ToString()
                    };

        var list = await query.ToListAsync();

        var affected = list.Where(x =>
        {
            var apptStart = x.AppointmentDate.ToDateTime(x.StartTime);
            var apptEnd = x.AppointmentDate.ToDateTime(x.EndTime);
            return apptStart < end && apptEnd > start;
        }).Select(x => new AffectedAppointmentDto
        {
            Id = x.Id,
            AppointmentCode = x.AppointmentCode,
            Date = x.AppointmentDate,
            StartTime = x.StartTime,
            EndTime = x.EndTime,
            PatientName = x.PatientName,
            Status = x.Status
        }).ToList();

        return new LeavePreviewDto
        {
            StartDateTime = start,
            EndDateTime = end,
            AffectedAppointmentsCount = affected.Count,
            AffectedAppointments = affected
        };
    }

    public async Task<LeaveRequestDto> CreateLeaveRequestAsync(CreateLeaveRequestDto request)
    {
        var doctor = await GetCurrentDoctorAsync();

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("INVALID_REASON", "Lý do nghỉ không được để trống.");

        if (request.StartDateTime >= request.EndDateTime)
            throw new BusinessException("INVALID_TIME", "Thời gian bắt đầu phải trước thời gian kết thúc.");

        if (request.StartDateTime < _dateTimeProvider.VietnamNow)
            throw new BusinessException("INVALID_TIME", "Không thể tạo yêu cầu nghỉ trong quá khứ.");

        var overlap = await _dbContext.DoctorLeaveRequests.AnyAsync(l => 
            l.DoctorId == doctor.Id &&
            (l.Status == DoctorLeaveRequestStatus.Pending || l.Status == DoctorLeaveRequestStatus.Approved) &&
            request.StartDateTime < l.EndDateTime && request.EndDateTime > l.StartDateTime);

        if (overlap)
            throw new BusinessException("LEAVE_OVERLAP", "Thời gian nghỉ bị trùng lặp với một yêu cầu khác đang chờ duyệt hoặc đã duyệt.");

        var leave = new DoctorLeaveRequest
        {
            DoctorId = doctor.Id,
            StartDateTime = request.StartDateTime,
            EndDateTime = request.EndDateTime,
            Reason = reason,
            Status = DoctorLeaveRequestStatus.Pending
        };

        _dbContext.DoctorLeaveRequests.Add(leave);
        await _dbContext.SaveChangesAsync();

        var user = await _dbContext.Users.FirstAsync(u => u.Id == doctor.UserId);

        return new LeaveRequestDto
        {
            Id = leave.Id,
            DoctorId = leave.DoctorId,
            DoctorName = user.FullName,
            StartDateTime = leave.StartDateTime,
            EndDateTime = leave.EndDateTime,
            Reason = leave.Reason,
            Status = leave.Status.ToString()
        };
    }

    public async Task WithdrawLeaveRequestAsync(long id)
    {
        var doctor = await GetCurrentDoctorAsync();

        var leave = await _dbContext.DoctorLeaveRequests.FirstOrDefaultAsync(l => l.Id == id && l.DoctorId == doctor.Id);
        if (leave == null) throw new NotFoundException("Yêu cầu không tồn tại.");

        if (leave.Status != DoctorLeaveRequestStatus.Pending)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể rút yêu cầu đang chờ duyệt.");

        leave.Status = DoctorLeaveRequestStatus.Cancelled;
        await _dbContext.SaveChangesAsync();
    }
}
