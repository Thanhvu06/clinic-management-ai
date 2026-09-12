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
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Appointments;

public class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAppointmentAvailabilityPolicy _availabilityPolicy;

    public AppointmentService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        IAppointmentAvailabilityPolicy availabilityPolicy)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _availabilityPolicy = availabilityPolicy;
    }

    public async Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest request)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
            throw new UnauthorizedAccessException("Bạn cần đăng nhập để đặt lịch khám.");

        var normalizedReason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length < 10 || normalizedReason.Length > 500)
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

        // Idempotency check: If same patient already holds this slot with an active appointment, return it immediately
        var initialExisting = await _dbContext.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor)
            .Include(a => a.Specialty)
            .Where(a => a.PatientId == patient.Id 
                     && a.AppointmentSlotId == request.AppointmentSlotId
                     && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
            .FirstOrDefaultAsync();

        if (initialExisting != null)
        {
            var initialDoctorName = "Bác sĩ";
            if (initialExisting.Doctor != null)
            {
                var docUser = await _dbContext.Users.FindAsync(initialExisting.Doctor.UserId);
                if (docUser != null) initialDoctorName = docUser.FullName;
            }

            return new AppointmentDto
            {
                Id = initialExisting.Id,
                AppointmentCode = initialExisting.AppointmentCode,
                PatientId = initialExisting.PatientId,
                DoctorId = initialExisting.DoctorId,
                DoctorName = initialDoctorName,
                SpecialtyId = initialExisting.SpecialtyId,
                SpecialtyName = initialExisting.Specialty?.Name ?? "Chuyên khoa",
                AppointmentSlotId = initialExisting.AppointmentSlotId,
                AppointmentDate = initialExisting.AppointmentDate,
                StartTime = initialExisting.StartTime,
                EndTime = initialExisting.EndTime,
                Reason = initialExisting.Reason,
                Status = initialExisting.Status.ToString()
            };
        }

        // 4. Canonical Availability Policy Evaluation
        var availResult = await _availabilityPolicy.EvaluateSlotAvailabilityAsync(new SlotAvailabilityRequest
        {
            SlotId = request.AppointmentSlotId,
            DoctorId = request.DoctorId,
            SpecialtyId = request.SpecialtyId,
            PatientId = patient.Id,
            CheckAiEnabledSpecialty = false
        });

        if (!availResult.IsAvailable)
        {
            if (availResult.ReasonCode == "SLOT_NOT_FOUND")
                throw new NotFoundException(availResult.FailureReason ?? "Slot không tồn tại.");
            if (availResult.ReasonCode == "SLOT_ALREADY_BOOKED")
                throw new ConflictException("SLOT_ALREADY_BOOKED", availResult.FailureReason ?? "Slot đã được đặt.");
            if (availResult.ReasonCode == "PATIENT_TIME_CONFLICT")
                throw new BusinessException("PATIENT_TIME_CONFLICT", availResult.FailureReason ?? "Trùng thời gian khám.");
            if (availResult.ReasonCode == "SPECIALTY_NOT_AVAILABLE")
                throw new BusinessException("SPECIALTY_NOT_AVAILABLE", availResult.FailureReason ?? "Chuyên khoa không khả dụng.");
            if (availResult.ReasonCode == "DOCTOR_NOT_AVAILABLE" || availResult.ReasonCode == "SUNDAY_CLOSED" 
                || availResult.ReasonCode == "DOCTOR_NOT_SCHEDULED" || availResult.ReasonCode == "DOCTOR_ON_LEAVE")
                throw new BusinessException("DOCTOR_NOT_AVAILABLE", availResult.FailureReason ?? "Bác sĩ không khả dụng.");

            throw new BusinessException("VALIDATION_ERROR", availResult.FailureReason ?? "Thông tin đặt lịch không hợp lệ.");
        }

        var doctorName = availResult.DoctorName ?? "Bác sĩ";
        var specialtyName = availResult.SpecialtyName ?? "Chuyên khoa";

        // TRANSACTION: Serializable to prevent overlapping inserts
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            // Idempotency check: If same patient already has an active appointment for this slot, return it
            var existingAppointment = await _dbContext.Appointments
                .Where(a => a.PatientId == patient.Id 
                         && a.AppointmentSlotId == request.AppointmentSlotId
                         && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
                .FirstOrDefaultAsync();

            if (existingAppointment != null)
            {
                await transaction.RollbackAsync();
                return new AppointmentDto
                {
                    Id = existingAppointment.Id,
                    AppointmentCode = existingAppointment.AppointmentCode,
                    PatientId = existingAppointment.PatientId,
                    DoctorId = existingAppointment.DoctorId,
                    DoctorName = doctorName,
                    SpecialtyId = existingAppointment.SpecialtyId,
                    SpecialtyName = specialtyName,
                    AppointmentSlotId = existingAppointment.AppointmentSlotId,
                    AppointmentDate = existingAppointment.AppointmentDate,
                    StartTime = existingAppointment.StartTime,
                    EndTime = existingAppointment.EndTime,
                    Reason = existingAppointment.Reason,
                    Status = existingAppointment.Status.ToString()
                };
            }

            // 11. Lock and update slot atomically
            var affectedRows = await _dbContext.AppointmentSlots
                .Where(s => s.Id == request.AppointmentSlotId && !s.IsBooked)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, true));

            if (affectedRows == 0)
            {
                // Rollback current transaction first to break snapshot isolation
                await transaction.RollbackAsync();

                // Check if the slot was booked by the SAME patient in another concurrent request
                var samePatientAppointment = await _dbContext.Appointments
                    .AsNoTracking()
                    .Where(a => a.PatientId == patient.Id 
                             && a.AppointmentSlotId == request.AppointmentSlotId
                             && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
                    .FirstOrDefaultAsync();

                if (samePatientAppointment != null)
                {
                    return new AppointmentDto
                    {
                        Id = samePatientAppointment.Id,
                        AppointmentCode = samePatientAppointment.AppointmentCode,
                        PatientId = samePatientAppointment.PatientId,
                        DoctorId = samePatientAppointment.DoctorId,
                        DoctorName = doctorName,
                        SpecialtyId = samePatientAppointment.SpecialtyId,
                        SpecialtyName = specialtyName,
                        AppointmentSlotId = samePatientAppointment.AppointmentSlotId,
                        AppointmentDate = samePatientAppointment.AppointmentDate,
                        StartTime = samePatientAppointment.StartTime,
                        EndTime = samePatientAppointment.EndTime,
                        Reason = samePatientAppointment.Reason,
                        Status = samePatientAppointment.Status.ToString()
                    };
                }

                throw new ConflictException("SLOT_ALREADY_BOOKED", "Slot đã được đặt hoặc không khả dụng.");
            }

            // 12. Check overlap for Patient using the canonical slot-holding policy
            var slot = await _dbContext.AppointmentSlots.FindAsync(request.AppointmentSlotId)
                ?? throw new NotFoundException("Slot không tồn tại.");
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
                    Title = "Lịch khám mới chờ tiếp nhận",
                    Message = $"Bệnh nhân đã đặt lịch khám #{appointment.AppointmentCode} ngày {appointment.AppointmentDate:dd/MM/yyyy} lúc {appointment.StartTime:HH\\:mm}.",
                    Route = $"/doctor/appointments/{appointment.Id}",
                    RelatedEntityType = "Appointment",
                    RelatedEntityId = appointment.Id.ToString(),
                    DedupeKey = $"appt_booked_doc_{appointment.Id}_{doctorUserId}",
                    IsRead = false,
                    CreatedAtUtc = DateTime.UtcNow
                });
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
                SpecialtyName = specialtyName,
                AppointmentSlotId = appointment.AppointmentSlotId,
                AppointmentDate = appointment.AppointmentDate,
                StartTime = appointment.StartTime,
                EndTime = appointment.EndTime,
                Reason = appointment.Reason,
                Status = appointment.Status.ToString()
            };
        }
        catch (Exception ex) when (IsConcurrencyOrConflictException(ex))
        {
            if (transaction != null)
            {
                try { await transaction.RollbackAsync(); } catch { }
            }

            // Outside transaction, check if this patient already holds this slot (idempotency)
            var samePatientAppointment = await _dbContext.Appointments
                .AsNoTracking()
                .Where(a => a.PatientId == patient.Id 
                         && a.AppointmentSlotId == request.AppointmentSlotId
                         && AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
                .FirstOrDefaultAsync();

            if (samePatientAppointment != null)
            {
                return new AppointmentDto
                {
                    Id = samePatientAppointment.Id,
                    AppointmentCode = samePatientAppointment.AppointmentCode,
                    PatientId = samePatientAppointment.PatientId,
                    DoctorId = samePatientAppointment.DoctorId,
                    DoctorName = doctorName,
                    SpecialtyId = samePatientAppointment.SpecialtyId,
                    SpecialtyName = specialtyName,
                    AppointmentSlotId = samePatientAppointment.AppointmentSlotId,
                    AppointmentDate = samePatientAppointment.AppointmentDate,
                    StartTime = samePatientAppointment.StartTime,
                    EndTime = samePatientAppointment.EndTime,
                    Reason = samePatientAppointment.Reason,
                    Status = samePatientAppointment.Status.ToString()
                };
            }

            throw new ConflictException("SLOT_ALREADY_BOOKED", "Slot đã được đặt hoặc đang có giao dịch xử lý đồng thời.");
        }
        catch
        {
            if (transaction != null)
            {
                try { await transaction.RollbackAsync(); } catch { }
            }
            throw;
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
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

    private static bool IsConcurrencyOrConflictException(Exception ex)
    {
        if (ex is ConflictException || ex is BusinessException || ex is NotFoundException)
            return false;

        var curr = ex;
        while (curr != null)
        {
            if (curr is DbUpdateConcurrencyException)
                return true;

            var typeName = curr.GetType().FullName ?? string.Empty;
            if (typeName.Contains("SqliteException", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("SqlException", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var msg = curr.Message;
            if (msg.Contains("concurrency", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("busy", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("snapshot", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("unique constraint", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (curr is DbUpdateException)
            {
                return true;
            }

            curr = curr.InnerException;
        }

        return false;
    }
}
