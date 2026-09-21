using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Visits;

public class PatientVisitService : IPatientVisitService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IMrnGenerator _mrnGenerator;
    private readonly IFacilityAuthorizationService _facilityAuthService;

    public PatientVisitService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        IMrnGenerator mrnGenerator,
        IFacilityAuthorizationService facilityAuthService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _mrnGenerator = mrnGenerator;
        _facilityAuthService = facilityAuthService;
    }

    private Guid GetUserId() => _currentUserService.UserId ?? Guid.Empty;

    private static string ComputePayloadHash(ReceptionIntakeRequest request)
    {
        var normalized = new
        {
            request.ExistingPatientId,
            request.AppointmentId,
            request.HealthPackageRegistrationId,
            request.FacilityId,
            request.DepartmentId,
            request.RoomId,
            request.AssignedDoctorId,
            ChiefComplaint = request.ChiefComplaint?.Trim(),
            request.Priority,
            NewPatient = request.NewPatient == null ? null : new
            {
                FullName = request.NewPatient.FullName?.Trim(),
                PhoneNumber = request.NewPatient.PhoneNumber?.Trim(),
                request.NewPatient.DateOfBirth,
                request.NewPatient.Gender,
                Address = request.NewPatient.Address?.Trim(),
                NationalId = request.NewPatient.NationalId?.Trim(),
                BhytNumber = request.NewPatient.BhytNumber?.Trim(),
                Email = request.NewPatient.Email?.Trim()
            }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(normalized);
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;
        }
        var msg = ex.InnerException?.Message ?? string.Empty;
        return msg.Contains("UNIQUE") || msg.Contains("constraint");
    }

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

        // Resolve or create patient
        var patient = await ResolveOrCreatePatientAsync(request, facilityId, cancellationToken);

        // Verify doctor if assigned
        Doctor? doctor = null;
        if (request.AssignedDoctorId.HasValue)
        {
            doctor = await _dbContext.Doctors
                .FirstOrDefaultAsync(d => d.Id == request.AssignedDoctorId.Value && d.IsActive, cancellationToken);
            if (doctor == null)
                throw new NotFoundException("Bác sĩ được chỉ định không tồn tại hoặc không hoạt động.");
        }

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

    public async Task<CheckInTicketDto> CheckInAppointmentAsync(AppointmentCheckInRequest request, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetUserId();

        // 1. Check idempotency: If visit already created for this appointment, return existing ticket
        var existingVisit = await _dbContext.PatientVisits
            .Include(v => v.Patient)
            .Include(v => v.Department)
            .Include(v => v.Facility)
            .Include(v => v.Room)
            .Include(v => v.AssignedDoctor)
            .Include(v => v.Appointment)
            .FirstOrDefaultAsync(v => v.AppointmentId == request.AppointmentId, cancellationToken);

        if (existingVisit != null)
        {
            var rName = await GetUserNameAsync(existingVisit.CreatedByUserId, cancellationToken);
            var docName = await GetDoctorNameAsync(existingVisit.AssignedDoctorId, cancellationToken);
            return MapToTicket(existingVisit, rName, docName);
        }

        // 2. Fetch appointment
        var appointment = await _dbContext.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Specialty)
            .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken);

        if (appointment == null)
            throw new NotFoundException("Lịch hẹn không tồn tại.");

        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new BusinessException("APPOINTMENT_CANCELLED", "Không thể tiếp nhận lịch hẹn đã bị hủy.");

        if (appointment.Status == AppointmentStatus.Completed)
            throw new BusinessException("APPOINTMENT_COMPLETED", "Lịch hẹn đã hoàn thành khám.");

        // 3. Resolve Department
        Department? department = null;
        if (request.DepartmentId.HasValue)
        {
            department = await _dbContext.Departments
                .Include(d => d.Facility)
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value && d.IsActive, cancellationToken);
        }

        if (department == null)
        {
            department = await _dbContext.Departments
                .Include(d => d.Facility)
                .FirstOrDefaultAsync(d => d.SpecialtyId == appointment.SpecialtyId && d.IsActive, cancellationToken);
        }

        if (department == null)
        {
            department = await _dbContext.Departments
                .Include(d => d.Facility)
                .FirstOrDefaultAsync(d => d.IsActive, cancellationToken);
        }

        if (department == null)
        {
            throw new NotFoundException("Khoa tiếp nhận không tồn tại hoặc đã ngừng hoạt động.");
        }

        var facilityId = request.FacilityId ?? department.FacilityId;
        await _facilityAuthService.ValidateUserFacilityAccessAsync(currentUserId, facilityId, cancellationToken);

        // Verify room if specified
        Room? room = null;
        if (request.RoomId.HasValue)
        {
            room = await _dbContext.Rooms
                .FirstOrDefaultAsync(r => r.Id == request.RoomId.Value && r.DepartmentId == department.Id && r.IsActive, cancellationToken);
        }

        var assignedDoctorId = request.AssignedDoctorId ?? appointment.DoctorId;
        var doctor = await _dbContext.Doctors
            .FirstOrDefaultAsync(d => d.Id == assignedDoctorId, cancellationToken);

        var queueNumber = await GetNextQueueNumberAsync(facilityId, department.Id, appointment.AppointmentDate, cancellationToken);
        var visitCode = await GenerateVisitCodeAsync(appointment.AppointmentDate, queueNumber, cancellationToken);

        var visit = new PatientVisit
        {
            VisitCode = visitCode,
            PatientId = appointment.PatientId,
            AppointmentId = appointment.Id,
            FacilityId = facilityId,
            DepartmentId = department.Id,
            RoomId = room?.Id,
            AssignedDoctorId = assignedDoctorId,
            VisitDate = appointment.AppointmentDate,
            ArrivalType = VisitArrivalType.Scheduled,
            Priority = VisitPriority.Normal,
            ChiefComplaint = appointment.Reason,
            QueueNumber = queueNumber,
            Status = VisitStatus.WaitingForDoctor,
            CheckedInAtUtc = _dateTimeProvider.UtcNow,
            CreatedByUserId = currentUserId,
            CreatedAtUtc = _dateTimeProvider.UtcNow
        };

        _dbContext.PatientVisits.Add(visit);

        // Update appointment status to CheckedIn
        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.CheckedIn;

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.CheckedIn,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.CheckedIn,
            Note = $"Tiếp nhận bệnh nhân tại quầy lễ tân. Mã lượt khám: {visitCode}, STT: {queueNumber}",
            PerformedByUserId = currentUserId,
            CreatedAt = _dateTimeProvider.UtcNow
        });

        // If patient has UserId, send notification
        if (appointment.Patient?.UserId.HasValue == true)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = appointment.Patient.UserId.Value,
                Type = NotificationType.Appointment,
                Title = "Tiếp nhận khám thành công",
                Message = $"Bạn đã được tiếp nhận khám tại {department.Name}. Số thứ tự của bạn là {queueNumber}. Mã lượt khám: {visitCode}.",
                Route = "/patient/appointments",
                RelatedEntityType = "PatientVisit",
                RelatedEntityId = visit.Id.ToString(),
                DedupeKey = $"checkin_visit_{visit.Id}",
                IsRead = false,
                CreatedAtUtc = _dateTimeProvider.UtcNow
            });
        }

        // If doctor has UserId, send notification
        if (doctor != null && doctor.UserId != Guid.Empty)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = doctor.UserId,
                Type = NotificationType.Appointment,
                Title = "Bệnh nhân đã đến phòng khám",
                Message = $"Bệnh nhân {appointment.Patient?.FullName} cho lịch khám #{appointment.AppointmentCode} đã có mặt tại phòng chờ.",
                Route = $"/doctor/appointments/{appointment.Id}",
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"appt_checkin_doc_{appointment.Id}_{doctor.UserId}",
                IsRead = false,
                CreatedAtUtc = _dateTimeProvider.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var receptionistName = await GetUserNameAsync(currentUserId, cancellationToken);
        var doctorName = doctor != null ? await GetDoctorNameAsync(doctor.Id, cancellationToken) : null;

        return new CheckInTicketDto
        {
            VisitId = visit.Id,
            VisitCode = visit.VisitCode,
            AppointmentId = appointment.Id,
            AppointmentCode = appointment.AppointmentCode,
            PatientId = appointment.PatientId,
            PatientName = appointment.Patient?.FullName ?? "Bệnh nhân",
            MedicalRecordNumber = appointment.Patient?.MedicalRecordNumber ?? string.Empty,
            PhoneNumber = appointment.Patient?.PhoneNumber ?? string.Empty,
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

    public async Task<PatientVisitDetailDto> GetVisitByIdAsync(long visitId, CancellationToken cancellationToken = default)
    {
        var visit = await _dbContext.PatientVisits
            .AsNoTracking()
            .Include(v => v.Patient)
            .Include(v => v.Facility)
            .Include(v => v.Department)
            .Include(v => v.Room)
            .Include(v => v.AssignedDoctor)
            .Include(v => v.Appointment)
            .Include(v => v.VitalSigns)
            .Include(v => v.VisitSummary)
            .Include(v => v.DiagnosticOrders)
            .Include(v => v.Prescriptions)
            .Include(v => v.Invoices)
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null)
            throw new NotFoundException("Lượt khám không tồn tại.");

        var activeOrders = visit.DiagnosticOrders.Where(o => o.Status != DiagnosticOrderStatus.Cancelled).ToList();
        var pendingOrdersCount = activeOrders.Count(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress);
        var latestPrescription = visit.Prescriptions.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        var latestInvoice = visit.Invoices.OrderByDescending(i => i.CreatedAtUtc).FirstOrDefault();
        var docName = await GetDoctorNameAsync(visit.AssignedDoctorId, cancellationToken);

        return new PatientVisitDetailDto
        {
            Id = visit.Id,
            VisitCode = visit.VisitCode,
            AppointmentId = visit.AppointmentId,
            AppointmentCode = visit.Appointment?.AppointmentCode,
            PatientId = visit.PatientId,
            PatientName = visit.Patient.FullName ?? string.Empty,
            MedicalRecordNumber = visit.Patient.MedicalRecordNumber ?? string.Empty,
            PhoneNumber = visit.Patient.PhoneNumber ?? string.Empty,
            DateOfBirth = visit.Patient.DateOfBirth,
            Gender = visit.Patient.Gender?.ToString(),
            Address = visit.Patient.Address,
            IdentityCardNumber = visit.Patient.NationalId,

            FacilityId = visit.FacilityId,
            FacilityName = visit.Facility?.Name ?? string.Empty,
            DepartmentId = visit.DepartmentId,
            DepartmentName = visit.Department?.Name ?? string.Empty,
            RoomId = visit.RoomId,
            RoomNumber = visit.Room != null ? $"{visit.Room.RoomNumber} - {visit.Room.Name}" : null,
            AssignedDoctorId = visit.AssignedDoctorId,
            DoctorName = docName,

            VisitDate = visit.VisitDate,
            ArrivalType = visit.ArrivalType.ToString(),
            Priority = visit.Priority.ToString(),
            ChiefComplaint = visit.ChiefComplaint,
            QueueNumber = visit.QueueNumber,
            QueueDisplay = $"{visit.Department?.Code ?? "VIS"}-{visit.QueueNumber:D3}",
            Status = visit.Status.ToString(),

            CheckedInAtUtc = visit.CheckedInAtUtc,
            ConsultationStartedAtUtc = visit.ConsultationStartedAtUtc,
            CompletedAtUtc = visit.CompletedAtUtc,
            CancelledAtUtc = visit.CancelledAtUtc,
            CancellationReason = visit.CancellationReason,

            HasVitalSigns = visit.VitalSigns != null,
            HasEncounter = visit.VisitSummary != null,
            DiagnosticOrdersCount = activeOrders.Count,
            PendingDiagnosticOrdersCount = pendingOrdersCount,
            HasPrescription = latestPrescription != null,
            PrescriptionStatus = latestPrescription?.Status.ToString(),
            HasInvoice = latestInvoice != null,
            InvoiceStatus = latestInvoice?.Status.ToString(),
            RowVersion = visit.RowVersion != null ? Convert.ToBase64String(visit.RowVersion) : null
        };
    }

    public async Task<List<DepartmentQueueItemDto>> GetDepartmentQueueAsync(long departmentId, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? _dateTimeProvider.VietnamToday;

        var visits = await _dbContext.PatientVisits
            .AsNoTracking()
            .Include(v => v.Patient)
            .Include(v => v.Department)
            .Include(v => v.AssignedDoctor)
            .Include(v => v.Room)
            .Include(v => v.Appointment)
            .Include(v => v.VitalSigns)
            .Where(v => v.DepartmentId == departmentId &&
                        v.VisitDate == targetDate &&
                        v.Status != VisitStatus.Cancelled &&
                        v.Status != VisitStatus.Completed &&
                        v.Status != VisitStatus.NoShow)
            .ToListAsync(cancellationToken);

        visits = visits
            .OrderByDescending(v => (int)v.Priority)
            .ThenBy(v => v.QueueNumber)
            .ToList();

        var docIds = visits.Where(v => v.AssignedDoctorId.HasValue).Select(v => v.AssignedDoctorId!.Value).ToList();
        var docNames = await GetDoctorNamesAsync(docIds, cancellationToken);

        return visits.Select(v =>
        {
            int? age = null;
            if (v.Patient.DateOfBirth.HasValue)
            {
                age = targetDate.Year - v.Patient.DateOfBirth.Value.Year;
                if (targetDate < v.Patient.DateOfBirth.Value.AddYears(age.Value)) age--;
            }

            return new DepartmentQueueItemDto
            {
                VisitId = v.Id,
                VisitCode = v.VisitCode,
                AppointmentId = v.AppointmentId,
                AppointmentCode = v.Appointment?.AppointmentCode,
                QueueNumber = v.QueueNumber,
                QueueDisplay = $"{v.Department?.Code ?? "Q"}-{v.QueueNumber:D3}",
                PatientId = v.PatientId,
                PatientName = v.Patient.FullName ?? "Bệnh nhân",
                MedicalRecordNumber = v.Patient.MedicalRecordNumber ?? string.Empty,
                PhoneNumber = v.Patient.PhoneNumber ?? string.Empty,
                Gender = v.Patient.Gender?.ToString(),
                DateOfBirth = v.Patient.DateOfBirth,
                Age = age,
                Priority = v.Priority.ToString(),
                Status = v.Status.ToString(),
                ArrivalType = v.ArrivalType.ToString(),
                ChiefComplaint = v.ChiefComplaint,
                CheckedInAtUtc = v.CheckedInAtUtc,
                AssignedDoctorId = v.AssignedDoctorId,
                AssignedDoctorName = v.AssignedDoctorId.HasValue && docNames.TryGetValue(v.AssignedDoctorId.Value, out var dn) ? dn : null,
                RoomId = v.RoomId,
                RoomNumber = v.Room != null ? $"{v.Room.RoomNumber} - {v.Room.Name}" : null,
                HasVitalSigns = v.VitalSigns != null
            };
        }).ToList();
    }

    public async Task<PatientVisitDetailDto> AssignDoctorAsync(long visitId, AssignDoctorRequest request, CancellationToken cancellationToken = default)
    {
        var visit = await _dbContext.PatientVisits
            .Include(v => v.Department)
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null)
            throw new NotFoundException("Lượt khám không tồn tại.");

        var doctor = await _dbContext.Doctors
            .FirstOrDefaultAsync(d => d.Id == request.DoctorId && d.IsActive, cancellationToken);

        if (doctor == null)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        Room? room = null;
        if (request.RoomId.HasValue)
        {
            room = await _dbContext.Rooms
                .FirstOrDefaultAsync(r => r.Id == request.RoomId.Value && r.DepartmentId == visit.DepartmentId && r.IsActive, cancellationToken);
            if (room == null)
                throw new NotFoundException("Phòng khám không tồn tại trong khoa của lượt khám.");
            visit.RoomId = room.Id;
        }

        visit.AssignedDoctorId = doctor.Id;
        if (visit.Status == VisitStatus.CheckedIn)
        {
            visit.Status = VisitStatus.WaitingForDoctor;
        }
        visit.UpdatedAtUtc = _dateTimeProvider.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetVisitByIdAsync(visit.Id, cancellationToken);
    }

    public async Task<PatientVisitDetailDto> UpdateVisitStatusAsync(long visitId, VisitStatus newStatus, string? reason = null, CancellationToken cancellationToken = default)
    {
        var visit = await _dbContext.PatientVisits.FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);
        if (visit == null)
            throw new NotFoundException("Lượt khám không tồn tại.");

        visit.Status = newStatus;
        visit.UpdatedAtUtc = _dateTimeProvider.UtcNow;

        if (newStatus == VisitStatus.InConsultation && !visit.ConsultationStartedAtUtc.HasValue)
        {
            visit.ConsultationStartedAtUtc = _dateTimeProvider.UtcNow;
        }
        else if (newStatus == VisitStatus.Completed)
        {
            var (canComplete, incompleteReason) = await VisitCompletionCoordinator.CanCompleteVisitAsync(visit.Id, _dbContext, cancellationToken);
            if (!canComplete)
            {
                throw new BusinessException("VISIT_CANNOT_COMPLETE", $"Lượt khám chưa đủ điều kiện hoàn tất: {incompleteReason}");
            }
            visit.CompletedAtUtc = _dateTimeProvider.UtcNow;
            if (visit.AppointmentId.HasValue)
            {
                var apt = await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, cancellationToken);
                if (apt != null && apt.Status != AppointmentStatus.Completed)
                {
                    apt.Status = AppointmentStatus.Completed;
                }
            }
        }
        else if (newStatus == VisitStatus.Cancelled)
        {
            visit.CancelledAtUtc = _dateTimeProvider.UtcNow;
            visit.CancellationReason = reason;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetVisitByIdAsync(visit.Id, cancellationToken);
    }

    public async Task<CheckInTicketDto> GetCheckInTicketAsync(long visitId, CancellationToken cancellationToken = default)
    {
        var visit = await _dbContext.PatientVisits
            .AsNoTracking()
            .Include(v => v.Patient)
            .Include(v => v.Department)
            .Include(v => v.Facility)
            .Include(v => v.Room)
            .Include(v => v.AssignedDoctor)
            .Include(v => v.Appointment)
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null)
            throw new NotFoundException("Lượt khám không tồn tại.");

        var rName = await GetUserNameAsync(visit.CreatedByUserId, cancellationToken);
        var docName = await GetDoctorNameAsync(visit.AssignedDoctorId, cancellationToken);
        return MapToTicket(visit, rName, docName);
    }

    private CheckInTicketDto MapToTicket(PatientVisit visit, string receptionistName, string? doctorName = null)
    {
        var deptCode = visit.Department?.Code ?? "VIS";
        return new CheckInTicketDto
        {
            VisitId = visit.Id,
            VisitCode = visit.VisitCode,
            AppointmentId = visit.AppointmentId,
            AppointmentCode = visit.Appointment?.AppointmentCode,
            PatientId = visit.PatientId,
            PatientName = visit.Patient?.FullName ?? "Bệnh nhân",
            MedicalRecordNumber = visit.Patient?.MedicalRecordNumber ?? string.Empty,
            PhoneNumber = visit.Patient?.PhoneNumber ?? string.Empty,
            QueueNumber = visit.QueueNumber,
            QueueDisplay = $"{deptCode}-{visit.QueueNumber:D3}",
            FacilityId = visit.FacilityId,
            FacilityName = visit.Facility?.Name ?? "Bệnh viện ClinicCare",
            DepartmentId = visit.DepartmentId,
            DepartmentName = visit.Department?.Name ?? string.Empty,
            RoomId = visit.RoomId,
            RoomNumber = visit.Room != null ? $"{visit.Room.RoomNumber} - {visit.Room.Name}" : null,
            AssignedDoctorId = visit.AssignedDoctorId,
            DoctorName = doctorName,
            CheckedInAtUtc = visit.CheckedInAtUtc,
            ReceptionistName = receptionistName,
            Status = visit.Status.ToString(),
            Priority = visit.Priority.ToString(),
            ArrivalType = visit.ArrivalType.ToString()
        };
    }

    private async Task<Patient> ResolveOrCreatePatientAsync(WalkInRegistrationRequest request, long facilityId, CancellationToken cancellationToken)
    {
        Patient? patient = null;

        // 0. Try finding by ExistingPatientId if provided
        if (request.ExistingPatientId.HasValue && request.ExistingPatientId.Value > 0)
        {
            patient = await _dbContext.Patients
                .FirstOrDefaultAsync(p => p.Id == request.ExistingPatientId.Value, cancellationToken);
        }

        var cleanPhone = request.PhoneNumber?.Trim();
        var cleanName = request.FullName?.Trim() ?? string.Empty;
        var cleanCccd = string.IsNullOrWhiteSpace(request.IdentityCardNumber)
            ? null
            : request.IdentityCardNumber.Trim().Replace(" ", "").Replace("-", "");

        // 1. Try finding by CCCD/NationalId
        if (patient == null && !string.IsNullOrEmpty(cleanCccd))
        {
            patient = await _dbContext.Patients
                .FirstOrDefaultAsync(p => p.NationalId == cleanCccd, cancellationToken);
        }

        // 2. Try finding by Phone and Name
        if (patient == null && !string.IsNullOrEmpty(cleanPhone) && !string.IsNullOrEmpty(cleanName))
        {
            patient = await _dbContext.Patients
                .FirstOrDefaultAsync(p => p.PhoneNumber == cleanPhone && p.FullName == cleanName, cancellationToken);
        }

        if (patient != null)
        {
            // Backfill CCCD/Address if missing
            if (string.IsNullOrEmpty(patient.NationalId) && !string.IsNullOrEmpty(cleanCccd))
            {
                patient.NationalId = cleanCccd;
            }
            if (string.IsNullOrEmpty(patient.Address) && !string.IsNullOrEmpty(request.Address))
            {
                patient.Address = request.Address.Trim();
            }
            if (!patient.DateOfBirth.HasValue && request.DateOfBirth.HasValue)
            {
                patient.DateOfBirth = request.DateOfBirth;
            }
            if (!patient.Gender.HasValue && request.Gender.HasValue)
            {
                patient.Gender = request.Gender;
            }
            return patient;
        }

        // 3. Create new Walk-in Patient (UserId = null)
        var mrn = await _mrnGenerator.GenerateNextMrnAsync(cancellationToken);

        patient = new Patient
        {
            UserId = null,
            FullName = cleanName,
            PhoneNumber = cleanPhone,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            Address = request.Address?.Trim(),
            NationalId = cleanCccd,
            MedicalRecordNumber = mrn,
            PrimaryFacilityId = facilityId
        };

        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return patient;
    }

    private async Task<int> GetNextQueueNumberAsync(long facilityId, long departmentId, DateOnly date, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var sequence = await _dbContext.DailyQueueSequences
                .FirstOrDefaultAsync(s => s.FacilityId == facilityId && s.DepartmentId == departmentId && s.Date == date, cancellationToken);

            if (sequence == null)
            {
                sequence = new DailyQueueSequence
                {
                    FacilityId = facilityId,
                    DepartmentId = departmentId,
                    Date = date,
                    LastNumber = 1
                };
                _dbContext.DailyQueueSequences.Add(sequence);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return sequence.LastNumber;
                }
                catch (DbUpdateException)
                {
                    _dbContext.Entry(sequence).State = EntityState.Detached;
                    await Task.Delay(RandomNumberGenerator.GetInt32(10, 50), cancellationToken);
                    continue;
                }
            }
            else
            {
                sequence.LastNumber++;
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return sequence.LastNumber;
                }
                catch (DbUpdateConcurrencyException)
                {
                    try { await _dbContext.Entry(sequence).ReloadAsync(cancellationToken); } catch { _dbContext.Entry(sequence).State = EntityState.Detached; }
                    await Task.Delay(RandomNumberGenerator.GetInt32(10, 50), cancellationToken);
                    continue;
                }
                catch (DbUpdateException)
                {
                    try { await _dbContext.Entry(sequence).ReloadAsync(cancellationToken); } catch { _dbContext.Entry(sequence).State = EntityState.Detached; }
                    await Task.Delay(RandomNumberGenerator.GetInt32(10, 50), cancellationToken);
                    continue;
                }
            }
        }

        throw new InvalidOperationException("Failed to allocate queue number after multiple attempts due to high concurrency (QUEUE_NUMBER_COLLISION).");
    }

    private async Task<string> GenerateVisitCodeAsync(DateOnly date, int queueNumber, CancellationToken cancellationToken)
    {
        var baseCode = $"VIS-{date:yyyyMMdd}-{queueNumber:D4}";
        var exists = await _dbContext.PatientVisits.AnyAsync(v => v.VisitCode == baseCode, cancellationToken);
        if (!exists) return baseCode;

        for (var i = 0; i < 5; i++)
        {
            var suffix = RandomNumberGenerator.GetInt32(100, 999);
            var candidate = $"{baseCode}-{suffix}";
            if (!await _dbContext.PatientVisits.AnyAsync(v => v.VisitCode == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return $"{baseCode}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";
    }

    private async Task<string> GetUserNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) return "Hệ thống";
        var name = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken);
        return name ?? "Lễ tân";
    }

    private async Task<string?> GetDoctorNameAsync(long? doctorId, CancellationToken cancellationToken = default)
    {
        if (!doctorId.HasValue) return null;
        var query = from d in _dbContext.Doctors.AsNoTracking()
                    join u in _dbContext.Users.AsNoTracking() on d.UserId equals u.Id
                    where d.Id == doctorId.Value
                    select u.FullName;
        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Dictionary<long, string>> GetDoctorNamesAsync(IEnumerable<long> doctorIds, CancellationToken cancellationToken = default)
    {
        var ids = doctorIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, string>();
        var query = from d in _dbContext.Doctors.AsNoTracking()
                    join u in _dbContext.Users.AsNoTracking() on d.UserId equals u.Id
                    where ids.Contains(d.Id)
                    select new { d.Id, u.FullName };
        return await query.ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);
    }
}
