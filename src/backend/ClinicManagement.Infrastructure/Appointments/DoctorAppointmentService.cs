using System;
using System.Collections.Generic;
using ClinicManagement.Application.Appointments.DTOs;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
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

public class DoctorAppointmentService : IDoctorAppointmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public DoctorAppointmentService(AppDbContext dbContext, ICurrentUserService currentUserService)
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

    private async Task<Doctor> GetCurrentDoctorAsync()
    {
        var userId = GetUserId();
        var doctor = await _dbContext.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
        if (doctor == null) throw new NotFoundException("Hồ sơ bác sĩ không tồn tại.");
        return doctor;
    }

    public async Task<PagedResult<DoctorAppointmentDto>> GetMyAppointmentsAsync(string? status, string? search, int page, int pageSize)
    {
        var doctor = await GetCurrentDoctorAsync();

        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals pu.Id
                    where a.DoctorId == doctor.Id
                    select new
                    {
                        Appointment = a,
                        PatientName = pu.FullName,
                        PatientPhone = pu.PhoneNumber,
                        PatientGender = p.Gender,
                        PatientDob = p.DateOfBirth
                    };

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Appointment.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(x => x.Appointment.AppointmentCode.Contains(search) 
                                  || x.PatientName.Contains(search));
        }

        query = query.OrderBy(x => x.Appointment.AppointmentDate).ThenBy(x => x.Appointment.StartTime);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var resultItems = items.Select(x => MapToDto(x.Appointment, x.PatientName, x.PatientPhone, x.PatientGender, x.PatientDob)).ToList();

        return new PagedResult<DoctorAppointmentDto>(resultItems, totalItems, page, pageSize);
    }

    public async Task<DoctorAppointmentDto> GetAppointmentByIdAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals pu.Id
                    where a.Id == appointmentId && a.DoctorId == doctor.Id
                    select new
                    {
                        Appointment = a,
                        PatientName = pu.FullName,
                        PatientPhone = pu.PhoneNumber,
                        PatientGender = p.Gender,
                        PatientDob = p.DateOfBirth
                    };

        var item = await query.FirstOrDefaultAsync();
        if (item == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        return MapToDto(item.Appointment, item.PatientName, item.PatientPhone, item.PatientGender, item.PatientDob);
    }

    public async Task<List<AppointmentHistoryDto>> GetAppointmentHistoryAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var appointmentExists = await _dbContext.Appointments
            .AnyAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (!appointmentExists)
            throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

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

    public async Task CompleteAppointmentAsync(long appointmentId, CompleteAppointmentDto request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var appointment = await _dbContext.Appointments
                .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

            if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

            if (appointment.Status != AppointmentStatus.Confirmed)
                throw new BusinessException("INVALID_STATE", "Chỉ có thể hoàn thành lịch hẹn ở trạng thái Confirmed.");

            var currentDateTime = DateTime.UtcNow; // Note: In real app, consider timezone for the clinic
            var appointmentDateTimeUtc = appointment.AppointmentDate.ToDateTime(appointment.StartTime, DateTimeKind.Utc);
            
            // To allow completion, it should theoretically be past or near the start time.
            // But we will be lenient and assume if it's confirmed, they can complete it during the visit.
            
            var oldStatus = appointment.Status;
            appointment.Status = AppointmentStatus.Completed;

            // Ensure no duplicate summary
            var existingSummary = await _dbContext.VisitSummaries.AnyAsync(v => v.AppointmentId == appointment.Id);
            if (!existingSummary)
            {
                _dbContext.VisitSummaries.Add(new VisitSummary
                {
                    AppointmentId = appointment.Id,
                    DoctorId = doctor.Id,
                    Summary = request.Summary,
                    FollowUpInstruction = request.FollowUpInstruction
                });
            }

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.Completed,
                OldStatus = oldStatus,
                NewStatus = AppointmentStatus.Completed,
                Note = "Bác sĩ hoàn thành khám",
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

    public async Task MarkNoShowAsync(long appointmentId, NoShowAppointmentDto request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var appointment = await _dbContext.Appointments
                .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

            if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

            if (appointment.Status != AppointmentStatus.Confirmed)
                throw new BusinessException("INVALID_STATE", "Chỉ có thể đánh dấu NoShow lịch hẹn ở trạng thái Confirmed.");

            // Check if time has passed
            // Simplification: Clinic timezone usually means UTC+7, but comparing Date+Time strictly
            var appointmentDateTime = appointment.AppointmentDate.ToDateTime(appointment.StartTime);
            if (appointmentDateTime > DateTime.Now)
                throw new BusinessException("INVALID_TIME", "Chưa đến thời gian khám, không thể đánh dấu vắng mặt.");

            var oldStatus = appointment.Status;
            appointment.Status = AppointmentStatus.NoShow;

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.NoShow,
                OldStatus = oldStatus,
                NewStatus = AppointmentStatus.NoShow,
                Note = request.Reason ?? "Bệnh nhân không đến khám",
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

    public async Task<RevisitRequestDto> CreateRevisitRequestAsync(long appointmentId, CreateRevisitRequestDto request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var appointment = await _dbContext.Appointments
                .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

            if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

            if (appointment.Status != AppointmentStatus.Completed)
                throw new BusinessException("INVALID_STATE", "Chỉ có thể tạo đề xuất tái khám cho lịch hẹn đã hoàn thành.");

            var existingPending = await _dbContext.RevisitRequests
                .AnyAsync(r => r.AppointmentId == appointment.Id && r.Status == RevisitRequestStatus.PendingPatientResponse);
            if (existingPending)
                throw new BusinessException("REQUEST_EXISTS", "Đã có đề xuất tái khám đang chờ phản hồi cho lịch hẹn này.");

            var revisitReq = new RevisitRequest
            {
                AppointmentId = appointment.Id,
                PatientId = appointment.PatientId,
                DoctorId = doctor.Id,
                SuggestedDate = request.SuggestedDate,
                Note = request.Note,
                Status = RevisitRequestStatus.PendingPatientResponse
            };

            _dbContext.RevisitRequests.Add(revisitReq);

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.RevisitCreated,
                OldStatus = appointment.Status,
                NewStatus = appointment.Status,
                Note = $"Bác sĩ tạo đề xuất tái khám ngày {request.SuggestedDate:yyyy-MM-dd}",
                PerformedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new RevisitRequestDto
            {
                Id = revisitReq.Id,
                AppointmentId = revisitReq.AppointmentId,
                PatientId = revisitReq.PatientId,
                DoctorId = revisitReq.DoctorId,
                SuggestedDate = revisitReq.SuggestedDate,
                Note = revisitReq.Note,
                Status = revisitReq.Status.ToString()
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static DoctorAppointmentDto MapToDto(Appointment a, string patientName, string patientPhone, Gender? gender, DateOnly? dob) => new()
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
        PatientGender = gender?.ToString() ?? string.Empty,
        PatientDob = dob
    };
}
