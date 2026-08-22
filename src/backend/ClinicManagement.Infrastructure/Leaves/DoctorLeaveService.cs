using System;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
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
    private readonly ICurrentUserService _currentUserService;

    public DoctorLeaveService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    private async Task<Doctor> GetCurrentDoctorAsync()
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");

        var doctor = await _dbContext.Doctors.FirstOrDefaultAsync(d => d.UserId == currentUserId.Value);
        if (doctor == null) throw new NotFoundException("Hồ sơ bác sĩ không tồn tại.");

        return doctor;
    }

    public async Task<PagedResult<LeaveRequestDto>> GetMyLeaveRequestsAsync(string? status, int page, int pageSize)
    {
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

    public async Task<LeaveRequestDto> CreateLeaveRequestAsync(CreateLeaveRequestDto request)
    {
        var doctor = await GetCurrentDoctorAsync();

        if (request.StartDateTime >= request.EndDateTime)
            throw new BusinessException("INVALID_TIME", "Thời gian bắt đầu phải trước thời gian kết thúc.");

        if (request.StartDateTime < DateTime.Now)
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
            Reason = request.Reason,
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
