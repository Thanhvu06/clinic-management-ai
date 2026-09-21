using System;
using System.Collections.Generic;
using ClinicManagement.Application.Appointments.DTOs;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Appointments.DTOs.Reception;
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

public class ReceptionService : IReceptionService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IFacilityAuthorizationService _facilityAuthService;

    public ReceptionService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        IFacilityAuthorizationService facilityAuthService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _facilityAuthService = facilityAuthService;
    }

    private Guid GetUserId()
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");
        return currentUserId.Value;
    }

    public async Task<PagedResult<ReceptionAppointmentDto>> GetAppointmentsAsync(string? status, string? tab, long? facilityId, string? search, int page, int pageSize)
    {
        var userId = GetUserId();
        var today = _dateTimeProvider.VietnamToday;

        var isGlobalAdmin = await _facilityAuthService.HasFullFacilityAccessAsync(userId);
        var allowedFacilityIds = await _facilityAuthService.GetUserAccessibleFacilityIdsAsync(userId);

        if (!isGlobalAdmin && allowedFacilityIds.Count == 0)
        {
            return new PagedResult<ReceptionAppointmentDto>(new List<ReceptionAppointmentDto>(), 0, page, pageSize);
        }

        if (facilityId.HasValue && facilityId.Value > 0)
        {
            await _facilityAuthService.ValidateUserFacilityAccessAsync(userId, facilityId.Value);
        }

        var query = from a in _dbContext.Appointments.AsNoTracking()
                    join p in _dbContext.Patients.AsNoTracking() on a.PatientId equals p.Id
                    join pu in _dbContext.Users.AsNoTracking() on p.UserId equals (Guid?)pu.Id into puGroup
                    from pu in puGroup.DefaultIfEmpty()
                    join d in _dbContext.Doctors.AsNoTracking() on a.DoctorId equals d.Id
                    join du in _dbContext.Users.AsNoTracking() on d.UserId equals du.Id into duGroup
                    from du in duGroup.DefaultIfEmpty()
                    join s in _dbContext.Specialties.AsNoTracking() on a.SpecialtyId equals s.Id
                    join pv in _dbContext.PatientVisits.AsNoTracking() on a.Id equals pv.AppointmentId into pvGroup
                    from pv in pvGroup.DefaultIfEmpty()
                    join f in _dbContext.Facilities.AsNoTracking() on pv.FacilityId equals f.Id into fGroup
                    from f in fGroup.DefaultIfEmpty()
                    select new
                    {
                        Appointment = a,
                        PatientName = pu != null ? pu.FullName : (p.FullName ?? string.Empty),
                        PatientPhone = pu != null ? pu.PhoneNumber : (p.PhoneNumber ?? string.Empty),
                        MedicalRecordNumber = p.MedicalRecordNumber ?? string.Empty,
                        NationalId = p.NationalId ?? string.Empty,
                        DoctorId = d.Id,
                        DoctorUserId = d.UserId,
                        DoctorName = du != null ? du.FullName : "Bác sĩ",
                        SpecialtyName = s.Name,
                        PatientVisitId = pv != null ? (long?)pv.Id : null,
                        VisitFacilityId = pv != null ? (long?)pv.FacilityId : null,
                        FacilityId = pv != null ? (long?)pv.FacilityId : _dbContext.StaffFacilityAssignments.Where(sa => sa.UserId == d.UserId && sa.IsActive).Select(sa => (long?)sa.FacilityId).FirstOrDefault(),
                        FacilityName = f != null ? f.Name : _dbContext.StaffFacilityAssignments.Where(sa => sa.UserId == d.UserId && sa.IsActive).Select(sa => sa.Facility.Name).FirstOrDefault()
                    };

        if (facilityId.HasValue && facilityId.Value > 0)
        {
            var facId = facilityId.Value;
            query = query.Where(x => (x.VisitFacilityId.HasValue && x.VisitFacilityId.Value == facId)
                                  || (!x.VisitFacilityId.HasValue && _dbContext.StaffFacilityAssignments.Any(s => s.UserId == x.DoctorUserId && s.IsActive && s.FacilityId == facId)));
        }
        else if (!isGlobalAdmin)
        {
            query = query.Where(x => (x.VisitFacilityId.HasValue && allowedFacilityIds.Contains(x.VisitFacilityId.Value))
                                  || (!x.VisitFacilityId.HasValue && _dbContext.StaffFacilityAssignments.Any(s => s.UserId == x.DoctorUserId && s.IsActive && allowedFacilityIds.Contains(s.FacilityId))));
        }

        var effectiveFilter = (!string.IsNullOrWhiteSpace(tab) ? tab : status)?.Trim().ToLower();
        if (effectiveFilter == "today")
        {
            query = query.Where(x => x.Appointment.AppointmentDate == today && x.Appointment.Status != AppointmentStatus.Cancelled);
        }
        else if (effectiveFilter == "pending")
        {
            query = query.Where(x => x.Appointment.Status == AppointmentStatus.Pending);
        }
        else if (effectiveFilter == "upcoming")
        {
            query = query.Where(x => x.Appointment.AppointmentDate > today && x.Appointment.Status != AppointmentStatus.Cancelled);
        }
        else if (effectiveFilter == "recent")
        {
            var recentDate = today.AddDays(-3);
            query = query.Where(x => x.Appointment.AppointmentDate >= recentDate && x.Appointment.AppointmentDate <= today);
        }
        else if (effectiveFilter == "history")
        {
            query = query.Where(x => x.Appointment.AppointmentDate < today || x.Appointment.Status == AppointmentStatus.Completed || x.Appointment.Status == AppointmentStatus.Cancelled);
        }
        else if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Appointment.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var clean = search.Trim().ToLower();
            query = query.Where(x => x.Appointment.AppointmentCode.ToLower().Contains(clean) 
                                  || x.PatientName.ToLower().Contains(clean) 
                                  || x.PatientPhone.Contains(clean)
                                  || x.MedicalRecordNumber.ToLower().Contains(clean)
                                  || x.NationalId.ToLower().Contains(clean));
        }

        if (effectiveFilter == "history")
        {
            query = query.OrderByDescending(x => x.Appointment.AppointmentDate)
                         .ThenByDescending(x => x.Appointment.StartTime);
        }
        else
        {
            // Actionable today appointments appear FIRST on page 1
            query = query.OrderByDescending(x => x.Appointment.AppointmentDate == today && (x.Appointment.Status == AppointmentStatus.Confirmed || x.Appointment.Status == AppointmentStatus.Pending))
                         .ThenBy(x => x.Appointment.AppointmentDate >= today ? 0 : 1)
                         .ThenBy(x => x.Appointment.AppointmentDate)
                         .ThenBy(x => x.Appointment.StartTime);
        }

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var resultItems = items.Select(x => MapToDto(x.Appointment, x.PatientName, x.PatientPhone, x.MedicalRecordNumber, x.NationalId, x.DoctorName, x.SpecialtyName, x.PatientVisitId, x.FacilityId, x.FacilityName)).ToList();

        return new PagedResult<ReceptionAppointmentDto>(resultItems, totalItems, page, pageSize);
    }

    public async Task<ReceptionAppointmentDto> GetAppointmentByIdAsync(long appointmentId)
    {
        var userId = GetUserId();
        await _facilityAuthService.ValidateAppointmentAccessAsync(userId, appointmentId);

        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals (Guid?)pu.Id into puGroup
                    from pu in puGroup.DefaultIfEmpty()
                    join d in _dbContext.Doctors on a.DoctorId equals d.Id
                    join du in _dbContext.Users on d.UserId equals du.Id into duGroup
                    from du in duGroup.DefaultIfEmpty()
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    join pv in _dbContext.PatientVisits on a.Id equals pv.AppointmentId into pvGroup
                    from pv in pvGroup.DefaultIfEmpty()
                    join f in _dbContext.Facilities on pv.FacilityId equals f.Id into fGroup
                    from f in fGroup.DefaultIfEmpty()
                    where a.Id == appointmentId
                    select new
                    {
                        Appointment = a,
                        PatientName = pu != null ? pu.FullName : (p.FullName ?? string.Empty),
                        PatientPhone = pu != null ? pu.PhoneNumber : (p.PhoneNumber ?? string.Empty),
                        MedicalRecordNumber = p.MedicalRecordNumber ?? string.Empty,
                        NationalId = p.NationalId ?? string.Empty,
                        DoctorName = du != null ? du.FullName : "Bác sĩ",
                        SpecialtyName = s.Name,
                        PatientVisitId = pv != null ? (long?)pv.Id : null,
                        FacilityId = pv != null ? (long?)pv.FacilityId : _dbContext.StaffFacilityAssignments.Where(sa => sa.UserId == d.UserId && sa.IsActive).Select(sa => (long?)sa.FacilityId).FirstOrDefault(),
                        FacilityName = f != null ? f.Name : _dbContext.StaffFacilityAssignments.Where(sa => sa.UserId == d.UserId && sa.IsActive).Select(sa => sa.Facility.Name).FirstOrDefault()
                    };

        var item = await query.FirstOrDefaultAsync();

        if (item == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

        return MapToDto(item.Appointment, item.PatientName, item.PatientPhone, item.MedicalRecordNumber, item.NationalId, item.DoctorName, item.SpecialtyName, item.PatientVisitId, item.FacilityId, item.FacilityName);
    }

    public async Task<List<AppointmentHistoryDto>> GetAppointmentHistoryAsync(long appointmentId)
    {
        var appointmentExists = await _dbContext.Appointments
            .AnyAsync(a => a.Id == appointmentId);

        if (!appointmentExists)
            throw new NotFoundException("Lịch hẹn không tồn tại.");

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

    public async Task ConfirmAppointmentAsync(long appointmentId)
    {
        var userId = GetUserId();

        await _facilityAuthService.ValidateAppointmentAccessAsync(userId, appointmentId);

        var appointment = await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

        if (appointment.Status != AppointmentStatus.Pending)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể xác nhận lịch hẹn ở trạng thái Pending.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.Confirmed;

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.Confirmed,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.Confirmed,
            Note = "Lễ tân xác nhận lịch",
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        // Notify patient
        var patientUserId = await _dbContext.Patients
            .Where(p => p.Id == appointment.PatientId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync();

        if (patientUserId.HasValue && patientUserId.Value != Guid.Empty)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = patientUserId.Value,
                Type = NotificationType.Appointment,
                Title = "Lịch khám đã được xác nhận",
                Message = $"Lịch khám #{appointment.AppointmentCode} ngày {appointment.AppointmentDate:dd/MM/yyyy} lúc {appointment.StartTime:HH\\:mm} đã được tiếp nhận và xác nhận.",
                Route = "/patient/appointments",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"appt_confirmed_pat_{appointment.Id}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
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
                Title = "Lịch khám đã được xác nhận",
                Message = $"Lịch khám #{appointment.AppointmentCode} ngày {appointment.AppointmentDate:dd/MM/yyyy} đã được xác nhận.",
                Route = $"/doctor/appointments/{appointment.Id}",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"appt_confirmed_doc_{appointment.Id}_{doctorUserId}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task CheckInAppointmentAsync(long appointmentId)
    {
        var userId = GetUserId();

        await _facilityAuthService.ValidateAppointmentAccessAsync(userId, appointmentId);

        var appointment = await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại.");

        if (appointment.Status == AppointmentStatus.CheckedIn)
            return;

        if (appointment.Status != AppointmentStatus.Confirmed)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể check-in lịch hẹn ở trạng thái Confirmed.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.CheckedIn;

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.CheckedIn,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.CheckedIn,
            Note = "Bệnh nhân đã có mặt tại phòng khám và check-in vào hàng đợi",
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

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
                Title = "Bệnh nhân đã đến phòng khám",
                Message = $"Bệnh nhân cho lịch khám #{appointment.AppointmentCode} đã có mặt tại phòng chờ.",
                Route = $"/doctor/appointments/{appointment.Id}",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"appt_checkin_doc_{appointment.Id}_{doctorUserId}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<ReceptionStatsDto> GetStatsAsync(long? facilityId = null)
    {
        var userId = GetUserId();
        var today = _dateTimeProvider.VietnamToday;

        var isGlobalAdmin = await _facilityAuthService.HasFullFacilityAccessAsync(userId);
        var allowedFacilityIds = await _facilityAuthService.GetUserAccessibleFacilityIdsAsync(userId);

        if (!isGlobalAdmin && allowedFacilityIds.Count == 0)
        {
            return new ReceptionStatsDto();
        }

        if (facilityId.HasValue && facilityId.Value > 0)
        {
            await _facilityAuthService.ValidateUserFacilityAccessAsync(userId, facilityId.Value);
        }

        var apptQuery = _dbContext.Appointments.AsNoTracking();
        if (facilityId.HasValue && facilityId.Value > 0)
        {
            var facId = facilityId.Value;
            apptQuery = apptQuery.Where(a => (a.PatientVisit != null && a.PatientVisit.FacilityId == facId)
                                          || (a.PatientVisit == null && _dbContext.StaffFacilityAssignments.Any(s => s.UserId == a.Doctor.UserId && s.IsActive && s.FacilityId == facId)));
        }
        else if (!isGlobalAdmin)
        {
            apptQuery = apptQuery.Where(a => (a.PatientVisit != null && allowedFacilityIds.Contains(a.PatientVisit.FacilityId))
                                          || (a.PatientVisit == null && _dbContext.StaffFacilityAssignments.Any(s => s.UserId == a.Doctor.UserId && s.IsActive && allowedFacilityIds.Contains(s.FacilityId))));
        }

        var appointmentsToday = await apptQuery
            .Where(a => a.AppointmentDate == today)
            .ToListAsync();

        var pendingChangeRequests = await _dbContext.AppointmentChangeRequests
            .CountAsync(c => c.Status == AppointmentChangeRequestStatus.Pending);

        var unbilledVisitsQuery = _dbContext.PatientVisits
            .AsNoTracking()
            .Where(v => v.Status != VisitStatus.Cancelled)
            .Where(v =>
                v.Status == VisitStatus.InBilling
                || _dbContext.Invoices.Any(i => i.PatientVisitId == v.Id && i.Status == InvoiceStatus.Unpaid)
                || ((v.Department != null && v.Department.Specialty != null && v.Department.Specialty.ConsultationFee > 0)
                    && !_dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.ReferenceType == "Consultation" && ii.ReferenceId == v.Id))
                || v.DiagnosticOrders.Any(o => o.Status != DiagnosticOrderStatus.Cancelled && o.Items.Any(i => i.Status != DiagnosticItemStatus.Cancelled && !_dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.ReferenceType == "DiagnosticItem" && ii.ReferenceId == i.Id)))
                || v.Prescriptions.Any(p => (p.Status == PrescriptionStatus.ReservedForPurchase || p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.Dispensed) && p.Items.Any(pi => !_dbContext.InvoiceItems.Any(ii => !ii.IsCancelled && ii.ReferenceType == "PrescriptionItem" && (ii.ReferenceId == p.Id * 4294967296L + pi.MedicineId || ii.ReferenceId == p.Id * 100000L + pi.MedicineId || ii.ReferenceId == p.Id))))
            );

        if (facilityId.HasValue && facilityId.Value > 0)
        {
            unbilledVisitsQuery = unbilledVisitsQuery.Where(v => v.FacilityId == facilityId.Value);
        }
        else if (!isGlobalAdmin)
        {
            unbilledVisitsQuery = unbilledVisitsQuery.Where(v => allowedFacilityIds.Contains(v.FacilityId));
        }
        var unbilledCount = await unbilledVisitsQuery.CountAsync();

        return new ReceptionStatsDto
        {
            AppointmentsToday = appointmentsToday.Count,
            PendingAppointmentsToday = appointmentsToday.Count(a => a.Status == AppointmentStatus.Pending),
            ConfirmedAppointmentsToday = appointmentsToday.Count(a => a.Status == AppointmentStatus.Confirmed),
            CompletedAppointmentsToday = appointmentsToday.Count(a => a.Status == AppointmentStatus.Completed),
            PendingChangeRequests = pendingChangeRequests,
            UnbilledCount = unbilledCount
        };
    }

    private static ReceptionAppointmentDto MapToDto(
        Appointment a, 
        string patientName, 
        string patientPhone, 
        string mrn, 
        string nationalId, 
        string doctorName, 
        string specialtyName,
        long? patientVisitId = null,
        long? facilityId = null,
        string? facilityName = null) => new()
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
        MedicalRecordNumber = mrn ?? string.Empty,
        NationalId = nationalId ?? string.Empty,
        DoctorName = doctorName ?? string.Empty,
        SpecialtyName = specialtyName ?? string.Empty,
        PatientVisitId = patientVisitId,
        FacilityId = facilityId,
        FacilityName = facilityName
    };
}
