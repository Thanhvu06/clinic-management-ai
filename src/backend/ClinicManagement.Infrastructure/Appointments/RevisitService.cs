using System;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Revisit;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
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

    public RevisitService(AppDbContext dbContext, ICurrentUserService currentUserService)
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
                        SpecialtyName = s.Name
                    };

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RevisitRequestStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Request.Status == parsedStatus);
        }

        query = query.OrderByDescending(x => x.Request.SuggestedDate);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var resultItems = items.Select(x => MapToDto(x.Request, x.DoctorName, x.SpecialtyName)).ToList();

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
                        SpecialtyName = s.Name
                    };

        var item = await query.FirstOrDefaultAsync();
        if (item == null) throw new NotFoundException("Đề xuất tái khám không tồn tại.");

        return MapToDto(item.Request, item.DoctorName, item.SpecialtyName);
    }

    public async Task AcceptRevisitRequestAsync(long id, AcceptRevisitRequestDto request)
    {
        var patient = await GetCurrentPatientAsync();
        var userId = GetUserId();

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var revisitReq = await _dbContext.RevisitRequests
                .Include(r => r.OriginalAppointment)
                .FirstOrDefaultAsync(r => r.Id == id && r.PatientId == patient.Id);

            if (revisitReq == null) throw new NotFoundException("Đề xuất tái khám không tồn tại.");
            
            if (revisitReq.Status != RevisitRequestStatus.PendingPatientResponse)
                throw new BusinessException("INVALID_STATE", "Chỉ có thể chấp nhận đề xuất đang chờ phản hồi.");

            var targetSlot = await _dbContext.AppointmentSlots
                .FirstOrDefaultAsync(s => s.Id == request.TargetSlotId);
            
            if (targetSlot == null) throw new NotFoundException("Slot yêu cầu không tồn tại.");

            if (targetSlot.DoctorId != revisitReq.DoctorId)
                throw new BusinessException("INVALID_TARGET", "Slot phải thuộc về bác sĩ đề xuất tái khám.");

            var slotStart = targetSlot.SlotDate.ToDateTime(targetSlot.StartTime);
            if (slotStart <= DateTime.UtcNow)
                throw new BusinessException("INVALID_TARGET", "Slot yêu cầu phải ở trong tương lai.");

            var affectedRows = await _dbContext.AppointmentSlots
                .Where(s => s.Id == targetSlot.Id && !s.IsBooked)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, true));

            if (affectedRows == 0)
                throw new BusinessException("SLOT_TAKEN", "Slot yêu cầu đã được đặt hoặc không khả dụng.");

            var newAppointment = new Appointment
            {
                AppointmentCode = "APT-" + DateTime.Now.ToString("yyMMdd") + "-" + Guid.NewGuid().ToString().Substring(0, 4).ToUpper(),
                PatientId = patient.Id,
                DoctorId = targetSlot.DoctorId,
                SpecialtyId = revisitReq.OriginalAppointment.SpecialtyId,
                AppointmentSlotId = targetSlot.Id,
                AppointmentDate = targetSlot.SlotDate,
                StartTime = targetSlot.StartTime,
                EndTime = targetSlot.EndTime,
                Reason = request.Reason ?? "Tái khám theo đề xuất của bác sĩ",
                Status = AppointmentStatus.Pending
            };

            _dbContext.Appointments.Add(newAppointment);
            await _dbContext.SaveChangesAsync(); // Save to generate ID

            revisitReq.Status = RevisitRequestStatus.Accepted;
            revisitReq.NewAppointmentId = newAppointment.Id;

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = newAppointment.Id,
                Action = AppointmentHistoryAction.Created,
                OldStatus = null,
                NewStatus = AppointmentStatus.Pending,
                Note = "Bệnh nhân đặt lịch tái khám",
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

    private static RevisitRequestDto MapToDto(RevisitRequest r, string doctorName, string specialtyName) => new()
    {
        Id = r.Id,
        AppointmentId = r.AppointmentId,
        PatientId = r.PatientId,
        DoctorId = r.DoctorId,
        SuggestedDate = r.SuggestedDate,
        Note = r.Note,
        Status = r.Status.ToString(),
        NewAppointmentId = r.NewAppointmentId,
        DoctorName = doctorName ?? string.Empty,
        SpecialtyName = specialtyName ?? string.Empty
    };
}
