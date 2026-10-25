using System;
using System.Collections.Generic;
using ClinicManagement.Application.Appointments.DTOs;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
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
using ClinicManagement.Application.Prescriptions.Interfaces;
using ClinicManagement.Infrastructure.Prescriptions;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Pharmacy;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;


using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Application.Common.Expiry;
using Microsoft.Extensions.Options;
namespace ClinicManagement.Infrastructure.Appointments;

public partial class DoctorAppointmentService
{
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
                    where a.DoctorId == doctor.Id && (a.AppointmentDate == targetDate ||
                        targetDate == _dateTimeProvider.VietnamToday && a.AppointmentDate < targetDate && a.Status == AppointmentStatus.InConsultation)
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
                        IsCompletionOverdue = a.Status == AppointmentStatus.InConsultation && a.AppointmentDate < _dateTimeProvider.VietnamToday,
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

        var allDoctorDeptIds = await VisitDoctorAuthorizationGuard.GetUnassignedDepartmentIdsAsync(_dbContext, doctor);

        var walkInVisits = await _dbContext.PatientVisits
            .AsNoTracking()
            .Include(v => v.Patient)
            .Include(v => v.Department)
                .ThenInclude(d => d.Specialty)
            .Where(v => v.AppointmentId == null &&
                        (v.VisitDate == targetDate || targetDate == _dateTimeProvider.VietnamToday && v.VisitDate < targetDate && v.Status == VisitStatus.InConsultation) &&
                        (v.AssignedDoctorId == doctor.Id || (!v.AssignedDoctorId.HasValue && allDoctorDeptIds.Contains(v.DepartmentId))))
            .ToListAsync();

        var walkInItems = walkInVisits.Select(v => new DoctorQueueItemDto
        {
            AppointmentId = null,
            AppointmentCode = v.VisitCode,
            PatientVisitId = v.Id,
            VisitCode = v.VisitCode,
            QueueNumber = v.QueueNumber,
            AppointmentDate = v.VisitDate,
            IsCompletionOverdue = ConsultationCompletionExpiryRule.IsOverdue(v.Status.ToString(), v.VisitDate, _dateTimeProvider),
            StartTime = TimeOnly.FromDateTime(_dateTimeProvider.ConvertUtcToVietnam(v.CheckedInAtUtc)),
            EndTime = TimeOnly.FromDateTime(_dateTimeProvider.ConvertUtcToVietnam(v.CheckedInAtUtc)).AddMinutes(15),
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
        var inConsultation = queue.Count(q => DoctorQueueStatus.IsConsultationInProgress(q.Status));
        var completed = queue.Count(q => q.Status == nameof(AppointmentStatus.Completed));
        var noShow = queue.Count(q => q.Status == nameof(AppointmentStatus.NoShow));

        var activeSchedules = await _dbContext.DoctorWorkSchedules
            .AsNoTracking()
            .Where(ws => ws.DoctorId == doctor.Id && ws.WorkDate == targetDate && ws.IsActive)
            .OrderBy(ws => ws.StartTime)
            .ToListAsync();

        var currentShift = DoctorDashboardShifts.DescribeCurrent(activeSchedules, currentTime);

        var nextPatient = queue.FirstOrDefault(q => DoctorQueueStatus.IsConsultationInProgress(q.Status))
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
                        IsCompletionOverdue = a.Status == AppointmentStatus.InConsultation && a.AppointmentDate < _dateTimeProvider.VietnamToday,
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
            TodayShifts = DoctorDashboardShifts.ToDtos(activeSchedules),
            NextPatient = nextPatient,
            Queue = queue,
            UpcomingAppointments = upcomingList
        };
    }

}
