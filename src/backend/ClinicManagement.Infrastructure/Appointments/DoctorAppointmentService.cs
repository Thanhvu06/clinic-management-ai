using System;
using System.Collections.Generic;
using ClinicManagement.Application.Appointments.DTOs;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.DTOs.Revisit;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Appointments;

public class DoctorAppointmentService : IDoctorAppointmentService
{
    private readonly AppDbContext _dbContext;
    private readonly IDoctorContextService _doctorContextService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUserService _currentUserService;

    public DoctorAppointmentService(
        AppDbContext dbContext, 
        IDoctorContextService doctorContextService,
        IDateTimeProvider dateTimeProvider,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _doctorContextService = doctorContextService;
        _dateTimeProvider = dateTimeProvider;
        _currentUserService = currentUserService;
    }

    private Task<Doctor> GetCurrentDoctorAsync()
    {
        return _doctorContextService.GetCurrentActiveDoctorAsync();
    }

    private Guid GetUserId()
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");
        return currentUserId.Value;
    }

    public async Task<DoctorDashboardDto> GetDoctorDashboardAsync(DateOnly? date)
    {
        var doctor = await GetCurrentDoctorAsync();
        var targetDate = date ?? _dateTimeProvider.VietnamToday;
        var currentTime = _dateTimeProvider.VietnamTime;

        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join u in _dbContext.Users on p.UserId equals (Guid?)u.Id into uGroup
                    from u in uGroup.DefaultIfEmpty()
                    join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                    where a.DoctorId == doctor.Id && a.AppointmentDate == targetDate
                    orderby a.StartTime ascending
                    select new DoctorQueueItemDto
                    {
                        AppointmentId = a.Id,
                        AppointmentCode = a.AppointmentCode,
                        AppointmentDate = a.AppointmentDate,
                        StartTime = a.StartTime,
                        EndTime = a.EndTime,
                        PatientId = p.Id,
                        PatientName = u != null ? u.FullName : (p.FullName ?? string.Empty),
                        PatientPhone = u != null ? (u.PhoneNumber ?? string.Empty) : (p.PhoneNumber ?? string.Empty),
                        PatientGender = p.Gender != null ? p.Gender.Value.ToString() : string.Empty,
                        PatientDob = p.DateOfBirth,
                        Reason = a.Reason,
                        Status = a.Status.ToString(),
                        SpecialtyName = s.Name
                    };

        var queue = await query.ToListAsync();

        var aptIds = queue.Where(q => q.AppointmentId.HasValue).Select(q => q.AppointmentId!.Value).ToList();
        var visitMap = await _dbContext.PatientVisits
            .AsNoTracking()
            .Where(v => v.AppointmentId.HasValue && aptIds.Contains(v.AppointmentId.Value))
            .ToDictionaryAsync(v => v.AppointmentId!.Value);

        foreach (var item in queue)
        {
            if (item.AppointmentId.HasValue && visitMap.TryGetValue(item.AppointmentId.Value, out var pv))
            {
                item.PatientVisitId = pv.Id;
                item.VisitCode = pv.VisitCode;
                item.QueueNumber = pv.QueueNumber;
                if (pv.Status == VisitStatus.WaitingDoctor && item.Status == nameof(AppointmentStatus.Confirmed))
                {
                    item.Status = nameof(AppointmentStatus.CheckedIn);
                }
                else if (pv.Status == VisitStatus.InDiagnostics)
                {
                    item.Status = "InDiagnostics";
                }
                else if (pv.Status == VisitStatus.ResultsReady)
                {
                    item.Status = "ResultsReady";
                }
            }
        }

        var startOfDay = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endOfDay = targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var doctorSpecialtyIds = await _dbContext.DoctorSpecialties
            .Where(ds => ds.DoctorId == doctor.Id)
            .Select(ds => ds.SpecialtyId)
            .ToListAsync();

        var doctorDeptIds = await _dbContext.Departments
            .Where(d => d.SpecialtyId.HasValue && doctorSpecialtyIds.Contains(d.SpecialtyId.Value))
            .Select(d => d.Id)
            .ToListAsync();

        var staffDeptIds = await _dbContext.StaffFacilityAssignments
            .Where(s => s.UserId == doctor.UserId && s.IsActive && s.DepartmentId.HasValue)
            .Select(s => s.DepartmentId!.Value)
            .ToListAsync();

        var allDoctorDeptIds = doctorDeptIds.Concat(staffDeptIds).Distinct().ToList();

        var walkInVisits = await _dbContext.PatientVisits
            .AsNoTracking()
            .Include(v => v.Patient)
            .Include(v => v.Department)
                .ThenInclude(d => d.Specialty)
            .Where(v => v.AppointmentId == null &&
                        v.VisitDate == targetDate &&
                        (v.AssignedDoctorId == doctor.Id || (!v.AssignedDoctorId.HasValue && allDoctorDeptIds.Contains(v.DepartmentId))))
            .ToListAsync();

        var walkInItems = walkInVisits.Select(v => new DoctorQueueItemDto
        {
            AppointmentId = null,
            AppointmentCode = v.VisitCode,
            PatientVisitId = v.Id,
            VisitCode = v.VisitCode,
            QueueNumber = v.QueueNumber,
            AppointmentDate = targetDate,
            StartTime = TimeOnly.FromDateTime(v.CheckedInAtUtc),
            EndTime = TimeOnly.FromDateTime(v.CheckedInAtUtc).AddMinutes(15),
            PatientId = v.PatientId,
            PatientName = v.Patient?.FullName ?? "Bệnh nhân",
            PatientPhone = v.Patient?.PhoneNumber ?? string.Empty,
            PatientGender = v.Patient?.Gender.HasValue == true ? v.Patient.Gender.Value.ToString() : string.Empty,
            PatientDob = v.Patient?.DateOfBirth,
            Reason = v.ChiefComplaint,
            Status = v.Status switch
            {
                VisitStatus.WaitingDoctor => nameof(AppointmentStatus.CheckedIn),
                VisitStatus.InConsultation => nameof(AppointmentStatus.InConsultation),
                VisitStatus.InDiagnostics => "InDiagnostics",
                VisitStatus.ResultsReady => "ResultsReady",
                VisitStatus.ConsultationCompleted => nameof(AppointmentStatus.Completed),
                VisitStatus.InPharmacy => nameof(AppointmentStatus.Completed),
                VisitStatus.InBilling => nameof(AppointmentStatus.Completed),
                VisitStatus.Completed => nameof(AppointmentStatus.Completed),
                VisitStatus.Cancelled => nameof(AppointmentStatus.Cancelled),
                _ => v.Status.ToString()
            },
            SpecialtyName = v.Department?.Specialty?.Name ?? v.Department?.Name ?? string.Empty
        }).ToList();

        queue.AddRange(walkInItems);

        var walkInVisitIds = walkInVisits.Select(v => v.Id).ToList();
        var visitVitalsDict = await _dbContext.AppointmentVitalSigns
            .AsNoTracking()
            .Where(v => v.PatientVisitId.HasValue && walkInVisitIds.Contains(v.PatientVisitId.Value))
            .ToDictionaryAsync(v => v.PatientVisitId!.Value);

        var visitEncountersDict = await _dbContext.VisitSummaries
            .AsNoTracking()
            .Where(e => e.PatientVisitId.HasValue && walkInVisitIds.Contains(e.PatientVisitId.Value))
            .ToDictionaryAsync(e => e.PatientVisitId!.Value);

        queue = queue.OrderBy(q => q.QueueNumber ?? 9999).ThenBy(q => q.StartTime).ToList();

        var vitalsDict = await _dbContext.AppointmentVitalSigns
            .AsNoTracking()
            .Where(v => v.AppointmentId.HasValue && aptIds.Contains(v.AppointmentId.Value))
            .ToDictionaryAsync(v => v.AppointmentId!.Value);

        var encountersDict = await _dbContext.VisitSummaries
            .AsNoTracking()
            .Where(e => e.AppointmentId.HasValue && aptIds.Contains(e.AppointmentId.Value))
            .ToDictionaryAsync(e => e.AppointmentId!.Value);

        for (int i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            item.QueueOrder = i + 1;
            if (item.PatientDob.HasValue)
            {
                var age = targetDate.Year - item.PatientDob.Value.Year;
                if (targetDate < item.PatientDob.Value.AddYears(age)) age--;
                item.PatientAge = age;
            }

            AppointmentVitalSigns? vitals = null;
            if (item.AppointmentId.HasValue && vitalsDict.TryGetValue(item.AppointmentId.Value, out var aptVitals))
                vitals = aptVitals;
            else if (item.PatientVisitId.HasValue && visitVitalsDict.TryGetValue(item.PatientVisitId.Value, out var visVitals))
                vitals = visVitals;

            if (vitals != null)
            {
                item.IsVitalsRecorded = true;
                var parts = new List<string>();
                if (vitals.BloodPressureSystolic.HasValue && vitals.BloodPressureDiastolic.HasValue)
                    parts.Add($"HA: {vitals.BloodPressureSystolic}/{vitals.BloodPressureDiastolic} mmHg");
                if (vitals.HeartRate.HasValue)
                    parts.Add($"Mạch: {vitals.HeartRate} bpm");
                if (vitals.Temperature.HasValue)
                    parts.Add($"T: {vitals.Temperature:0.#}°C");
                if (vitals.SpO2.HasValue)
                    parts.Add($"SpO2: {vitals.SpO2}%");
                item.VitalSummaryText = parts.Count > 0 ? string.Join(", ", parts) : null;
            }

            VisitSummary? encounter = null;
            if (item.AppointmentId.HasValue && encountersDict.TryGetValue(item.AppointmentId.Value, out var aptEncounter))
                encounter = aptEncounter;
            else if (item.PatientVisitId.HasValue && visitEncountersDict.TryGetValue(item.PatientVisitId.Value, out var visEncounter))
                encounter = visEncounter;

            if (encounter != null && !string.IsNullOrWhiteSpace(encounter.ChiefComplaint))
            {
                item.ChiefComplaint = encounter.ChiefComplaint;
            }
            else
            {
                item.ChiefComplaint = item.Reason;
            }
        }

        var total = queue.Count;
        var checkedIn = queue.Count(q => q.Status == nameof(AppointmentStatus.CheckedIn));
        var inConsultation = queue.Count(q => q.Status == nameof(AppointmentStatus.InConsultation));
        var completed = queue.Count(q => q.Status == nameof(AppointmentStatus.Completed));
        var noShow = queue.Count(q => q.Status == nameof(AppointmentStatus.NoShow));

        var activeSchedules = await _dbContext.DoctorWorkSchedules
            .AsNoTracking()
            .Where(ws => ws.DoctorId == doctor.Id && ws.WorkDate == targetDate && ws.IsActive)
            .OrderBy(ws => ws.StartTime)
            .ToListAsync();

        string currentShift = "Không có ca trực";
        if (activeSchedules.Count > 0)
        {
            var matchingShift = activeSchedules.FirstOrDefault(ws => ws.StartTime <= currentTime && ws.EndTime >= currentTime);
            if (matchingShift != null)
            {
                currentShift = matchingShift.StartTime < new TimeOnly(12, 0)
                    ? $"Ca sáng ({matchingShift.StartTime:HH\\:mm} - {matchingShift.EndTime:HH\\:mm})"
                    : $"Ca chiều ({matchingShift.StartTime:HH\\:mm} - {matchingShift.EndTime:HH\\:mm})";
            }
            else
            {
                currentShift = string.Join(", ", activeSchedules.Select(s => $"{s.StartTime:HH\\:mm} - {s.EndTime:HH\\:mm}"));
            }
        }

        var nextPatient = queue.FirstOrDefault(q => q.Status == nameof(AppointmentStatus.InConsultation))
                       ?? queue.FirstOrDefault(q => q.Status == nameof(AppointmentStatus.CheckedIn))
                       ?? queue.FirstOrDefault(q => q.Status == nameof(AppointmentStatus.Confirmed));

        var upcomingEndDate = targetDate.AddDays(7);
        var upcomingQuery = from a in _dbContext.Appointments
                            join p in _dbContext.Patients on a.PatientId equals p.Id
                            join u in _dbContext.Users on p.UserId equals (Guid?)u.Id into uGroup
                            from u in uGroup.DefaultIfEmpty()
                            join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                            where a.DoctorId == doctor.Id
                                  && a.AppointmentDate > targetDate
                                  && a.AppointmentDate <= upcomingEndDate
                                  && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)
                            orderby a.AppointmentDate ascending, a.StartTime ascending
                            select new DoctorQueueItemDto
                            {
                                AppointmentId = a.Id,
                                AppointmentCode = a.AppointmentCode,
                                AppointmentDate = a.AppointmentDate,
                                StartTime = a.StartTime,
                                EndTime = a.EndTime,
                                PatientId = p.Id,
                                PatientName = u != null ? u.FullName : (p.FullName ?? string.Empty),
                                PatientPhone = u != null ? (u.PhoneNumber ?? string.Empty) : (p.PhoneNumber ?? string.Empty),
                                PatientGender = p.Gender != null ? p.Gender.Value.ToString() : string.Empty,
                                PatientDob = p.DateOfBirth,
                                Reason = a.Reason,
                                Status = a.Status.ToString(),
                                SpecialtyName = s.Name
                            };

        var upcomingList = await upcomingQuery.ToListAsync();
        for (int i = 0; i < upcomingList.Count; i++)
        {
            var item = upcomingList[i];
            item.QueueOrder = i + 1;
            if (item.PatientDob.HasValue)
            {
                var age = item.AppointmentDate.Year - item.PatientDob.Value.Year;
                if (item.AppointmentDate < item.PatientDob.Value.AddYears(age)) age--;
                item.PatientAge = age;
            }
        }

        return new DoctorDashboardDto
        {
            TodayDate = targetDate,
            TotalToday = total,
            CheckedInCount = checkedIn,
            InConsultationCount = inConsultation,
            CompletedTodayCount = completed,
            NoShowTodayCount = noShow,
            CurrentShift = currentShift,
            NextPatient = nextPatient,
            Queue = queue,
            UpcomingAppointments = upcomingList
        };
    }

    public async Task<List<DoctorScheduleDayDto>> GetDoctorScheduleAsync(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
            throw new BusinessException("INVALID_DATE_RANGE", "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");

        if (toDate.DayNumber - fromDate.DayNumber > 365)
            throw new BusinessException("DATE_RANGE_TOO_LARGE", "Khoảng xem lịch không được vượt quá 366 ngày.");

        var doctor = await GetCurrentDoctorAsync();

        var schedules = await _dbContext.DoctorWorkSchedules
            .AsNoTracking()
            .Where(ws => ws.DoctorId == doctor.Id && ws.WorkDate >= fromDate && ws.WorkDate <= toDate)
            .OrderBy(ws => ws.WorkDate).ThenBy(ws => ws.StartTime)
            .ToListAsync();

        var slots = await _dbContext.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.DoctorId == doctor.Id && s.SlotDate >= fromDate && s.SlotDate <= toDate)
            .OrderBy(s => s.SlotDate).ThenBy(s => s.StartTime)
            .ToListAsync();

        var appointments = await (from a in _dbContext.Appointments
                                  join p in _dbContext.Patients on a.PatientId equals p.Id
                                  join u in _dbContext.Users on p.UserId equals (Guid?)u.Id into uGroup
                                  from u in uGroup.DefaultIfEmpty()
                                  where a.DoctorId == doctor.Id && a.AppointmentDate >= fromDate && a.AppointmentDate <= toDate
                                  select new
                                  {
                                      a.Id,
                                      a.AppointmentSlotId,
                                      a.AppointmentCode,
                                      a.Status,
                                      PatientName = u != null ? u.FullName : (p.FullName ?? string.Empty)
                                  }).ToListAsync();

        var apptMap = appointments.ToDictionary(a => a.AppointmentSlotId);

        var groupedDays = new List<DoctorScheduleDayDto>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var date = d;
            var daySchedules = schedules.Where(s => s.WorkDate == date).ToList();
            var daySlots = slots.Where(s => s.SlotDate == date).ToList();

            var shiftDtos = new List<DoctorShiftDto>();
            foreach (var sch in daySchedules)
            {
                var schSlots = daySlots
                    .Where(s => s.StartTime >= sch.StartTime && s.EndTime <= sch.EndTime)
                    .Select(s =>
                    {
                        apptMap.TryGetValue(s.Id, out var appt);
                        return new DoctorSlotDetailDto
                        {
                            SlotId = s.Id,
                            SlotDate = s.SlotDate,
                            StartTime = s.StartTime,
                            EndTime = s.EndTime,
                            IsBooked = s.IsBooked,
                            AppointmentId = appt?.Id,
                            AppointmentCode = appt?.AppointmentCode,
                            PatientName = appt?.PatientName,
                            Status = appt?.Status.ToString()
                        };
                    }).ToList();

                var shiftName = sch.StartTime < new TimeOnly(12, 0) ? "Ca sáng" : "Ca chiều";
                var bookedCount = schSlots.Count(s => s.IsBooked);

                shiftDtos.Add(new DoctorShiftDto
                {
                    ScheduleId = sch.Id,
                    ShiftName = shiftName,
                    StartTime = sch.StartTime,
                    EndTime = sch.EndTime,
                    IsActive = sch.IsActive,
                    TotalSlots = schSlots.Count,
                    BookedSlots = bookedCount,
                    AvailableSlots = schSlots.Count - bookedCount,
                    Slots = schSlots
                });
            }

            var dayName = date.DayOfWeek switch
            {
                DayOfWeek.Monday => "Thứ Hai",
                DayOfWeek.Tuesday => "Thứ Ba",
                DayOfWeek.Wednesday => "Thứ Tư",
                DayOfWeek.Thursday => "Thứ Năm",
                DayOfWeek.Friday => "Thứ Sáu",
                DayOfWeek.Saturday => "Thứ Bảy",
                DayOfWeek.Sunday => "Chủ Nhật",
                _ => string.Empty
            };

            groupedDays.Add(new DoctorScheduleDayDto
            {
                Date = date,
                DayOfWeekName = dayName,
                Shifts = shiftDtos
            });
        }

        return groupedDays;
    }

    public async Task<PagedResult<DoctorAppointmentDto>> GetMyAppointmentsAsync(DateOnly? date, string? status, string? search, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var doctor = await GetCurrentDoctorAsync();

        var query = from a in _dbContext.Appointments
                    join p in _dbContext.Patients on a.PatientId equals p.Id
                    join pu in _dbContext.Users on p.UserId equals (Guid?)pu.Id into puGroup
                    from pu in puGroup.DefaultIfEmpty()
                    where a.DoctorId == doctor.Id
                    select new
                    {
                        Appointment = a,
                        PatientName = pu != null ? pu.FullName : (p.FullName ?? string.Empty),
                        PatientPhone = pu != null ? (pu.PhoneNumber ?? string.Empty) : (p.PhoneNumber ?? string.Empty),
                        PatientGender = p.Gender,
                        PatientDob = p.DateOfBirth
                    };

        if (date.HasValue)
        {
            query = query.Where(x => x.Appointment.AppointmentDate == date.Value);
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Appointment.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(x => x.Appointment.AppointmentCode.Contains(search) 
                                  || x.PatientName.Contains(search)
                                  || x.PatientPhone.Contains(search));
        }

        query = query.OrderByDescending(x => x.Appointment.AppointmentDate).ThenBy(x => x.Appointment.StartTime);

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
                    join pu in _dbContext.Users on p.UserId equals (Guid?)pu.Id into puGroup
                    from pu in puGroup.DefaultIfEmpty()
                    where a.Id == appointmentId && a.DoctorId == doctor.Id
                    select new
                    {
                        Appointment = a,
                        PatientName = pu != null ? pu.FullName : (p.FullName ?? string.Empty),
                        PatientPhone = pu != null ? (pu.PhoneNumber ?? string.Empty) : (p.PhoneNumber ?? string.Empty),
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

    public async Task<PatientClinicalContextDto> GetPatientClinicalContextAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var appointment = await _dbContext.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Specialty)
            .Include(a => a.VitalSigns)
            .Include(a => a.VisitSummary)
            .Include(a => a.Prescription)
                .ThenInclude(p => p!.Items)
                    .ThenInclude(i => i.Medicine)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null)
            throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        var patientUser = appointment.Patient.UserId.HasValue
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == appointment.Patient.UserId.Value)
            : null;
        var doctorUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == doctor.UserId);

        var patientId = appointment.PatientId;

        var pastAppointments = await _dbContext.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor)
            .Include(a => a.Specialty)
            .Include(a => a.VisitSummary)
            .Include(a => a.Prescription)
                .ThenInclude(p => p!.Items)
                    .ThenInclude(i => i.Medicine)
            .Where(a => a.PatientId == patientId && a.Id != appointment.Id && a.Status == AppointmentStatus.Completed)
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .Take(20)
            .ToListAsync();

        var pastDoctorUserIds = pastAppointments.Select(a => a.Doctor.UserId).Distinct().ToList();

        var pastWalkInVisits = await _dbContext.PatientVisits
            .AsNoTracking()
            .Include(v => v.AssignedDoctor)
            .Include(v => v.Department)
                .ThenInclude(d => d.Specialty)
            .Include(v => v.VisitSummary)
            .Include(v => v.Prescriptions)
                .ThenInclude(p => p.Items)
                    .ThenInclude(i => i.Medicine)
            .Where(v => v.PatientId == patientId && v.AppointmentId == null &&
                        (v.Status == VisitStatus.Completed || v.Status == VisitStatus.ConsultationCompleted ||
                         v.Status == VisitStatus.InPharmacy || v.Status == VisitStatus.InBilling))
            .OrderByDescending(v => v.VisitDate)
            .ThenByDescending(v => v.CheckedInAtUtc)
            .Take(20)
            .ToListAsync();

        var pastWalkInDoctorUserIds = pastWalkInVisits
            .Where(v => v.AssignedDoctor != null)
            .Select(v => v.AssignedDoctor!.UserId)
            .Distinct()
            .ToList();

        var allDoctorUserIds = pastDoctorUserIds.Concat(pastWalkInDoctorUserIds).Distinct().ToList();
        var pastDoctorUsers = await _dbContext.Users
            .Where(u => allDoctorUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var pastVisitDtos = new List<PastVisitSummaryDto>();

        foreach (var a in pastAppointments)
        {
            pastDoctorUsers.TryGetValue(a.Doctor.UserId, out var docName);
            pastVisitDtos.Add(new PastVisitSummaryDto
            {
                AppointmentId = a.Id,
                AppointmentCode = a.AppointmentCode,
                Date = a.AppointmentDate,
                DoctorName = docName ?? "Bác sĩ",
                SpecialtyName = a.Specialty?.Name ?? string.Empty,
                Diagnosis = a.VisitSummary?.Diagnosis,
                Summary = a.VisitSummary?.Summary,
                PrescriptionItemNames = a.Prescription?.Items.Select(i => i.Medicine?.Name ?? "Thuốc").ToList() ?? new List<string>()
            });
        }

        foreach (var v in pastWalkInVisits)
        {
            string? docName = null;
            if (v.AssignedDoctor != null) pastDoctorUsers.TryGetValue(v.AssignedDoctor.UserId, out docName);
            pastVisitDtos.Add(new PastVisitSummaryDto
            {
                AppointmentId = 0,
                PatientVisitId = v.Id,
                AppointmentCode = v.VisitCode,
                Date = v.VisitDate,
                DoctorName = docName ?? "Bác sĩ",
                SpecialtyName = v.Department?.Specialty?.Name ?? v.Department?.Name ?? string.Empty,
                Diagnosis = v.VisitSummary?.Diagnosis,
                Summary = v.VisitSummary?.Summary,
                PrescriptionItemNames = v.Prescriptions.SelectMany(p => p.Items).Select(i => i.Medicine?.Name ?? "Thuốc").ToList()
            });
        }

        pastVisitDtos = pastVisitDtos
            .OrderByDescending(p => p.Date)
            .Take(20)
            .ToList();

        VitalSignsDto? vitalsDto = null;
        if (appointment.VitalSigns != null)
        {
            var recorder = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == appointment.VitalSigns.RecordedByUserId);
            vitalsDto = MapVitalsToDto(appointment.VitalSigns, recorder?.FullName ?? "Nhân viên y tế");
        }

        // 1. Vital history query across appointments & walk-ins for this patient (exclude Cancelled and NoShow)
        var allVitalSigns = await _dbContext.AppointmentVitalSigns
            .AsNoTracking()
            .Include(vs => vs.Appointment)
            .Include(vs => vs.PatientVisit)
            .Where(vs =>
                (vs.Appointment != null && vs.Appointment.PatientId == patientId &&
                 vs.Appointment.Status != AppointmentStatus.Cancelled &&
                 vs.Appointment.Status != AppointmentStatus.NoShow) ||
                (vs.PatientVisit != null && vs.PatientVisit.PatientId == patientId &&
                 vs.PatientVisit.Status != VisitStatus.Cancelled))
            .OrderByDescending(vs => vs.RecordedAtUtc)
            .Take(20)
            .ToListAsync();

        var vitalRecorderUserIds = allVitalSigns
            .Select(vs => vs.RecordedByUserId)
            .Distinct()
            .ToList();

        var vitalRecorderUsers = await _dbContext.Users
            .Where(u => vitalRecorderUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var vitalHistoryDtos = allVitalSigns.Select(vs =>
        {
            vitalRecorderUsers.TryGetValue(vs.RecordedByUserId, out var recName);
            return new PatientVitalHistoryItemDto
            {
                AppointmentId = vs.AppointmentId ?? 0,
                PatientVisitId = vs.PatientVisitId,
                AppointmentCode = vs.Appointment?.AppointmentCode ?? vs.PatientVisit?.VisitCode ?? "VISIT",
                AppointmentDate = vs.Appointment?.AppointmentDate ?? (vs.PatientVisit != null ? vs.PatientVisit.VisitDate : DateOnly.FromDateTime(vs.RecordedAtUtc)),
                RecordedAtUtc = vs.RecordedAtUtc,
                Height = vs.Height,
                Weight = vs.Weight,
                Bmi = vs.Bmi,
                Temperature = vs.Temperature,
                BloodPressureSystolic = vs.BloodPressureSystolic,
                BloodPressureDiastolic = vs.BloodPressureDiastolic,
                HeartRate = vs.HeartRate,
                RespiratoryRate = vs.RespiratoryRate,
                SpO2 = vs.SpO2,
                RecordedByUserName = recName ?? "Nhân viên y tế"
            };
        }).ToList();

        // 2. Current measurement (from current appointment if recorded)
        var currentMeasurement = vitalHistoryDtos.FirstOrDefault(vh => vh.AppointmentId == appointment.Id);

        // 3. Previous measurement (most recent prior to current appointment)
        var previousMeasurement = vitalHistoryDtos.FirstOrDefault(vh => vh.AppointmentId != appointment.Id);

        // 4. Compute anthropometric deltas
        AnthropometricComparisonDto? anthropometricComparison = null;
        if (currentMeasurement != null || previousMeasurement != null)
        {
            decimal? weightDelta = null;
            if (currentMeasurement?.Weight.HasValue == true && previousMeasurement?.Weight.HasValue == true)
            {
                weightDelta = Math.Round(currentMeasurement.Weight.Value - previousMeasurement.Weight.Value, 2);
            }

            decimal? heightDelta = null;
            if (currentMeasurement?.Height.HasValue == true && previousMeasurement?.Height.HasValue == true)
            {
                heightDelta = Math.Round(currentMeasurement.Height.Value - previousMeasurement.Height.Value, 1);
            }

            decimal? bmiDelta = null;
            if (currentMeasurement?.Bmi.HasValue == true && previousMeasurement?.Bmi.HasValue == true)
            {
                bmiDelta = Math.Round(currentMeasurement.Bmi.Value - previousMeasurement.Bmi.Value, 1);
            }

            anthropometricComparison = new AnthropometricComparisonDto
            {
                CurrentMeasurement = currentMeasurement,
                PreviousMeasurement = previousMeasurement,
                WeightDeltaKg = weightDelta,
                HeightDeltaCm = heightDelta,
                BmiDelta = bmiDelta,
                HasComparableData = weightDelta.HasValue || heightDelta.HasValue || bmiDelta.HasValue
            };
        }

        // 5. Latest known vitals if current appointment hasn't been measured yet (reference only)
        VitalSignsDto? latestKnownVitals = null;
        if (appointment.VitalSigns == null && previousMeasurement != null)
        {
            latestKnownVitals = new VitalSignsDto
            {
                AppointmentId = previousMeasurement.AppointmentId,
                Temperature = previousMeasurement.Temperature,
                BloodPressureSystolic = previousMeasurement.BloodPressureSystolic,
                BloodPressureDiastolic = previousMeasurement.BloodPressureDiastolic,
                HeartRate = previousMeasurement.HeartRate,
                RespiratoryRate = previousMeasurement.RespiratoryRate,
                Weight = previousMeasurement.Weight,
                Height = previousMeasurement.Height,
                Bmi = previousMeasurement.Bmi,
                SpO2 = previousMeasurement.SpO2,
                RecordedAtUtc = previousMeasurement.RecordedAtUtc,
                RecordedByUserName = previousMeasurement.RecordedByUserName
            };
        }

        ClinicalEncounterDto? encounterDto = null;
        if (appointment.VisitSummary != null)
        {
            encounterDto = MapEncounterToDto(appointment.VisitSummary, doctorUser?.FullName ?? "Bác sĩ");
        }

        PrescriptionDraftDto? presDto = null;
        if (appointment.Prescription != null)
        {
            presDto = MapPrescriptionToDto(appointment.Prescription, doctorUser?.FullName ?? "Bác sĩ", patientUser?.FullName ?? appointment.Patient.FullName ?? "Bệnh nhân");
        }

        return new PatientClinicalContextDto
        {
            PatientId = appointment.PatientId,
            PatientName = patientUser?.FullName ?? appointment.Patient.FullName ?? "Bệnh nhân",
            PatientPhone = patientUser?.PhoneNumber ?? appointment.Patient.PhoneNumber ?? string.Empty,
            PatientGender = appointment.Patient.Gender.HasValue ? appointment.Patient.Gender.Value.ToString() : string.Empty,
            PatientDob = appointment.Patient.DateOfBirth,
            Address = appointment.Patient.Address,
            TotalPastVisits = pastVisitDtos.Count,
            PastVisits = pastVisitDtos,
            CurrentAppointment = MapToDto(appointment, patientUser?.FullName ?? appointment.Patient.FullName ?? "", patientUser?.PhoneNumber ?? appointment.Patient.PhoneNumber ?? "", appointment.Patient.Gender, appointment.Patient.DateOfBirth),
            VitalSigns = vitalsDto,
            LatestKnownVitals = latestKnownVitals,
            Encounter = encounterDto,
            Prescription = presDto,
            VitalHistory = vitalHistoryDtos,
            AnthropometricComparison = anthropometricComparison
        };
    }

    public async Task CheckInAppointmentAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var appointment = await _dbContext.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        if (appointment.Status != AppointmentStatus.Confirmed)
            throw new BusinessException("INVALID_STATE_TRANSITION", "Chỉ có thể check-in lịch hẹn ở trạng thái Confirmed.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.CheckedIn;

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.CheckedIn,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.CheckedIn,
            Note = "Bác sĩ xác nhận bệnh nhân đã có mặt và check-in vào phòng khám",
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }

    public async Task StartConsultationAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var appointment = await _dbContext.Appointments
            .Include(a => a.VisitSummary)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        if (appointment.Status != AppointmentStatus.CheckedIn)
            throw new BusinessException("INVALID_STATE_TRANSITION", "Chỉ có thể bắt đầu khám khi bệnh nhân đã ở trạng thái CheckedIn.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.InConsultation;

        var relatedVisit = await _dbContext.PatientVisits.FirstOrDefaultAsync(v => v.AppointmentId == appointment.Id);
        if (relatedVisit != null)
        {
            relatedVisit.Status = VisitStatus.InConsultation;
            relatedVisit.AssignedDoctorId = doctor.Id;
        }

        if (appointment.VisitSummary == null)
        {
            appointment.VisitSummary = new VisitSummary
            {
                AppointmentId = appointment.Id,
                DoctorId = doctor.Id,
                ChiefComplaint = appointment.Reason,
                Summary = "Đang trong quá trình thăm khám.",
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.VisitSummaries.Add(appointment.VisitSummary);
        }

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.InConsultation,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.InConsultation,
            Note = "Bác sĩ bắt đầu phiên khám lâm sàng",
            PerformedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }

    public async Task CompleteAppointmentAsync(long appointmentId, CompleteConsultationRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var appointment = await _dbContext.Appointments
            .Include(a => a.VisitSummary)
            .Include(a => a.Prescription)
                .ThenInclude(p => p!.Items)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        if (string.IsNullOrWhiteSpace(request.Summary) && string.IsNullOrWhiteSpace(request.Diagnosis))
            throw new BusinessException("VALIDATION_ERROR", "Tóm tắt kết luận khám không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Diagnosis))
        {
            request.Diagnosis = !string.IsNullOrWhiteSpace(request.Summary) ? request.Summary : "Khám lâm sàng";
        }

        if (string.IsNullOrWhiteSpace(request.Summary))
        {
            request.Summary = request.Diagnosis;
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            if (appointment.Status != AppointmentStatus.InConsultation)
                throw new BusinessException("INVALID_STATE_TRANSITION", "Chỉ có thể hoàn tất lịch hẹn đang trong phiên khám (InConsultation).");

            // Diagnostic orders completion guards
            var diagnosticOrders = await _dbContext.DiagnosticOrders
                .Where(o => o.AppointmentId == appointment.Id)
                .ToListAsync();

            var pendingOrders = diagnosticOrders
                .Where(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress)
                .ToList();

            if (pendingOrders.Count > 0)
            {
                throw new BusinessException("PENDING_DIAGNOSTIC_RESULTS", "Không thể hoàn tất phiên khám khi còn chỉ định cận lâm sàng đang chờ kết quả.");
            }

            var unreviewedOrders = diagnosticOrders
                .Where(o => o.Status == DiagnosticOrderStatus.Completed && !o.ReviewedAtUtc.HasValue)
                .ToList();

            if (unreviewedOrders.Count > 0)
            {
                throw new BusinessException("UNREVIEWED_DIAGNOSTIC_RESULTS", "Không thể hoàn tất phiên khám khi có kết quả cận lâm sàng chưa được bác sĩ xem và xác nhận.");
            }

            var oldStatus = appointment.Status;
            appointment.Status = AppointmentStatus.Completed;

            var relatedVisit = await _dbContext.PatientVisits.FirstOrDefaultAsync(v => v.AppointmentId == appointment.Id);

            var summary = appointment.VisitSummary;
            if (summary == null)
            {
                summary = new VisitSummary
                {
                    AppointmentId = appointment.Id,
                    PatientVisitId = relatedVisit?.Id,
                    DoctorId = doctor.Id,
                    ChiefComplaint = request.ChiefComplaint,
                    ClinicalFindings = request.ClinicalFindings,
                    Diagnosis = request.Diagnosis,
                    DiagnosisCode = request.DiagnosisCode,
                    TreatmentPlan = request.TreatmentPlan,
                    Summary = request.Summary,
                    FollowUpInstruction = request.FollowUpInstruction,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                };
                _dbContext.VisitSummaries.Add(summary);
            }
            else
            {
                ValidateRowVersion(summary.RowVersion, request.EncounterRowVersion);

                if (!summary.PatientVisitId.HasValue && relatedVisit != null)
                {
                    summary.PatientVisitId = relatedVisit.Id;
                }

                summary.ChiefComplaint = request.ChiefComplaint;
                summary.ClinicalFindings = request.ClinicalFindings;
                summary.Diagnosis = request.Diagnosis;
                summary.DiagnosisCode = request.DiagnosisCode;
                summary.TreatmentPlan = request.TreatmentPlan;
                summary.Summary = request.Summary;
                summary.FollowUpInstruction = request.FollowUpInstruction;
                summary.UpdatedAtUtc = DateTime.UtcNow;
                summary.CompletedAtUtc = DateTime.UtcNow;
            }

            if (request.IssuePrescription && request.PrescriptionItems != null && request.PrescriptionItems.Count > 0)
            {
                var prescription = appointment.Prescription;
                if (prescription == null)
                {
                    prescription = new Prescription
                    {
                        AppointmentId = appointment.Id,
                        PatientVisitId = relatedVisit?.Id,
                        PatientId = appointment.PatientId,
                        DoctorId = doctor.Id,
                        Status = PrescriptionStatus.Issued,
                        Notes = request.PrescriptionNotes,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.Prescriptions.Add(prescription);
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    ValidateRowVersion(prescription.RowVersion, request.PrescriptionRowVersion);
                    if (!prescription.PatientVisitId.HasValue && relatedVisit != null)
                    {
                        prescription.PatientVisitId = relatedVisit.Id;
                    }
                    prescription.Status = PrescriptionStatus.Issued;
                    prescription.Notes = request.PrescriptionNotes;
                    prescription.CreatedAt = DateTime.UtcNow;

                    _dbContext.PrescriptionItems.RemoveRange(prescription.Items);
                }

                foreach (var item in request.PrescriptionItems)
                {
                    var medExists = await _dbContext.Medicines.AnyAsync(m => m.Id == item.MedicineId && m.IsActive);
                    if (!medExists)
                        throw new BusinessException("INVALID_MEDICINE", $"Thuốc với ID {item.MedicineId} không tồn tại hoặc đã ngừng cung cấp.");

                    _dbContext.PrescriptionItems.Add(new PrescriptionItem
                    {
                        PrescriptionId = prescription.Id,
                        MedicineId = item.MedicineId,
                        Quantity = item.Quantity,
                        Dosage = item.Dosage ?? string.Empty,
                        Frequency = item.Frequency ?? string.Empty,
                        DurationDays = item.DurationDays,
                        Instructions = item.Instructions
                    });
                }
            }
            else if (appointment.Prescription != null && appointment.Prescription.Status == PrescriptionStatus.Draft)
            {
                if (appointment.Prescription.Items.Count > 0)
                {
                    appointment.Prescription.Status = PrescriptionStatus.Issued;
                }
            }

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.Completed,
                OldStatus = oldStatus,
                NewStatus = AppointmentStatus.Completed,
                Note = "Bác sĩ hoàn thành phiên khám lâm sàng và cấp hồ sơ bệnh án",
                PerformedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });

            if (relatedVisit != null)
            {
                relatedVisit.Status = (request.IssuePrescription && request.PrescriptionItems != null && request.PrescriptionItems.Count > 0) || (appointment.Prescription != null && appointment.Prescription.Items.Count > 0)
                    ? VisitStatus.InPharmacy
                    : VisitStatus.InBilling;
            }

            var patientUserId = await _dbContext.Patients
                .Where(p => p.Id == appointment.PatientId)
                .Select(p => p.UserId)
                .FirstOrDefaultAsync();

            if (patientUserId.HasValue && patientUserId.Value != Guid.Empty)
            {
                _dbContext.Notifications.Add(new Notification
                {
                    UserId = patientUserId.Value,
                    Type = NotificationType.Prescription,
                    Title = "Hoàn thành buổi khám bệnh",
                    Message = $"Buổi khám #{appointment.AppointmentCode} đã hoàn tất. Bạn có thể xem kết luận khám và đơn thuốc trực tuyến.",
                    Route = "/patient/appointments",
                    RelatedEntityType = "Appointment",
                    RelatedEntityId = appointment.Id.ToString(),
                    DedupeKey = $"consult_done_{appointment.Id}",
                    IsRead = false,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            throw new ConflictException("Hồ sơ khám đã được cập nhật bởi một phiên làm việc khác. Vui lòng tải lại trang.");
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

            if (appointment.Status != AppointmentStatus.Confirmed && appointment.Status != AppointmentStatus.CheckedIn && appointment.Status != AppointmentStatus.Pending)
                throw new BusinessException("INVALID_STATE", "Chỉ có thể đánh dấu NoShow cho lịch hẹn chưa hoàn thành.");

            if (appointment.AppointmentDate > _dateTimeProvider.VietnamToday ||
               (appointment.AppointmentDate == _dateTimeProvider.VietnamToday && appointment.StartTime > _dateTimeProvider.VietnamTime))
            {
                throw new BusinessException("INVALID_TIME", "Chưa đến thời gian khám, không thể đánh dấu vắng mặt.");
            }

            var oldStatus = appointment.Status;
            appointment.Status = AppointmentStatus.NoShow;

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = appointment.Id,
                Action = AppointmentHistoryAction.NoShow,
                OldStatus = oldStatus,
                NewStatus = AppointmentStatus.NoShow,
                Note = request.Reason ?? "Bệnh nhân không có mặt tại phòng khám vào giờ hẹn",
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

            if (request.SuggestedDate <= _dateTimeProvider.VietnamToday)
                throw new BusinessException("INVALID_DATE", "Ngày hẹn tái khám phải sau ngày hôm nay.");

            if (request.SuggestedDate.DayOfWeek == DayOfWeek.Sunday)
                throw new BusinessException("INVALID_DATE", "Không thể đề xuất tái khám vào Chủ nhật vì phòng khám không làm việc.");

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
            await _dbContext.SaveChangesAsync(); // Generate the real ID before building notification links and dedupe keys.

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

            var patientUserId = await _dbContext.Patients
                .Where(p => p.Id == appointment.PatientId)
                .Select(p => p.UserId)
                .FirstOrDefaultAsync();

            if (patientUserId.HasValue && patientUserId.Value != Guid.Empty)
            {
                _dbContext.Notifications.Add(new Notification
                {
                    UserId = patientUserId.Value,
                    Type = NotificationType.Revisit,
                    Title = "Đề xuất tái khám mới",
                    Message = $"Bác sĩ đã gửi đề xuất tái khám sau buổi khám #{appointment.AppointmentCode}. Vui lòng xác nhận lịch tái khám.",
                    Route = "/patient/revisit",
                    RelatedEntityType = "RevisitRequest",
                    RelatedEntityId = revisitReq.Id.ToString(),
                    DedupeKey = $"revisit_req_{revisitReq.Id}",
                    IsRead = false,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new RevisitRequestDto
            {
                Id = revisitReq.Id,
                AppointmentId = revisitReq.AppointmentId,
                PatientId = revisitReq.PatientId,
                DoctorId = revisitReq.DoctorId,
                SpecialtyId = appointment.SpecialtyId,
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

    public async Task<ClinicalEncounterDto?> GetEncounterAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var encounter = await _dbContext.VisitSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(vs => vs.AppointmentId == appointmentId && vs.DoctorId == doctor.Id);

        if (encounter == null) return null;

        var doctorUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == doctor.UserId);
        return MapEncounterToDto(encounter, doctorUser?.FullName ?? "Bác sĩ");
    }

    public async Task<ClinicalEncounterDto> SaveEncounterAsync(long appointmentId, SaveEncounterRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();

        var appointment = await _dbContext.Appointments
            .Include(a => a.VisitSummary)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        if (appointment.Status != AppointmentStatus.InConsultation && appointment.Status != AppointmentStatus.CheckedIn)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể ghi nhận diễn tiến khám khi đang trong phiên khám.");

        var summary = appointment.VisitSummary;
        if (summary == null)
        {
            summary = new VisitSummary
            {
                AppointmentId = appointment.Id,
                DoctorId = doctor.Id,
                ChiefComplaint = request.ChiefComplaint,
                ClinicalFindings = request.ClinicalFindings,
                Diagnosis = request.Diagnosis,
                DiagnosisCode = request.DiagnosisCode,
                TreatmentPlan = request.TreatmentPlan,
                Summary = request.Summary ?? "Ghi chép lâm sàng",
                FollowUpInstruction = request.FollowUpInstruction,
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.VisitSummaries.Add(summary);
        }
        else
        {
            ValidateRowVersion(summary.RowVersion, request.RowVersion);

            summary.ChiefComplaint = request.ChiefComplaint;
            summary.ClinicalFindings = request.ClinicalFindings;
            summary.Diagnosis = request.Diagnosis;
            summary.DiagnosisCode = request.DiagnosisCode;
            summary.TreatmentPlan = request.TreatmentPlan;
            summary.Summary = request.Summary ?? summary.Summary;
            summary.FollowUpInstruction = request.FollowUpInstruction;
            summary.UpdatedAtUtc = DateTime.UtcNow;
            summary.RowVersion = Guid.NewGuid().ToByteArray();
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Hồ sơ khám đã được cập nhật bởi một phiên làm việc khác. Vui lòng tải lại trang.");
        }

        var doctorUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == doctor.UserId);
        return MapEncounterToDto(summary, doctorUser?.FullName ?? "Bác sĩ");
    }

    public async Task<VitalSignsDto?> GetVitalSignsAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var vitals = await _dbContext.AppointmentVitalSigns
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.AppointmentId == appointmentId && v.Appointment != null && v.Appointment.DoctorId == doctor.Id);

        if (vitals == null) return null;

        var recorder = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == vitals.RecordedByUserId);
        return MapVitalsToDto(vitals, recorder?.FullName ?? "Nhân viên y tế");
    }

    public async Task<VitalSignsDto> SaveVitalSignsAsync(long appointmentId, SaveVitalSignsRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var appointment = await _dbContext.Appointments
            .Include(a => a.VitalSigns)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        if (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.NoShow)
            throw new BusinessException("INVALID_STATE", "Không thể chỉnh sửa dấu hiệu sinh tồn cho lịch hẹn đã kết thúc.");

        if (request.Weight.HasValue && request.Weight.Value <= 0)
            throw new BusinessException("VALIDATION_ERROR", "Cân nặng phải lớn hơn 0.");

        if (request.Height.HasValue && request.Height.Value <= 0)
            throw new BusinessException("VALIDATION_ERROR", "Chiều cao phải lớn hơn 0.");

        if (request.BloodPressureSystolic.HasValue != request.BloodPressureDiastolic.HasValue)
            throw new BusinessException("VALIDATION_ERROR", "Huyết áp tâm thu và tâm trương phải cùng có hoặc cùng để trống.");

        if (request.SpO2.HasValue && (request.SpO2.Value < 0 || request.SpO2.Value > 100))
            throw new BusinessException("VALIDATION_ERROR", "Chỉ số SpO2 phải nằm trong khoảng 0 - 100%.");

        var vitals = appointment.VitalSigns;
        var computedBmi = AppointmentVitalSigns.CalculateBmi(request.Weight, request.Height);

        if (vitals == null)
        {
            var linkedVisit = await _dbContext.PatientVisits.FirstOrDefaultAsync(v => v.AppointmentId == appointment.Id);
            vitals = new AppointmentVitalSigns
            {
                AppointmentId = appointment.Id,
                PatientVisitId = linkedVisit?.Id,
                Temperature = request.Temperature,
                BloodPressureSystolic = request.BloodPressureSystolic,
                BloodPressureDiastolic = request.BloodPressureDiastolic,
                HeartRate = request.HeartRate,
                RespiratoryRate = request.RespiratoryRate,
                Weight = request.Weight,
                Height = request.Height,
                Bmi = computedBmi,
                SpO2 = request.SpO2,
                RecordedAtUtc = DateTime.UtcNow,
                RecordedByUserId = userId
            };
            _dbContext.AppointmentVitalSigns.Add(vitals);
        }
        else
        {
            ValidateRowVersion(vitals.RowVersion, request.RowVersion);

            if (!vitals.PatientVisitId.HasValue)
            {
                var linkedVisit = await _dbContext.PatientVisits.FirstOrDefaultAsync(v => v.AppointmentId == appointment.Id);
                if (linkedVisit != null) vitals.PatientVisitId = linkedVisit.Id;
            }

            vitals.Temperature = request.Temperature;
            vitals.BloodPressureSystolic = request.BloodPressureSystolic;
            vitals.BloodPressureDiastolic = request.BloodPressureDiastolic;
            vitals.HeartRate = request.HeartRate;
            vitals.RespiratoryRate = request.RespiratoryRate;
            vitals.Weight = request.Weight;
            vitals.Height = request.Height;
            vitals.Bmi = computedBmi;
            vitals.SpO2 = request.SpO2;
            vitals.RecordedAtUtc = DateTime.UtcNow;
            vitals.RecordedByUserId = userId;
            vitals.RowVersion = Guid.NewGuid().ToByteArray();
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu dấu hiệu sinh tồn đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại.");
        }

        var recorder = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        return MapVitalsToDto(vitals, recorder?.FullName ?? "Nhân viên y tế");
    }

    public async Task<PrescriptionDraftDto?> GetPrescriptionDraftAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var prescription = await _dbContext.Prescriptions
            .Include(p => p.Items)
                .ThenInclude(i => i.Medicine)
            .Include(p => p.Patient)
            .FirstOrDefaultAsync(p => p.AppointmentId == appointmentId && p.DoctorId == doctor.Id);

        if (prescription == null) return null;

        var doctorUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == doctor.UserId);
        var patientUser = prescription.Patient != null && prescription.Patient.UserId.HasValue
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == prescription.Patient.UserId.Value)
            : null;

        return MapPrescriptionToDto(prescription, doctorUser?.FullName ?? "Bác sĩ", patientUser?.FullName ?? prescription.Patient?.FullName ?? "Bệnh nhân");
    }

    public async Task<PrescriptionDraftDto> SavePrescriptionDraftAsync(long appointmentId, SavePrescriptionDraftRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();

        var appointment = await _dbContext.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Prescription)
                .ThenInclude(p => p!.Items)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null) throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        if (appointment.Status != AppointmentStatus.InConsultation && appointment.Status != AppointmentStatus.CheckedIn)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể soạn đơn thuốc trong phiên khám.");

        var prescription = appointment.Prescription;
        if (prescription == null)
        {
            prescription = new Prescription
            {
                AppointmentId = appointment.Id,
                PatientId = appointment.PatientId,
                DoctorId = doctor.Id,
                Status = PrescriptionStatus.Draft,
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Prescriptions.Add(prescription);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            if (prescription.Status == PrescriptionStatus.Dispensed)
                throw new BusinessException("ALREADY_DISPENSED", "Đơn thuốc đã cấp phát không thể chỉnh sửa.");

            ValidateRowVersion(prescription.RowVersion, request.RowVersion);

            prescription.Notes = request.Notes;
            prescription.Status = PrescriptionStatus.Draft;
            prescription.CreatedAt = DateTime.UtcNow;
            prescription.RowVersion = Guid.NewGuid().ToByteArray();

            _dbContext.PrescriptionItems.RemoveRange(prescription.Items);
        }

        foreach (var item in request.Items)
        {
            var medExists = await _dbContext.Medicines.AnyAsync(m => m.Id == item.MedicineId && m.IsActive);
            if (!medExists)
                throw new BusinessException("INVALID_MEDICINE", $"Thuốc với ID {item.MedicineId} không tồn tại hoặc ngừng hoạt động.");

            _dbContext.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = prescription.Id,
                MedicineId = item.MedicineId,
                Quantity = item.Quantity,
                Dosage = item.Dosage ?? string.Empty,
                Frequency = item.Frequency ?? string.Empty,
                DurationDays = item.DurationDays,
                Instructions = item.Instructions
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Đơn thuốc đã được cập nhật bởi một phiên làm việc khác. Vui lòng tải lại.");
        }

        return (await GetPrescriptionDraftAsync(appointmentId))!;
    }

    private static void ValidateRowVersion(byte[]? entityVersion, string? clientVersion)
    {
        if (entityVersion != null && entityVersion.Length > 0 && !string.IsNullOrEmpty(clientVersion))
        {
            try
            {
                var clientBytes = Convert.FromBase64String(clientVersion);
                if (!entityVersion.SequenceEqual(clientBytes))
                {
                    throw new ConflictException("CONCURRENCY_CONFLICT", "Dữ liệu đã bị sửa đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
                }
            }
            catch (FormatException)
            {
                throw new ConflictException("INVALID_ROW_VERSION", "RowVersion không hợp lệ.");
            }
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

    private static VitalSignsDto MapVitalsToDto(AppointmentVitalSigns v, string recorderName) => new()
    {
        Id = v.Id,
        AppointmentId = v.AppointmentId,
        PatientVisitId = v.PatientVisitId,
        VisitCode = v.PatientVisit?.VisitCode,
        Temperature = v.Temperature,
        BloodPressureSystolic = v.BloodPressureSystolic,
        BloodPressureDiastolic = v.BloodPressureDiastolic,
        HeartRate = v.HeartRate,
        RespiratoryRate = v.RespiratoryRate,
        Weight = v.Weight,
        Height = v.Height,
        Bmi = v.Bmi,
        SpO2 = v.SpO2,
        RecordedAtUtc = v.RecordedAtUtc,
        RecordedByUserName = recorderName,
        RowVersion = v.RowVersion != null ? Convert.ToBase64String(v.RowVersion) : null
    };

    private static ClinicalEncounterDto MapEncounterToDto(VisitSummary vs, string doctorName) => new()
    {
        Id = vs.Id,
        AppointmentId = vs.AppointmentId,
        PatientVisitId = vs.PatientVisitId,
        VisitCode = vs.PatientVisit?.VisitCode,
        DoctorId = vs.DoctorId,
        DoctorName = doctorName,
        ChiefComplaint = vs.ChiefComplaint,
        ClinicalFindings = vs.ClinicalFindings,
        Diagnosis = vs.Diagnosis,
        DiagnosisCode = vs.DiagnosisCode,
        TreatmentPlan = vs.TreatmentPlan,
        Summary = vs.Summary,
        FollowUpInstruction = vs.FollowUpInstruction,
        CreatedAtUtc = vs.CreatedAtUtc,
        UpdatedAtUtc = vs.UpdatedAtUtc,
        CompletedAtUtc = vs.CompletedAtUtc,
        RowVersion = vs.RowVersion != null ? Convert.ToBase64String(vs.RowVersion) : null
    };

    private static PrescriptionDraftDto MapPrescriptionToDto(Prescription p, string doctorName, string patientName) => new()
    {
        Id = p.Id,
        AppointmentId = p.AppointmentId,
        PatientVisitId = p.PatientVisitId,
        VisitCode = p.PatientVisit?.VisitCode,
        DoctorId = p.DoctorId,
        DoctorName = doctorName,
        PatientId = p.PatientId,
        PatientName = patientName,
        Status = p.Status.ToString(),
        Notes = p.Notes,
        CreatedAt = p.CreatedAt,
        RowVersion = p.RowVersion != null ? Convert.ToBase64String(p.RowVersion) : null,
        Items = p.Items.Select(i => new PrescriptionDraftItemDto
        {
            MedicineId = i.MedicineId,
            MedicineCode = i.Medicine?.Code ?? string.Empty,
            MedicineName = i.Medicine?.Name ?? "Thuốc",
            Unit = i.Medicine?.Unit ?? "Hộp",
            Quantity = i.Quantity,
            AvailableStock = i.Medicine?.StockQuantity ?? 0,
            Dosage = i.Dosage,
            Frequency = i.Frequency,
            DurationDays = i.DurationDays,
            Instructions = i.Instructions
        }).ToList()
    };

    public async Task<PatientClinicalContextDto> GetVisitClinicalContextAsync(long visitId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var visit = await _dbContext.PatientVisits
            .Include(v => v.Patient)
            .Include(v => v.Department)
                .ThenInclude(d => d.Specialty)
            .Include(v => v.Appointment)
                .ThenInclude(a => a!.Specialty)
            .Include(v => v.VitalSigns)
            .Include(v => v.VisitSummary)
            .Include(v => v.Prescriptions)
                .ThenInclude(p => p.Items)
                    .ThenInclude(i => i.Medicine)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        if (visit == null)
            throw new NotFoundException("Lượt khám không tồn tại.");

        if (!visit.AssignedDoctorId.HasValue)
        {
            visit.AssignedDoctorId = doctor.Id;
            await _dbContext.SaveChangesAsync();
        }

        var patient = visit.Patient;
        var patientName = patient?.FullName ?? "Bệnh nhân";
        var patientPhone = patient?.PhoneNumber ?? string.Empty;
        var doctorUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == doctor.UserId);

        DoctorAppointmentDto currentAppointment;
        if (visit.Appointment != null)
        {
            currentAppointment = MapToDto(visit.Appointment, patientName, patientPhone, patient?.Gender, patient?.DateOfBirth);
        }
        else
        {
            currentAppointment = new DoctorAppointmentDto
            {
                Id = 0,
                AppointmentCode = visit.VisitCode,
                PatientId = visit.PatientId,
                DoctorId = doctor.Id,
                SpecialtyId = visit.Department?.SpecialtyId ?? 0,
                AppointmentDate = visit.VisitDate,
                StartTime = TimeOnly.FromDateTime(visit.CheckedInAtUtc),
                EndTime = TimeOnly.FromDateTime(visit.CheckedInAtUtc).AddMinutes(15),
                Reason = visit.ChiefComplaint,
                Status = visit.Status.ToString(),
                PatientName = patientName,
                PatientPhone = patientPhone,
                PatientGender = patient?.Gender.HasValue == true ? patient.Gender.Value.ToString() : string.Empty,
                PatientDob = patient?.DateOfBirth,
                SpecialtyName = visit.Department?.Specialty?.Name ?? visit.Department?.Name ?? string.Empty
            };
        }

        var patientId = visit.PatientId;

        var pastAppointments = await _dbContext.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor)
            .Include(a => a.Specialty)
            .Include(a => a.VisitSummary)
            .Include(a => a.Prescription)
                .ThenInclude(p => p!.Items)
                    .ThenInclude(i => i.Medicine)
            .Where(a => a.PatientId == patientId && a.Id != (visit.AppointmentId ?? 0) && a.Status == AppointmentStatus.Completed)
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .Take(20)
            .ToListAsync();

        var pastDoctorUserIds = pastAppointments.Select(a => a.Doctor.UserId).Distinct().ToList();

        var pastWalkInVisits = await _dbContext.PatientVisits
            .AsNoTracking()
            .Include(v => v.AssignedDoctor)
            .Include(v => v.Department)
                .ThenInclude(d => d.Specialty)
            .Include(v => v.VisitSummary)
            .Include(v => v.Prescriptions)
                .ThenInclude(p => p.Items)
                    .ThenInclude(i => i.Medicine)
            .Where(v => v.PatientId == patientId && v.Id != visit.Id && v.AppointmentId == null &&
                        (v.Status == VisitStatus.Completed || v.Status == VisitStatus.ConsultationCompleted ||
                         v.Status == VisitStatus.InPharmacy || v.Status == VisitStatus.InBilling))
            .OrderByDescending(v => v.VisitDate)
            .ThenByDescending(v => v.CheckedInAtUtc)
            .Take(20)
            .ToListAsync();

        var pastWalkInDoctorUserIds = pastWalkInVisits
            .Where(v => v.AssignedDoctor != null)
            .Select(v => v.AssignedDoctor!.UserId)
            .Distinct()
            .ToList();

        var allDoctorUserIds = pastDoctorUserIds.Concat(pastWalkInDoctorUserIds).Distinct().ToList();
        var pastDoctorUsers = await _dbContext.Users
            .Where(u => allDoctorUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var pastVisitDtos = new List<PastVisitSummaryDto>();

        foreach (var a in pastAppointments)
        {
            pastDoctorUsers.TryGetValue(a.Doctor.UserId, out var docName);
            pastVisitDtos.Add(new PastVisitSummaryDto
            {
                AppointmentId = a.Id,
                AppointmentCode = a.AppointmentCode,
                Date = a.AppointmentDate,
                DoctorName = docName ?? "Bác sĩ",
                SpecialtyName = a.Specialty?.Name ?? string.Empty,
                Diagnosis = a.VisitSummary?.Diagnosis,
                Summary = a.VisitSummary?.Summary,
                PrescriptionItemNames = a.Prescription?.Items.Select(i => i.Medicine?.Name ?? "Thuốc").ToList() ?? new List<string>()
            });
        }

        foreach (var v in pastWalkInVisits)
        {
            string? docName = null;
            if (v.AssignedDoctor != null) pastDoctorUsers.TryGetValue(v.AssignedDoctor.UserId, out docName);
            pastVisitDtos.Add(new PastVisitSummaryDto
            {
                AppointmentId = 0,
                PatientVisitId = v.Id,
                AppointmentCode = v.VisitCode,
                Date = v.VisitDate,
                DoctorName = docName ?? "Bác sĩ",
                SpecialtyName = v.Department?.Specialty?.Name ?? v.Department?.Name ?? string.Empty,
                Diagnosis = v.VisitSummary?.Diagnosis,
                Summary = v.VisitSummary?.Summary,
                PrescriptionItemNames = v.Prescriptions.SelectMany(p => p.Items).Select(i => i.Medicine?.Name ?? "Thuốc").ToList()
            });
        }

        pastVisitDtos = pastVisitDtos
            .OrderByDescending(p => p.Date)
            .Take(20)
            .ToList();

        VitalSignsDto? vitalsDto = null;
        var currentVitals = visit.VitalSigns ?? (visit.AppointmentId.HasValue
            ? await _dbContext.AppointmentVitalSigns.FirstOrDefaultAsync(v => v.AppointmentId == visit.AppointmentId.Value)
            : null);

        if (currentVitals != null)
        {
            var recorder = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentVitals.RecordedByUserId);
            vitalsDto = MapVitalsToDto(currentVitals, recorder?.FullName ?? "Nhân viên y tế");
        }

        var allVitalSigns = await _dbContext.AppointmentVitalSigns
            .AsNoTracking()
            .Include(vs => vs.Appointment)
            .Include(vs => vs.PatientVisit)
            .Where(vs =>
                (vs.Appointment != null && vs.Appointment.PatientId == patientId &&
                 vs.Appointment.Status != AppointmentStatus.Cancelled &&
                 vs.Appointment.Status != AppointmentStatus.NoShow) ||
                (vs.PatientVisit != null && vs.PatientVisit.PatientId == patientId &&
                 vs.PatientVisit.Status != VisitStatus.Cancelled))
            .OrderByDescending(vs => vs.RecordedAtUtc)
            .Take(20)
            .ToListAsync();

        var vitalRecorderUserIds = allVitalSigns
            .Select(vs => vs.RecordedByUserId)
            .Distinct()
            .ToList();

        var vitalRecorderUsers = await _dbContext.Users
            .Where(u => vitalRecorderUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var vitalHistoryDtos = allVitalSigns.Select(vs =>
        {
            vitalRecorderUsers.TryGetValue(vs.RecordedByUserId, out var recName);
            return new PatientVitalHistoryItemDto
            {
                AppointmentId = vs.AppointmentId ?? 0,
                PatientVisitId = vs.PatientVisitId,
                AppointmentCode = vs.Appointment?.AppointmentCode ?? vs.PatientVisit?.VisitCode ?? "VISIT",
                AppointmentDate = vs.Appointment?.AppointmentDate ?? (vs.PatientVisit != null ? vs.PatientVisit.VisitDate : DateOnly.FromDateTime(vs.RecordedAtUtc)),
                RecordedAtUtc = vs.RecordedAtUtc,
                Height = vs.Height,
                Weight = vs.Weight,
                Bmi = vs.Bmi,
                Temperature = vs.Temperature,
                BloodPressureSystolic = vs.BloodPressureSystolic,
                BloodPressureDiastolic = vs.BloodPressureDiastolic,
                HeartRate = vs.HeartRate,
                RespiratoryRate = vs.RespiratoryRate,
                SpO2 = vs.SpO2,
                RecordedByUserName = recName ?? "Nhân viên y tế"
            };
        }).ToList();

        var currentMeasurement = vitalsDto != null
            ? new PatientVitalHistoryItemDto
            {
                AppointmentId = currentVitals?.AppointmentId ?? 0,
                PatientVisitId = currentVitals?.PatientVisitId ?? visit.Id,
                AppointmentCode = visit.Appointment != null ? visit.Appointment.AppointmentCode : visit.VisitCode,
                AppointmentDate = visit.VisitDate,
                RecordedAtUtc = currentVitals?.RecordedAtUtc ?? DateTime.UtcNow,
                Height = currentVitals?.Height,
                Weight = currentVitals?.Weight,
                Bmi = currentVitals?.Bmi,
                Temperature = currentVitals?.Temperature,
                BloodPressureSystolic = currentVitals?.BloodPressureSystolic,
                BloodPressureDiastolic = currentVitals?.BloodPressureDiastolic,
                HeartRate = currentVitals?.HeartRate,
                RespiratoryRate = currentVitals?.RespiratoryRate,
                SpO2 = currentVitals?.SpO2,
                RecordedByUserName = vitalsDto.RecordedByUserName
            }
            : null;

        var previousMeasurement = vitalHistoryDtos.FirstOrDefault(vh =>
            (vh.PatientVisitId == null || vh.PatientVisitId != visit.Id) &&
            (!visit.AppointmentId.HasValue || vh.AppointmentId != visit.AppointmentId.Value));

        AnthropometricComparisonDto? anthropometricComparison = null;
        if (currentMeasurement != null || previousMeasurement != null)
        {
            decimal? weightDelta = null;
            if (currentMeasurement?.Weight.HasValue == true && previousMeasurement?.Weight.HasValue == true)
                weightDelta = Math.Round(currentMeasurement.Weight.Value - previousMeasurement.Weight.Value, 2);

            decimal? heightDelta = null;
            if (currentMeasurement?.Height.HasValue == true && previousMeasurement?.Height.HasValue == true)
                heightDelta = Math.Round(currentMeasurement.Height.Value - previousMeasurement.Height.Value, 2);

            decimal? bmiDelta = null;
            if (currentMeasurement?.Bmi.HasValue == true && previousMeasurement?.Bmi.HasValue == true)
                bmiDelta = Math.Round(currentMeasurement.Bmi.Value - previousMeasurement.Bmi.Value, 2);

            anthropometricComparison = new AnthropometricComparisonDto
            {
                CurrentMeasurement = currentMeasurement,
                PreviousMeasurement = previousMeasurement,
                WeightDeltaKg = weightDelta,
                HeightDeltaCm = heightDelta,
                BmiDelta = bmiDelta,
                HasComparableData = weightDelta.HasValue || heightDelta.HasValue || bmiDelta.HasValue
            };
        }

        ClinicalEncounterDto? encounterDto = null;
        var currentEncounter = visit.VisitSummary ?? (visit.AppointmentId.HasValue
            ? await _dbContext.VisitSummaries.FirstOrDefaultAsync(vs => vs.AppointmentId == visit.AppointmentId.Value)
            : null);

        if (currentEncounter != null)
        {
            encounterDto = MapEncounterToDto(currentEncounter, doctorUser?.FullName ?? "Bác sĩ");
        }

        PrescriptionDraftDto? presDto = null;
        var currentPrescription = visit.Prescriptions.FirstOrDefault() ?? (visit.AppointmentId.HasValue
            ? await _dbContext.Prescriptions.Include(p => p.Items).ThenInclude(i => i.Medicine).FirstOrDefaultAsync(p => p.AppointmentId == visit.AppointmentId.Value)
            : null);

        if (currentPrescription != null)
        {
            presDto = MapPrescriptionToDto(currentPrescription, doctorUser?.FullName ?? "Bác sĩ", patientName);
        }

        var diagnosticOrders = await _dbContext.DiagnosticOrders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.DiagnosticService)
            .Include(o => o.Items)
                .ThenInclude(i => i.Result)
            .Where(o => o.PatientVisitId == visit.Id || (visit.AppointmentId.HasValue && o.AppointmentId == visit.AppointmentId.Value))
            .OrderByDescending(o => o.OrderedAtUtc)
            .ToListAsync();

        var diagnosticOrderDtos = diagnosticOrders.Select(o => new DiagnosticOrderDto
        {
            Id = o.Id,
            OrderCode = o.OrderCode,
            AppointmentId = o.AppointmentId,
            PatientVisitId = o.PatientVisitId,
            OrderingDoctorId = o.OrderingDoctorId,
            OrderingDoctorName = doctorUser?.FullName ?? "Bác sĩ",
            PatientId = o.PatientId,
            PatientName = patientName,
            PatientPhone = patientPhone,
            Status = o.Status.ToString(),
            ClinicalIndication = o.ClinicalIndication,
            OrderedAtUtc = o.OrderedAtUtc,
            Items = o.Items.Select(i => new DiagnosticOrderItemDto
            {
                Id = i.Id,
                DiagnosticOrderId = i.DiagnosticOrderId,
                DiagnosticServiceId = i.DiagnosticServiceId,
                ServiceName = i.DiagnosticService?.Name ?? "Dịch vụ",
                ServiceCode = i.DiagnosticService?.Code ?? string.Empty,
                Category = i.DiagnosticService?.Category.ToString() ?? string.Empty,
                Status = i.Status.ToString(),
                Result = i.Result != null ? new DiagnosticResultDto
                {
                    Id = i.Result.Id,
                    DiagnosticOrderItemId = i.Result.DiagnosticOrderItemId,
                    ResultText = i.Result.ResultText,
                    Conclusion = i.Result.Conclusion,
                    ReferenceRange = i.Result.ReferenceRange,
                    Unit = i.Result.Unit,
                    ResultedAtUtc = i.Result.ResultedAtUtc
                } : null
            }).ToList()
        }).ToList();

        return new PatientClinicalContextDto
        {
            PatientId = visit.PatientId,
            PatientName = patientName,
            PatientPhone = patientPhone,
            PatientGender = patient?.Gender.HasValue == true ? patient.Gender.Value.ToString() : string.Empty,
            PatientDob = patient?.DateOfBirth,
            Address = patient?.Address,
            TotalPastVisits = pastVisitDtos.Count,
            PastVisits = pastVisitDtos,
            CurrentAppointment = currentAppointment,
            VitalSigns = vitalsDto,
            LatestKnownVitals = vitalsDto == null && previousMeasurement != null ? new VitalSignsDto
            {
                AppointmentId = previousMeasurement.AppointmentId,
                Temperature = previousMeasurement.Temperature,
                BloodPressureSystolic = previousMeasurement.BloodPressureSystolic,
                BloodPressureDiastolic = previousMeasurement.BloodPressureDiastolic,
                HeartRate = previousMeasurement.HeartRate,
                RespiratoryRate = previousMeasurement.RespiratoryRate,
                Weight = previousMeasurement.Weight,
                Height = previousMeasurement.Height,
                Bmi = previousMeasurement.Bmi,
                SpO2 = previousMeasurement.SpO2,
                RecordedAtUtc = previousMeasurement.RecordedAtUtc,
                RecordedByUserName = previousMeasurement.RecordedByUserName
            } : null,
            Encounter = encounterDto,
            Prescription = presDto,
            VitalHistory = vitalHistoryDtos,
            AnthropometricComparison = anthropometricComparison,
            PatientVisitId = visit.Id,
            VisitCode = visit.VisitCode,
            QueueNumber = visit.QueueNumber,
            Mrn = patient?.MedicalRecordNumber,
            VisitStatus = visit.Status.ToString(),
            DiagnosticOrders = diagnosticOrderDtos
        };
    }

    public async Task StartVisitConsultationAsync(long visitId)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var visit = await _dbContext.PatientVisits
            .Include(v => v.Appointment)
            .Include(v => v.VisitSummary)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        if (visit == null) throw new NotFoundException("Lượt khám không tồn tại.");

        if (visit.Status != VisitStatus.WaitingDoctor &&
            visit.Status != VisitStatus.InConsultation &&
            visit.Status != VisitStatus.ResultsReady)
        {
            throw new BusinessException("INVALID_STATE_TRANSITION", "Chỉ có thể bắt đầu khám khi bệnh nhân đang chờ hoặc đã có kết quả cận lâm sàng.");
        }

        visit.Status = VisitStatus.InConsultation;
        visit.AssignedDoctorId = doctor.Id;

        if (visit.Appointment != null && visit.Appointment.Status != AppointmentStatus.InConsultation)
        {
            var oldAptStatus = visit.Appointment.Status;
            visit.Appointment.Status = AppointmentStatus.InConsultation;

            _dbContext.AppointmentHistories.Add(new AppointmentHistory
            {
                AppointmentId = visit.Appointment.Id,
                Action = AppointmentHistoryAction.InConsultation,
                OldStatus = oldAptStatus,
                NewStatus = AppointmentStatus.InConsultation,
                Note = "Bác sĩ bắt đầu phiên khám lâm sàng từ lượt khám",
                PerformedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (visit.VisitSummary == null)
        {
            visit.VisitSummary = new VisitSummary
            {
                PatientVisitId = visit.Id,
                AppointmentId = visit.AppointmentId,
                DoctorId = doctor.Id,
                ChiefComplaint = visit.ChiefComplaint,
                Summary = "Đang trong quá trình thăm khám.",
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.VisitSummaries.Add(visit.VisitSummary);
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task CompleteVisitConsultationAsync(long visitId, CompleteConsultationRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var visit = await _dbContext.PatientVisits
            .Include(v => v.Patient)
            .Include(v => v.Appointment)
            .Include(v => v.VisitSummary)
            .Include(v => v.Prescriptions)
                .ThenInclude(p => p.Items)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        if (visit == null) throw new NotFoundException("Lượt khám không tồn tại.");

        if (string.IsNullOrWhiteSpace(request.Summary) && string.IsNullOrWhiteSpace(request.Diagnosis))
            throw new BusinessException("VALIDATION_ERROR", "Tóm tắt kết luận khám không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Diagnosis))
            request.Diagnosis = !string.IsNullOrWhiteSpace(request.Summary) ? request.Summary : "Khám lâm sàng";

        if (string.IsNullOrWhiteSpace(request.Summary))
            request.Summary = request.Diagnosis;

        using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            if (visit.Status != VisitStatus.InConsultation && visit.Status != VisitStatus.ResultsReady)
                throw new BusinessException("INVALID_STATE_TRANSITION", "Chỉ có thể hoàn tất lượt khám đang trong phiên khám (InConsultation).");

            var diagnosticOrders = await _dbContext.DiagnosticOrders
                .Where(o => o.PatientVisitId == visit.Id || (visit.AppointmentId.HasValue && o.AppointmentId == visit.AppointmentId.Value))
                .ToListAsync();

            var pendingOrders = diagnosticOrders
                .Where(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress)
                .ToList();

            if (pendingOrders.Count > 0)
            {
                throw new BusinessException("PENDING_DIAGNOSTIC_RESULTS", "Không thể hoàn tất phiên khám khi còn chỉ định cận lâm sàng đang chờ kết quả.");
            }

            var unreviewedOrders = diagnosticOrders
                .Where(o => o.Status == DiagnosticOrderStatus.Completed && !o.ReviewedAtUtc.HasValue)
                .ToList();

            if (unreviewedOrders.Count > 0)
            {
                throw new BusinessException("UNREVIEWED_DIAGNOSTIC_RESULTS", "Không thể hoàn tất phiên khám khi có kết quả cận lâm sàng chưa được bác sĩ xem và xác nhận.");
            }

            visit.Status = VisitStatus.ConsultationCompleted;

            if (visit.Appointment != null)
            {
                var oldStatus = visit.Appointment.Status;
                visit.Appointment.Status = AppointmentStatus.Completed;

                _dbContext.AppointmentHistories.Add(new AppointmentHistory
                {
                    AppointmentId = visit.Appointment.Id,
                    Action = AppointmentHistoryAction.Completed,
                    OldStatus = oldStatus,
                    NewStatus = AppointmentStatus.Completed,
                    Note = "Bác sĩ hoàn thành phiên khám lâm sàng từ lượt khám",
                    PerformedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var summary = visit.VisitSummary ?? (visit.AppointmentId.HasValue
                ? await _dbContext.VisitSummaries.FirstOrDefaultAsync(vs => vs.AppointmentId == visit.AppointmentId.Value)
                : null);

            if (summary == null)
            {
                summary = new VisitSummary
                {
                    PatientVisitId = visit.Id,
                    AppointmentId = visit.AppointmentId,
                    DoctorId = doctor.Id,
                    ChiefComplaint = request.ChiefComplaint ?? visit.ChiefComplaint,
                    ClinicalFindings = request.ClinicalFindings,
                    Diagnosis = request.Diagnosis,
                    DiagnosisCode = request.DiagnosisCode,
                    TreatmentPlan = request.TreatmentPlan,
                    Summary = request.Summary,
                    FollowUpInstruction = request.FollowUpInstruction,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                };
                _dbContext.VisitSummaries.Add(summary);
            }
            else
            {
                ValidateRowVersion(summary.RowVersion, request.EncounterRowVersion);

                summary.PatientVisitId = visit.Id;
                summary.ChiefComplaint = request.ChiefComplaint ?? summary.ChiefComplaint;
                summary.ClinicalFindings = request.ClinicalFindings;
                summary.Diagnosis = request.Diagnosis;
                summary.DiagnosisCode = request.DiagnosisCode;
                summary.TreatmentPlan = request.TreatmentPlan;
                summary.Summary = request.Summary;
                summary.FollowUpInstruction = request.FollowUpInstruction;
                summary.UpdatedAtUtc = DateTime.UtcNow;
                summary.CompletedAtUtc = DateTime.UtcNow;
            }

            bool hasPrescription = false;
            if (request.IssuePrescription && request.PrescriptionItems != null && request.PrescriptionItems.Count > 0)
            {
                hasPrescription = true;
                var prescription = visit.Prescriptions.FirstOrDefault() ?? (visit.AppointmentId.HasValue
                    ? await _dbContext.Prescriptions.Include(p => p.Items).FirstOrDefaultAsync(p => p.AppointmentId == visit.AppointmentId.Value)
                    : null);

                if (prescription == null)
                {
                    prescription = new Prescription
                    {
                        PatientVisitId = visit.Id,
                        AppointmentId = visit.AppointmentId,
                        PatientId = visit.PatientId,
                        DoctorId = doctor.Id,
                        Status = PrescriptionStatus.Issued,
                        Notes = request.PrescriptionNotes,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.Prescriptions.Add(prescription);
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    ValidateRowVersion(prescription.RowVersion, request.PrescriptionRowVersion);
                    prescription.PatientVisitId = visit.Id;
                    prescription.Status = PrescriptionStatus.Issued;
                    prescription.Notes = request.PrescriptionNotes;
                    prescription.CreatedAt = DateTime.UtcNow;

                    _dbContext.PrescriptionItems.RemoveRange(prescription.Items);
                }

                foreach (var item in request.PrescriptionItems)
                {
                    var medExists = await _dbContext.Medicines.AnyAsync(m => m.Id == item.MedicineId && m.IsActive);
                    if (!medExists)
                        throw new BusinessException("INVALID_MEDICINE", $"Thuốc với ID {item.MedicineId} không tồn tại hoặc đã ngừng cung cấp.");

                    _dbContext.PrescriptionItems.Add(new PrescriptionItem
                    {
                        PrescriptionId = prescription.Id,
                        MedicineId = item.MedicineId,
                        Quantity = item.Quantity,
                        Dosage = item.Dosage ?? string.Empty,
                        Frequency = item.Frequency ?? string.Empty,
                        DurationDays = item.DurationDays,
                        Instructions = item.Instructions
                    });
                }
            }
            else
            {
                var existingPres = visit.Prescriptions.FirstOrDefault() ?? (visit.AppointmentId.HasValue
                    ? await _dbContext.Prescriptions.Include(p => p.Items).FirstOrDefaultAsync(p => p.AppointmentId == visit.AppointmentId.Value)
                    : null);

                if (existingPres != null && existingPres.Status == PrescriptionStatus.Draft && existingPres.Items.Count > 0)
                {
                    existingPres.Status = PrescriptionStatus.Issued;
                    existingPres.PatientVisitId = visit.Id;
                    hasPrescription = true;
                }
            }

            visit.Status = hasPrescription ? VisitStatus.InPharmacy : VisitStatus.InBilling;

            var patientUserId = visit.Patient?.UserId;
            if (patientUserId.HasValue && patientUserId.Value != Guid.Empty)
            {
                _dbContext.Notifications.Add(new Notification
                {
                    UserId = patientUserId.Value,
                    Type = NotificationType.Appointment,
                    Title = "Buổi khám đã hoàn tất",
                    Message = $"Lượt khám #{visit.VisitCode} đã được bác sĩ hoàn tất. Chẩn đoán: {request.Diagnosis}",
                    Route = "/patient/medical-records",
                    RelatedEntityType = "PatientVisit",
                    RelatedEntityId = visit.Id.ToString(),
                    DedupeKey = $"visit_complete_{visit.Id}",
                    IsRead = false,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ClinicalEncounterDto?> GetVisitEncounterAsync(long visitId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var encounter = await _dbContext.VisitSummaries
            .AsNoTracking()
            .Include(vs => vs.PatientVisit)
            .FirstOrDefaultAsync(vs => vs.PatientVisitId == visitId || (vs.PatientVisit != null && vs.PatientVisit.Id == visitId));

        if (encounter == null)
        {
            var visit = await _dbContext.PatientVisits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId);
            if (visit?.AppointmentId != null)
            {
                encounter = await _dbContext.VisitSummaries
                    .AsNoTracking()
                    .Include(vs => vs.PatientVisit)
                    .FirstOrDefaultAsync(vs => vs.AppointmentId == visit.AppointmentId);
            }
        }

        if (encounter == null) return null;

        var doctorUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == doctor.UserId);
        return MapEncounterToDto(encounter, doctorUser?.FullName ?? "Bác sĩ");
    }

    public async Task<ClinicalEncounterDto> SaveVisitEncounterAsync(long visitId, SaveEncounterRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();

        var visit = await _dbContext.PatientVisits
            .Include(v => v.VisitSummary)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        if (visit == null) throw new NotFoundException("Lượt khám không tồn tại.");

        if (visit.Status != VisitStatus.InConsultation &&
            visit.Status != VisitStatus.WaitingDoctor &&
            visit.Status != VisitStatus.ResultsReady)
        {
            throw new BusinessException("INVALID_STATE", "Chỉ có thể ghi nhận diễn tiến khám khi đang trong phiên khám.");
        }

        var summary = visit.VisitSummary ?? (visit.AppointmentId.HasValue
            ? await _dbContext.VisitSummaries.FirstOrDefaultAsync(vs => vs.AppointmentId == visit.AppointmentId.Value)
            : null);

        if (summary == null)
        {
            summary = new VisitSummary
            {
                PatientVisitId = visit.Id,
                AppointmentId = visit.AppointmentId,
                DoctorId = doctor.Id,
                ChiefComplaint = request.ChiefComplaint ?? visit.ChiefComplaint,
                ClinicalFindings = request.ClinicalFindings,
                Diagnosis = request.Diagnosis,
                DiagnosisCode = request.DiagnosisCode,
                TreatmentPlan = request.TreatmentPlan,
                Summary = request.Summary ?? "Ghi chép lâm sàng",
                FollowUpInstruction = request.FollowUpInstruction,
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.VisitSummaries.Add(summary);
        }
        else
        {
            ValidateRowVersion(summary.RowVersion, request.RowVersion);

            summary.PatientVisitId = visit.Id;
            summary.ChiefComplaint = request.ChiefComplaint ?? summary.ChiefComplaint;
            summary.ClinicalFindings = request.ClinicalFindings;
            summary.Diagnosis = request.Diagnosis;
            summary.DiagnosisCode = request.DiagnosisCode;
            summary.TreatmentPlan = request.TreatmentPlan;
            summary.Summary = request.Summary ?? summary.Summary;
            summary.FollowUpInstruction = request.FollowUpInstruction;
            summary.UpdatedAtUtc = DateTime.UtcNow;
            summary.RowVersion = Guid.NewGuid().ToByteArray();
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Hồ sơ khám đã được cập nhật bởi một phiên làm việc khác. Vui lòng tải lại trang.");
        }

        var doctorUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == doctor.UserId);
        return MapEncounterToDto(summary, doctorUser?.FullName ?? "Bác sĩ");
    }

    public async Task<VitalSignsDto?> GetVisitVitalSignsAsync(long visitId)
    {
        var vitals = await _dbContext.AppointmentVitalSigns
            .AsNoTracking()
            .Include(v => v.PatientVisit)
            .FirstOrDefaultAsync(v => v.PatientVisitId == visitId);

        if (vitals == null)
        {
            var visit = await _dbContext.PatientVisits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId);
            if (visit?.AppointmentId != null)
            {
                vitals = await _dbContext.AppointmentVitalSigns
                    .AsNoTracking()
                    .Include(v => v.PatientVisit)
                    .FirstOrDefaultAsync(v => v.AppointmentId == visit.AppointmentId);
            }
        }

        if (vitals == null) return null;

        var recorder = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == vitals.RecordedByUserId);
        return MapVitalsToDto(vitals, recorder?.FullName ?? "Nhân viên y tế");
    }

    public async Task<VitalSignsDto> SaveVisitVitalSignsAsync(long visitId, SaveVitalSignsRequest request)
    {
        var userId = GetUserId();

        var visit = await _dbContext.PatientVisits
            .Include(v => v.VitalSigns)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        if (visit == null) throw new NotFoundException("Lượt khám không tồn tại.");

        if (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Cancelled)
            throw new BusinessException("INVALID_STATE", "Không thể chỉnh sửa dấu hiệu sinh tồn cho lượt khám đã kết thúc.");

        if (request.Weight.HasValue && request.Weight.Value <= 0)
            throw new BusinessException("VALIDATION_ERROR", "Cân nặng phải lớn hơn 0.");

        if (request.Height.HasValue && request.Height.Value <= 0)
            throw new BusinessException("VALIDATION_ERROR", "Chiều cao phải lớn hơn 0.");

        if (request.BloodPressureSystolic.HasValue != request.BloodPressureDiastolic.HasValue)
            throw new BusinessException("VALIDATION_ERROR", "Huyết áp tâm thu và tâm trương phải cùng có hoặc cùng để trống.");

        if (request.SpO2.HasValue && (request.SpO2.Value < 0 || request.SpO2.Value > 100))
            throw new BusinessException("VALIDATION_ERROR", "Chỉ số SpO2 phải nằm trong khoảng 0 - 100%.");

        var vitals = visit.VitalSigns ?? (visit.AppointmentId.HasValue
            ? await _dbContext.AppointmentVitalSigns.FirstOrDefaultAsync(v => v.AppointmentId == visit.AppointmentId.Value)
            : null);

        var computedBmi = AppointmentVitalSigns.CalculateBmi(request.Weight, request.Height);

        if (vitals == null)
        {
            vitals = new AppointmentVitalSigns
            {
                PatientVisitId = visit.Id,
                AppointmentId = visit.AppointmentId,
                Temperature = request.Temperature,
                BloodPressureSystolic = request.BloodPressureSystolic,
                BloodPressureDiastolic = request.BloodPressureDiastolic,
                HeartRate = request.HeartRate,
                RespiratoryRate = request.RespiratoryRate,
                Weight = request.Weight,
                Height = request.Height,
                Bmi = computedBmi,
                SpO2 = request.SpO2,
                RecordedAtUtc = DateTime.UtcNow,
                RecordedByUserId = userId
            };
            _dbContext.AppointmentVitalSigns.Add(vitals);
        }
        else
        {
            ValidateRowVersion(vitals.RowVersion, request.RowVersion);

            vitals.PatientVisitId = visit.Id;
            vitals.Temperature = request.Temperature;
            vitals.BloodPressureSystolic = request.BloodPressureSystolic;
            vitals.BloodPressureDiastolic = request.BloodPressureDiastolic;
            vitals.HeartRate = request.HeartRate;
            vitals.RespiratoryRate = request.RespiratoryRate;
            vitals.Weight = request.Weight;
            vitals.Height = request.Height;
            vitals.Bmi = computedBmi;
            vitals.SpO2 = request.SpO2;
            vitals.RecordedAtUtc = DateTime.UtcNow;
            vitals.RecordedByUserId = userId;
            vitals.RowVersion = Guid.NewGuid().ToByteArray();
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu dấu hiệu sinh tồn đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại.");
        }

        var recorder = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        return MapVitalsToDto(vitals, recorder?.FullName ?? "Nhân viên y tế");
    }

    public async Task<PrescriptionDraftDto?> GetVisitPrescriptionDraftAsync(long visitId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var prescription = await _dbContext.Prescriptions
            .Include(p => p.Items)
                .ThenInclude(i => i.Medicine)
            .Include(p => p.Patient)
            .Include(p => p.PatientVisit)
            .FirstOrDefaultAsync(p => p.PatientVisitId == visitId);

        if (prescription == null)
        {
            var visit = await _dbContext.PatientVisits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId);
            if (visit?.AppointmentId != null)
            {
                prescription = await _dbContext.Prescriptions
                    .Include(p => p.Items)
                        .ThenInclude(i => i.Medicine)
                    .Include(p => p.Patient)
                    .Include(p => p.PatientVisit)
                    .FirstOrDefaultAsync(p => p.AppointmentId == visit.AppointmentId);
            }
        }

        if (prescription == null) return null;

        var doctorUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == doctor.UserId);
        var patientUser = prescription.Patient != null && prescription.Patient.UserId.HasValue
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == prescription.Patient.UserId.Value)
            : null;

        return MapPrescriptionToDto(prescription, doctorUser?.FullName ?? "Bác sĩ", patientUser?.FullName ?? prescription.Patient?.FullName ?? "Bệnh nhân");
    }

    public async Task<PrescriptionDraftDto> SaveVisitPrescriptionDraftAsync(long visitId, SavePrescriptionDraftRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();

        var visit = await _dbContext.PatientVisits
            .Include(v => v.Patient)
            .Include(v => v.Prescriptions)
                .ThenInclude(p => p.Items)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        if (visit == null) throw new NotFoundException("Lượt khám không tồn tại.");

        if (visit.Status != VisitStatus.InConsultation &&
            visit.Status != VisitStatus.WaitingDoctor &&
            visit.Status != VisitStatus.ResultsReady)
        {
            throw new BusinessException("INVALID_STATE", "Chỉ có thể soạn đơn thuốc trong phiên khám.");
        }

        var prescription = visit.Prescriptions.FirstOrDefault() ?? (visit.AppointmentId.HasValue
            ? await _dbContext.Prescriptions.Include(p => p.Items).FirstOrDefaultAsync(p => p.AppointmentId == visit.AppointmentId.Value)
            : null);

        if (prescription == null)
        {
            prescription = new Prescription
            {
                PatientVisitId = visit.Id,
                AppointmentId = visit.AppointmentId,
                PatientId = visit.PatientId,
                DoctorId = doctor.Id,
                Status = PrescriptionStatus.Draft,
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Prescriptions.Add(prescription);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            if (prescription.Status == PrescriptionStatus.Dispensed)
                throw new BusinessException("ALREADY_DISPENSED", "Đơn thuốc đã cấp phát không thể chỉnh sửa.");

            ValidateRowVersion(prescription.RowVersion, request.RowVersion);

            prescription.PatientVisitId = visit.Id;
            prescription.Notes = request.Notes;
            prescription.Status = PrescriptionStatus.Draft;
            prescription.CreatedAt = DateTime.UtcNow;
            prescription.RowVersion = Guid.NewGuid().ToByteArray();

            _dbContext.PrescriptionItems.RemoveRange(prescription.Items);
        }

        foreach (var item in request.Items)
        {
            var medExists = await _dbContext.Medicines.AnyAsync(m => m.Id == item.MedicineId && m.IsActive);
            if (!medExists)
                throw new BusinessException("INVALID_MEDICINE", $"Thuốc với ID {item.MedicineId} không tồn tại hoặc ngừng hoạt động.");

            _dbContext.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = prescription.Id,
                MedicineId = item.MedicineId,
                Quantity = item.Quantity,
                Dosage = item.Dosage ?? string.Empty,
                Frequency = item.Frequency ?? string.Empty,
                DurationDays = item.DurationDays,
                Instructions = item.Instructions
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Đơn thuốc đã được cập nhật bởi một phiên làm việc khác. Vui lòng tải lại.");
        }

        return (await GetVisitPrescriptionDraftAsync(visitId))!;
    }
}
