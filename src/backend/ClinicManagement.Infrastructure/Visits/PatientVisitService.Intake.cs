using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Visits;

public partial class PatientVisitService
{
    public async Task<CheckInTicketDto> ReceptionIntakeAsync(ReceptionIntakeRequest request, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetUserId();
        var visitDate = _dateTimeProvider.VietnamToday;

        // 1. Validate Single Source of Patient
        var sourceCount = (request.ExistingPatientId.HasValue ? 1 : 0)
                        + (request.AppointmentId.HasValue ? 1 : 0)
                        + (request.NewPatient != null ? 1 : 0);

        if (sourceCount == 0)
        {
            throw new BusinessException("MISSING_PATIENT_SOURCE", "Vui lòng chọn hồ sơ bệnh nhân cũ, lịch hẹn hoặc nhập thông tin bệnh nhân mới.");
        }

        if (request.NewPatient != null && (request.ExistingPatientId.HasValue || request.AppointmentId.HasValue))
        {
            throw new BusinessException("INVALID_PATIENT_SOURCE", "Không thể vừa khai báo thông tin bệnh nhân mới vừa chọn hồ sơ cũ hoặc lịch hẹn.");
        }

        Appointment? appointment = null;
        if (request.AppointmentId.HasValue)
        {
            appointment = await _dbContext.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Specialty)
                .FirstOrDefaultAsync(a => a.Id == request.AppointmentId.Value, cancellationToken);

            if (appointment == null)
                throw new NotFoundException("Lịch hẹn không tồn tại.");

            if (request.ExistingPatientId.HasValue && appointment.PatientId != request.ExistingPatientId.Value)
            {
                throw new BusinessException("PATIENT_APPOINTMENT_MISMATCH", "Lịch hẹn không thuộc về bệnh nhân đã chọn.");
            }

            if (appointment.Status == AppointmentStatus.Cancelled)
            {
                throw new BusinessException("APPOINTMENT_ALREADY_CANCELLED", "Không thể tiếp nhận lịch hẹn đã bị hủy.");
            }

            if (appointment.Status == AppointmentStatus.Completed)
            {
                throw new BusinessException("APPOINTMENT_ALREADY_COMPLETED", "Lịch hẹn đã hoàn thành khám.");
            }
        }

        // 2. Validate Contact Phone for New Patient
        if (request.NewPatient != null)
        {
            var hasPersonalPhone = !string.IsNullOrWhiteSpace(request.NewPatient.PhoneNumber);
            var hasEmergencyPhone = !string.IsNullOrWhiteSpace(request.NewPatient.EmergencyContact?.PhoneNumber);
            if (!hasPersonalPhone && !hasEmergencyPhone)
            {
                throw new BusinessException("CONTACT_PHONE_REQUIRED", "Vui lòng cung cấp số điện thoại của bệnh nhân hoặc số điện thoại của người giám hộ/người thân liên hệ.");
            }
        }

        // 3. Database-backed Idempotency check
        var payloadHash = ComputePayloadHash(request);
        var existingRecord = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == request.IdempotencyKey && r.Scope == "ReceptionIntake" && r.ExpiresAtUtc > _dateTimeProvider.UtcNow, cancellationToken);

        if (existingRecord != null)
        {
            if (existingRecord.RequestHash != payloadHash)
            {
                throw new ConflictException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", "Idempotency key này đã được sử dụng cho một yêu cầu có dữ liệu khác.");
            }
            return System.Text.Json.JsonSerializer.Deserialize<CheckInTicketDto>(existingRecord.ResponseBody)!;
        }

        // 4. Validate Facility & Resource Authorization
        var facility = await _dbContext.Facilities
            .FirstOrDefaultAsync(f => f.Id == request.FacilityId && f.IsActive, cancellationToken);
        if (facility == null)
            throw new NotFoundException("Cơ sở y tế không tồn tại hoặc đã ngừng hoạt động.");

        await _facilityAuthService.ValidateUserFacilityAccessAsync(currentUserId, request.FacilityId, cancellationToken);

        var department = await _dbContext.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.FacilityId == request.FacilityId && d.IsActive, cancellationToken);
        if (department == null)
            throw new NotFoundException("Khoa tiếp nhận không tồn tại hoặc không thuộc cơ sở y tế đã chọn.");

        // Verify Doctor
        Doctor? doctor = null;
        var doctorIdToAssign = request.AssignedDoctorId ?? (appointment?.DoctorId);
        if (doctorIdToAssign.HasValue)
        {
            doctor = await _dbContext.Doctors
                .FirstOrDefaultAsync(d => d.Id == doctorIdToAssign.Value && d.IsActive, cancellationToken);
            if (doctor == null)
                throw new NotFoundException("Bác sĩ được chỉ định không tồn tại hoặc không hoạt động.");
            await ValidateDoctorFacilityAssignmentAsync(doctor, request.FacilityId, cancellationToken);
            await EnsureDoctorNotOnApprovedLeaveAsync(doctor.Id, cancellationToken);
        }

        // Verify Room
        Room? room = null;
        if (request.RoomId.HasValue)
        {
            room = await _dbContext.Rooms
                .FirstOrDefaultAsync(r => r.Id == request.RoomId.Value && r.DepartmentId == department.Id && r.IsActive, cancellationToken);
            if (room == null)
                throw new NotFoundException("Phòng khám không tồn tại trong khoa đã chọn.");
        }

        // 5. Transaction: Resolve/Create Patient, Package link, Issue Visit, Save Idempotency
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            Patient patient;
            if (request.ExistingPatientId.HasValue)
            {
                var existingPatient = await _dbContext.Patients
                    .FirstOrDefaultAsync(p => p.Id == request.ExistingPatientId.Value, cancellationToken);
                if (existingPatient == null)
                    throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");
                patient = existingPatient;
            }
            else if (appointment != null)
            {
                patient = appointment.Patient;
            }
            else
            {
                // New Patient
                var newProfile = request.NewPatient!;
                var cleanCccd = string.IsNullOrWhiteSpace(newProfile.NationalId)
                    ? null
                    : newProfile.NationalId.Trim().Replace(" ", "").Replace("-", "");

                if (!string.IsNullOrEmpty(cleanCccd))
                {
                    var cccdExists = await _dbContext.Patients.AnyAsync(p => p.NationalId == cleanCccd, cancellationToken);
                    if (cccdExists)
                    {
                        throw new ConflictException("DUPLICATE_NATIONAL_ID", "Số CCCD/Định danh cá nhân này đã tồn tại trong hệ thống.");
                    }
                }

                var mrn = await _mrnGenerator.GenerateNextMrnAsync(cancellationToken);
                patient = new Patient
                {
                    UserId = null,
                    FullName = newProfile.FullName.Trim(),
                    PhoneNumber = string.IsNullOrWhiteSpace(newProfile.PhoneNumber) ? (newProfile.EmergencyContact?.PhoneNumber.Trim() ?? string.Empty) : newProfile.PhoneNumber.Trim(),
                    DateOfBirth = newProfile.DateOfBirth,
                    Gender = newProfile.Gender,
                    Address = newProfile.Address?.Trim(),
                    NationalId = cleanCccd,
                    BhytNumber = string.IsNullOrWhiteSpace(newProfile.BhytNumber) ? null : newProfile.BhytNumber.Trim(),
                    Email = string.IsNullOrWhiteSpace(newProfile.Email) ? null : newProfile.Email.Trim(),
                    MedicalRecordNumber = mrn,
                    PrimaryFacilityId = request.FacilityId
                };
                _dbContext.Patients.Add(patient);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (newProfile.Allergies.Count > 0)
                {
                    foreach (var al in newProfile.Allergies)
                    {
                        var severity = Enum.TryParse<AllergySeverity>(al.Severity, true, out var parsedSev) ? parsedSev : AllergySeverity.Moderate;
                        _dbContext.PatientAllergies.Add(new PatientAllergy
                        {
                            PatientId = patient.Id,
                            AllergenName = al.Allergen.Trim(),
                            Severity = severity,
                            ReactionDescription = al.Reaction?.Trim(),
                            AllergenType = AllergenType.Drug
                        });
                    }
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                if (newProfile.EmergencyContact != null)
                {
                    _dbContext.EmergencyContacts.Add(new EmergencyContact
                    {
                        PatientId = patient.Id,
                        FullName = newProfile.EmergencyContact.ContactName.Trim(),
                        Relationship = newProfile.EmergencyContact.Relationship.Trim(),
                        PhoneNumber = newProfile.EmergencyContact.PhoneNumber.Trim(),
                        IsGuardian = newProfile.EmergencyContact.IsGuardian,
                        IsPrimary = true
                    });
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            // Check health package registration
            if (request.HealthPackageRegistrationId.HasValue)
            {
                var pkgReg = await _dbContext.HealthPackageRegistrations
                    .FirstOrDefaultAsync(r => r.Id == request.HealthPackageRegistrationId.Value, cancellationToken);
                if (pkgReg == null)
                    throw new NotFoundException("Thông tin đăng ký gói khám không tồn tại.");
                if (pkgReg.PatientId != patient.Id)
                    throw new BusinessException("PACKAGE_PATIENT_MISMATCH", "Đăng ký gói khám không thuộc về bệnh nhân này.");
            }

            var queueNumber = await GetNextQueueNumberAsync(request.FacilityId, department.Id, visitDate, cancellationToken);
            var visitCode = await GenerateVisitCodeAsync(visitDate, queueNumber, cancellationToken);

            var arrivalType = request.HealthPackageRegistrationId.HasValue
                ? VisitArrivalType.HealthPackage
                : (appointment != null ? VisitArrivalType.Scheduled : VisitArrivalType.WalkIn);

            var visit = new PatientVisit
            {
                VisitCode = visitCode,
                PatientId = patient.Id,
                AppointmentId = appointment?.Id,
                HealthPackageRegistrationId = request.HealthPackageRegistrationId,
                FacilityId = request.FacilityId,
                DepartmentId = department.Id,
                RoomId = room?.Id,
                AssignedDoctorId = doctor?.Id,
                VisitDate = visitDate,
                ArrivalType = arrivalType,
                Priority = request.Priority,
                ChiefComplaint = request.ChiefComplaint.Trim(),
                QueueNumber = queueNumber,
                Status = VisitStatus.WaitingForDoctor,
                CheckedInAtUtc = _dateTimeProvider.UtcNow,
                CreatedByUserId = currentUserId,
                CreatedAtUtc = _dateTimeProvider.UtcNow
            };

            _dbContext.PatientVisits.Add(visit);

            if (appointment != null)
            {
                if (appointment.Status == AppointmentStatus.Pending)
                {
                    appointment.Status = AppointmentStatus.Confirmed;
                }
                appointment.PatientVisit = visit;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            var rName = await GetUserNameAsync(currentUserId, cancellationToken);
            var docName = doctor != null ? await GetUserNameAsync(doctor.UserId, cancellationToken) : null;
            visit.Facility = facility;
            visit.Department = department;
            visit.Patient = patient;
            visit.Room = room;

            var ticketDto = MapToTicket(visit, rName, docName);

            // Record idempotency
            var idempotencyRecord = new IdempotencyRecord
            {
                Key = request.IdempotencyKey,
                Scope = "ReceptionIntake",
                UserId = currentUserId,
                RequestHash = payloadHash,
                StatusCode = 201,
                ResponseBody = System.Text.Json.JsonSerializer.Serialize(ticketDto),
                CreatedAtUtc = _dateTimeProvider.UtcNow,
                ExpiresAtUtc = _dateTimeProvider.UtcNow.AddHours(24)
            };
            _dbContext.IdempotencyRecords.Add(idempotencyRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

            RecordWalkInAudit(visit, currentUserId);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ticketDto;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            var committedRecord = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == request.IdempotencyKey && r.Scope == "ReceptionIntake", cancellationToken);

            if (committedRecord != null)
            {
                return System.Text.Json.JsonSerializer.Deserialize<CheckInTicketDto>(committedRecord.ResponseBody)!;
            }
            throw;
        }
    }

    public async Task<CheckInTicketDto> CreateWalkInVisitAsync(WalkInRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetUserId();
        var visitDate = _dateTimeProvider.VietnamToday;

        var department = await _dbContext.Departments
            .Include(d => d.Facility)
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.IsActive, cancellationToken);

        if (department == null)
            throw new NotFoundException("Khoa tiếp nhận không tồn tại hoặc đã ngừng hoạt động.");

        var facilityId = request.FacilityId ?? department.FacilityId;
        await _facilityAuthService.ValidateUserFacilityAccessAsync(currentUserId, facilityId, cancellationToken);

        // Verify doctor if assigned
        Doctor? doctor = null;
        if (request.AssignedDoctorId.HasValue)
        {
            doctor = await _dbContext.Doctors
                .FirstOrDefaultAsync(d => d.Id == request.AssignedDoctorId.Value && d.IsActive, cancellationToken);
            if (doctor == null)
                throw new NotFoundException("Bác sĩ được chỉ định không tồn tại hoặc không hoạt động.");
            await ValidateDoctorFacilityAssignmentAsync(doctor, facilityId, cancellationToken);
        }

        var patient = await ResolveOrCreatePatientAsync(request, facilityId, cancellationToken);

        // Verify room if specified
        Room? room = null;
        if (request.RoomId.HasValue)
        {
            room = await _dbContext.Rooms
                .FirstOrDefaultAsync(r => r.Id == request.RoomId.Value && r.DepartmentId == department.Id && r.IsActive, cancellationToken);
            if (room == null)
                throw new NotFoundException("Phòng khám không tồn tại trong khoa đã chọn.");
        }

        var queueNumber = await GetNextQueueNumberAsync(facilityId, department.Id, visitDate, cancellationToken);
        var visitCode = await GenerateVisitCodeAsync(visitDate, queueNumber, cancellationToken);

        var visit = new PatientVisit
        {
            VisitCode = visitCode,
            PatientId = patient.Id,
            AppointmentId = null,
            FacilityId = facilityId,
            DepartmentId = department.Id,
            RoomId = room?.Id,
            AssignedDoctorId = doctor?.Id,
            VisitDate = visitDate,
            ArrivalType = VisitArrivalType.WalkIn,
            Priority = request.Priority,
            ChiefComplaint = request.ChiefComplaint?.Trim(),
            QueueNumber = queueNumber,
            Status = VisitStatus.WaitingForDoctor,
            CheckedInAtUtc = _dateTimeProvider.UtcNow,
            CreatedByUserId = currentUserId,
            CreatedAtUtc = _dateTimeProvider.UtcNow
        };

        _dbContext.PatientVisits.Add(visit);
        await _dbContext.SaveChangesAsync(cancellationToken);
        RecordWalkInAudit(visit, currentUserId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var receptionistName = await GetUserNameAsync(currentUserId, cancellationToken);
        var doctorName = doctor != null ? await GetDoctorNameAsync(doctor.Id, cancellationToken) : null;

        return new CheckInTicketDto
        {
            VisitId = visit.Id,
            VisitCode = visit.VisitCode,
            AppointmentId = null,
            AppointmentCode = null,
            PatientId = patient.Id,
            PatientName = patient.FullName ?? string.Empty,
            MedicalRecordNumber = patient.MedicalRecordNumber ?? string.Empty,
            PhoneNumber = patient.PhoneNumber ?? string.Empty,
            QueueNumber = queueNumber,
            QueueDisplay = $"{department.Code}-{queueNumber:D3}",
            FacilityId = facilityId,
            FacilityName = department.Facility?.Name ?? "Bệnh viện ClinicCare",
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            RoomId = room?.Id,
            RoomNumber = room != null ? $"{room.RoomNumber} - {room.Name}" : null,
            AssignedDoctorId = doctor?.Id,
            DoctorName = doctorName,
            CheckedInAtUtc = visit.CheckedInAtUtc,
            ReceptionistName = receptionistName,
            Status = visit.Status.ToString(),
            Priority = visit.Priority.ToString(),
            ArrivalType = visit.ArrivalType.ToString()
        };
    }
    private void RecordWalkInAudit(PatientVisit visit, Guid actor)
    {
        if (visit.AppointmentId.HasValue) return;
        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = actor, Action = "WALK_IN_INTAKE", EntityName = "PatientVisit",
            EntityId = visit.Id.ToString(), Description = $"Tiếp nhận khám không hẹn {visit.VisitCode}",
            CreatedAt = _dateTimeProvider.UtcNow
        });
    }
}
