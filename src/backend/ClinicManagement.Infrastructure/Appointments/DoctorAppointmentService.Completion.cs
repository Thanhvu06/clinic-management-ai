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

namespace ClinicManagement.Infrastructure.Appointments;

public partial class DoctorAppointmentService
{
    private readonly ClinicManagement.Application.Notifications.Interfaces.INotificationService _notificationService;

    public DoctorAppointmentService(
        AppDbContext dbContext,
        IDoctorContextService doctorContextService,
        IDateTimeProvider dateTimeProvider,
        ICurrentUserService currentUserService,
        IPatientVisitService patientVisitService,
        IPrescriptionSafetyService? prescriptionSafety = null,
        ClinicManagement.Application.Notifications.Interfaces.INotificationService? notificationService = null)
    {
        _dbContext = dbContext;
        _notificationService = notificationService ?? new ClinicManagement.Infrastructure.Notifications.NotificationService(dbContext, currentUserService, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClinicManagement.Infrastructure.Notifications.NotificationService>.Instance);
        _doctorContextService = doctorContextService;
        _dateTimeProvider = dateTimeProvider;
        _currentUserService = currentUserService;
        _patientVisitService = patientVisitService;
        _prescriptionSafety = prescriptionSafety ?? new PrescriptionSafetyService(dbContext);
    }

    public async Task CompleteAppointmentAsync(long appointmentId, CompleteConsultationRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();
        request.DiagnosisCode = await NormalizeDiagnosisCodeAsync(request.DiagnosisCode);
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

        var issuedBefore = await FindIssuedPrescriptionIdsAsync(appointment.Id, null);
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
                    CreatedAtUtc = _dateTimeProvider.UtcNow,
                    CompletedAtUtc = _dateTimeProvider.UtcNow
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
                summary.UpdatedAtUtc = _dateTimeProvider.UtcNow;
                summary.CompletedAtUtc = _dateTimeProvider.UtcNow;
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
                        CreatedAt = _dateTimeProvider.UtcNow
                    };
                    _dbContext.Prescriptions.Add(prescription);
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    await ValidatePrescriptionNotBilledOrLockedAsync(prescription);
                    ValidateRowVersion(prescription.RowVersion, request.PrescriptionRowVersion);
                    if (!prescription.PatientVisitId.HasValue && relatedVisit != null)
                    {
                        prescription.PatientVisitId = relatedVisit.Id;
                    }
                    prescription.Status = PrescriptionStatus.Issued;
                    prescription.Notes = request.PrescriptionNotes;
                    prescription.CreatedAt = _dateTimeProvider.UtcNow;

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
                await ApplyPrescriptionSafetyAsync(prescription, request.PrescriptionItems.Select(i => i.MedicineId), request.PrescriptionSafetyOverrideReason);
            }
            else if (appointment.Prescription != null && appointment.Prescription.Status == PrescriptionStatus.Draft)
            {
                if (appointment.Prescription.Items.Count > 0)
                {
                    await ApplyPrescriptionSafetyAsync(appointment.Prescription, appointment.Prescription.Items.Select(i => i.MedicineId), request.PrescriptionSafetyOverrideReason);
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
                CreatedAt = _dateTimeProvider.UtcNow
            });

            await RecordCompletionAuditAsync(userId, appointment.Id, relatedVisit?.Id, issuedBefore);
            if (relatedVisit != null)
            {
                await VisitCompletionCoordinator.RecordClinicalCompletionAsync(relatedVisit.Id, _dbContext, _notificationService);
                await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(relatedVisit.Id, _dbContext, _dateTimeProvider.UtcNow);
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
                    CreatedAtUtc = _dateTimeProvider.UtcNow
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

    public async Task CompleteVisitConsultationAsync(long visitId, CompleteConsultationRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();
        request.DiagnosisCode = await NormalizeDiagnosisCodeAsync(request.DiagnosisCode);
        var userId = GetUserId();

        var visit = await _dbContext.PatientVisits
            .Include(v => v.Patient)
            .Include(v => v.Appointment)
            .Include(v => v.VisitSummary)
            .Include(v => v.Prescriptions)
                .ThenInclude(p => p.Items)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        visit = await ValidateVisitDoctorAccessAsync(visit, doctor);

        if (string.IsNullOrWhiteSpace(request.Summary) && string.IsNullOrWhiteSpace(request.Diagnosis))
            throw new BusinessException("VALIDATION_ERROR", "Tóm tắt kết luận khám không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Diagnosis))
            request.Diagnosis = !string.IsNullOrWhiteSpace(request.Summary) ? request.Summary : "Khám lâm sàng";

        if (string.IsNullOrWhiteSpace(request.Summary))
            request.Summary = request.Diagnosis;

        var issuedBefore = await FindIssuedPrescriptionIdsAsync(visit.AppointmentId, visit.Id);
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
                    CreatedAt = _dateTimeProvider.UtcNow
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
                    CreatedAtUtc = _dateTimeProvider.UtcNow,
                    CompletedAtUtc = _dateTimeProvider.UtcNow
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
                summary.UpdatedAtUtc = _dateTimeProvider.UtcNow;
                summary.CompletedAtUtc = _dateTimeProvider.UtcNow;
            }

            if (request.IssuePrescription && request.PrescriptionItems != null && request.PrescriptionItems.Count > 0)
            {
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
                        CreatedAt = _dateTimeProvider.UtcNow
                    };
                    _dbContext.Prescriptions.Add(prescription);
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    await ValidatePrescriptionNotBilledOrLockedAsync(prescription);
                    ValidateRowVersion(prescription.RowVersion, request.PrescriptionRowVersion);
                    prescription.PatientVisitId = visit.Id;
                    prescription.Status = PrescriptionStatus.Issued;
                    prescription.Notes = request.PrescriptionNotes;
                    prescription.CreatedAt = _dateTimeProvider.UtcNow;

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
                await ApplyPrescriptionSafetyAsync(prescription, request.PrescriptionItems.Select(i => i.MedicineId), request.PrescriptionSafetyOverrideReason);
            }
            else
            {
                var existingPres = visit.Prescriptions.FirstOrDefault() ?? (visit.AppointmentId.HasValue
                    ? await _dbContext.Prescriptions.Include(p => p.Items).FirstOrDefaultAsync(p => p.AppointmentId == visit.AppointmentId.Value)
                    : null);

                if (existingPres != null && existingPres.Status == PrescriptionStatus.Draft && existingPres.Items.Count > 0)
                {
                    await ApplyPrescriptionSafetyAsync(existingPres, existingPres.Items.Select(i => i.MedicineId), request.PrescriptionSafetyOverrideReason);
                    existingPres.Status = PrescriptionStatus.Issued;
                    existingPres.PatientVisitId = visit.Id;
                }
            }

            await _dbContext.SaveChangesAsync();
            await RecordCompletionAuditAsync(userId, visit.AppointmentId, visit.Id, issuedBefore);
            await VisitCompletionCoordinator.RecordClinicalCompletionAsync(visit.Id, _dbContext, _notificationService);
            await VisitCompletionCoordinator.TryUpdateVisitProgressAsync(visit.Id, _dbContext, _dateTimeProvider.UtcNow);

            var patientUserId = visit.Patient?.UserId;
            if (patientUserId.HasValue && patientUserId.Value != Guid.Empty)
            {
                _dbContext.Notifications.Add(new Notification
                {
                    UserId = patientUserId.Value,
                    Type = NotificationType.Appointment,
                    Title = "Buổi khám đã hoàn tất",
                    Message = $"Lượt khám #{visit.VisitCode} đã được bác sĩ hoàn tất. Chẩn đoán: {request.Diagnosis}",
                    Route = "/patient/appointments",
                    RelatedEntityType = "PatientVisit",
                    RelatedEntityId = visit.Id.ToString(),
                    DedupeKey = $"visit_complete_{visit.Id}",
                    IsRead = false,
                    CreatedAtUtc = _dateTimeProvider.UtcNow
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
}
