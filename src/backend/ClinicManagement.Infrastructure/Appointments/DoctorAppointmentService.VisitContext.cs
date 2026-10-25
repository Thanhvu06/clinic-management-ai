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

        visit = await ValidateVisitDoctorAccessAsync(visit, doctor);

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
                StartTime = TimeOnly.FromDateTime(_dateTimeProvider.ConvertUtcToVietnam(visit.CheckedInAtUtc)),
                EndTime = TimeOnly.FromDateTime(_dateTimeProvider.ConvertUtcToVietnam(visit.CheckedInAtUtc)).AddMinutes(15),
                Reason = visit.ChiefComplaint,
                Status = visit.Status.ToString(),
                IsCompletionOverdue = ConsultationCompletionExpiryRule.IsOverdue(visit.Status.ToString(), visit.VisitDate, _dateTimeProvider),
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
            presDto = await MapPrescriptionToDtoAsync(currentPrescription, doctorUser?.FullName ?? "Bác sĩ", patientName);
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

}
