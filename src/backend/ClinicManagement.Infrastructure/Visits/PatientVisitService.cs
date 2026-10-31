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

public partial class PatientVisitService : IPatientVisitService
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

    private async Task EnsureDoctorNotOnApprovedLeaveAsync(long doctorId, CancellationToken cancellationToken)
    {
        var vietnamNow = _dateTimeProvider.VietnamNow;
        var onLeave = await _dbContext.DoctorLeaveRequests.AsNoTracking()
            .AnyAsync(leave => leave.DoctorId == doctorId
                && leave.Status == DoctorLeaveRequestStatus.Approved
                && leave.StartDateTime <= vietnamNow && leave.EndDateTime > vietnamNow, cancellationToken);
        if (onLeave)
            throw new BusinessException("DOCTOR_ON_LEAVE", "Bác sĩ đang trong lịch nghỉ đã được duyệt, vui lòng chọn bác sĩ khác.");
    }

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





    public async Task<CheckInTicketDto> CheckInAppointmentAsync(AppointmentCheckInRequest request, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetUserId();

        // Active visits keep their ticket; cancelled visits must pass fresh check-in validation.
        var existingVisit = await _dbContext.PatientVisits
            .Include(v => v.Patient)
            .Include(v => v.Department)
            .Include(v => v.Facility)
            .Include(v => v.Room)
            .Include(v => v.AssignedDoctor)
            .Include(v => v.Appointment)
            .FirstOrDefaultAsync(v => v.AppointmentId == request.AppointmentId, cancellationToken);

        if (existingVisit != null && existingVisit.Status != VisitStatus.Cancelled)
        {
            await _facilityAuthService.ValidateUserFacilityAccessAsync(currentUserId, existingVisit.FacilityId, cancellationToken);
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

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            var code = appointment.Status switch
            {
                AppointmentStatus.Pending => "CHECKIN_APPOINTMENT_PENDING",
                AppointmentStatus.NoShow => "CHECKIN_APPOINTMENT_NO_SHOW",
                AppointmentStatus.CheckedIn => "CHECKIN_VISIT_MISSING",
                _ => "CHECKIN_APPOINTMENT_NOT_CONFIRMED"
            };
            throw new BusinessException(code, "Chỉ có thể tiếp nhận lịch hẹn đã xác nhận và chưa có lượt khám đang hoạt động.");
        }

        // Appointments created before the facility binding migration can only
        // be repaired when one facility is provably eligible. Ambiguous legacy
        // rows stay fail-closed instead of being assigned to an arbitrary
        // doctor location.
        var appointmentFacilityId = appointment.FacilityId ?? await ResolveUniqueLegacyAppointmentFacilityAsync(appointment, cancellationToken);
        if (!appointmentFacilityId.HasValue)
            throw new BusinessException("APPOINTMENT_FACILITY_UNBOUND", "Lịch hẹn chưa được ràng vào cơ sở y tế; không thể check-in an toàn.");
        if (!appointment.FacilityId.HasValue)
            appointment.FacilityId = appointmentFacilityId.Value;
        if (request.FacilityId.HasValue && request.FacilityId.Value != appointmentFacilityId.Value)
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Cơ sở yêu cầu không khớp với cơ sở đã ràng cho lịch hẹn.");

        // 3. Resolve Department
        Department? department = null;
        if (request.DepartmentId.HasValue)
        {
            department = await _dbContext.Departments
                .Include(d => d.Facility)
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value && d.FacilityId == appointmentFacilityId.Value && d.IsActive, cancellationToken);
            if (department == null || department.SpecialtyId != appointment.SpecialtyId)
                throw new BusinessException("CHECKIN_DEPARTMENT_INVALID", "Khoa được chọn không hoạt động, không thuộc cơ sở hoặc không đúng chuyên khoa của lịch hẹn.");
        }

        if (department == null)
        {
            department = await _dbContext.Departments
                .Include(d => d.Facility)
                .FirstOrDefaultAsync(d => d.FacilityId == appointmentFacilityId.Value && d.SpecialtyId == appointment.SpecialtyId && d.IsActive, cancellationToken);
        }

        if (department == null)
        {
            throw new BusinessException("CHECKIN_SPECIALTY_DEPARTMENT_UNAVAILABLE", "Cơ sở của lịch hẹn không có khoa đang hoạt động đúng chuyên khoa; không thể tiếp nhận.");
        }

        var facilityId = appointmentFacilityId.Value;
        await _facilityAuthService.ValidateUserFacilityAccessAsync(currentUserId, facilityId, cancellationToken);

        if (appointment.AppointmentDate != _dateTimeProvider.VietnamToday)
            throw new BusinessException("CHECKIN_NOT_TODAY", $"Lịch hẹn ngày {appointment.AppointmentDate:dd/MM/yyyy}; chỉ tiếp nhận được vào đúng ngày khám.");

        // Verify room if specified
        Room? room = null;
        if (request.RoomId.HasValue)
        {
            room = await _dbContext.Rooms
                .FirstOrDefaultAsync(r => r.Id == request.RoomId.Value && r.DepartmentId == department.Id && r.IsActive, cancellationToken);
            if (room == null)
                throw new BusinessException("CHECKIN_ROOM_INVALID", "Phòng được chọn không tồn tại, đã ngừng hoạt động hoặc không thuộc khoa tiếp nhận.");
        }

        var assignedDoctorId = request.AssignedDoctorId ?? appointment.DoctorId;
        var doctor = await _dbContext.Doctors
            .FirstOrDefaultAsync(d => d.Id == assignedDoctorId && d.IsActive, cancellationToken);
        if (doctor == null || !await _dbContext.StaffFacilityAssignments.AsNoTracking().AnyAsync(
                x => x.UserId == doctor.UserId && x.IsActive && x.Role == "Doctor" && x.FacilityId == facilityId,
                cancellationToken))
        {
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Bác sĩ được phân công không thuộc cơ sở của lịch hẹn.");
        }

        await EnsureDoctorNotOnApprovedLeaveAsync(assignedDoctorId, cancellationToken);

        var queueNumber = await GetNextQueueNumberAsync(facilityId, department.Id, appointment.AppointmentDate, cancellationToken);
        var now = _dateTimeProvider.UtcNow;
        var visit = existingVisit ?? new PatientVisit
        {
            VisitCode = await GenerateVisitCodeAsync(appointment.AppointmentDate, queueNumber, cancellationToken),
            PatientId = appointment.PatientId,
            AppointmentId = appointment.Id,
            ArrivalType = VisitArrivalType.Scheduled,
            Priority = VisitPriority.Normal,
            ChiefComplaint = appointment.Reason,
            CreatedByUserId = currentUserId,
            CreatedAtUtc = now
        };
        visit.FacilityId = facilityId;
        visit.DepartmentId = department.Id;
        visit.RoomId = room?.Id;
        visit.AssignedDoctorId = assignedDoctorId;
        visit.VisitDate = appointment.AppointmentDate;
        visit.QueueNumber = queueNumber;
        visit.Status = VisitStatus.WaitingForDoctor;
        visit.CheckedInAtUtc = now;
        visit.CancelledAtUtc = null;
        visit.CancellationReason = null;
        if (existingVisit == null)
            _dbContext.PatientVisits.Add(visit);
        else
            visit.UpdatedAtUtc = now;
        var visitCode = visit.VisitCode;
        // Each re-check-in issues a new queue ticket; first-check-in keys stay unchanged.
        var notificationSuffix = existingVisit == null ? string.Empty
            : $":recheck:{department.Id}:{appointment.AppointmentDate:yyyyMMdd}:{queueNumber}";

        // Update appointment status to CheckedIn
        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.CheckedIn;

        _dbContext.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id,
            Action = AppointmentHistoryAction.CheckedIn,
            OldStatus = oldStatus,
            NewStatus = AppointmentStatus.CheckedIn,
            Note = doctor.UserId == currentUserId
                ? $"Bác sĩ tiếp nhận bệnh nhân vào phòng khám. Mã lượt khám: {visitCode}, STT: {queueNumber}"
                : $"Tiếp nhận bệnh nhân tại quầy lễ tân. Mã lượt khám: {visitCode}, STT: {queueNumber}",
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
                // The visit uses a database-generated key and has not been
                // saved yet. Bind the transactional notification to the
                // stable appointment instead of the temporary visit key.
                RelatedEntityType = "Appointment",
                RelatedEntityId = appointment.Id.ToString(),
                DedupeKey = $"checkin_appointment_{appointment.Id}{notificationSuffix}",
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
                DedupeKey = $"appt_checkin_doc_{appointment.Id}_{doctor.UserId}{notificationSuffix}",
                IsRead = false,
                CreatedAtUtc = _dateTimeProvider.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var receptionistName = await GetUserNameAsync(visit.CreatedByUserId, cancellationToken);
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

        await ValidateVisitReadAccessAsync(visit, cancellationToken);

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
        var userId = GetUserId();
        Doctor? doctor = null;
        if (userId != Guid.Empty)
        {
            var department = await _dbContext.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
                ?? throw new NotFoundException("Khoa tiếp nhận không tồn tại.");
            await _facilityAuthService.ValidateUserFacilityAccessAsync(userId, department.FacilityId, cancellationToken);
            doctor = await GetReadDoctorAsync(userId, cancellationToken);
        }
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

        if (doctor != null)
        {
            var departmentIds = await VisitDoctorAuthorizationGuard.GetUnassignedDepartmentIdsAsync(_dbContext, doctor);
            visits = visits.Where(v => v.AssignedDoctorId == doctor.Id ||
                (!v.AssignedDoctorId.HasValue && departmentIds.Contains(v.DepartmentId))).ToList();
        }

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

        await _facilityAuthService.ValidateUserFacilityAccessAsync(GetUserId(), visit.FacilityId, cancellationToken);
        // This operation returns visit detail; validate that read before persisting a reassignment.
        await ValidateVisitReadAccessAsync(visit, cancellationToken);
        var readDoctor = await GetReadDoctorAsync(GetUserId(), cancellationToken);
        if (readDoctor != null && request.DoctorId != readDoctor.Id)
            throw new NotFoundException("Lượt khám không tồn tại hoặc không thuộc quyền quản lý.");

        var doctor = await _dbContext.Doctors
            .FirstOrDefaultAsync(d => d.Id == request.DoctorId && d.IsActive, cancellationToken);

        if (doctor == null)
            throw new NotFoundException("Bác sĩ không tồn tại hoặc đã ngừng hoạt động.");

        var hasDoctorAssignment = await _dbContext.StaffFacilityAssignments
            .AnyAsync(a => a.UserId == doctor.UserId && a.FacilityId == visit.FacilityId &&
                a.IsActive && a.Role == RoleNames.Doctor, cancellationToken);
        if (!hasDoctorAssignment)
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Bác sĩ không được phân quyền tại cơ sở của lượt khám.");

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
        var currentUserId = GetUserId();
        if (currentUserId != Guid.Empty)
        {
            await _facilityAuthService.ValidateVisitAccessAsync(currentUserId, visitId, cancellationToken);
        }

        var visit = await _dbContext.PatientVisits.FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);
        if (visit == null)
            throw new NotFoundException("Lượt khám không tồn tại.");

        // Avoid saving a status change and then rejecting the returned detail.
        await ValidateVisitReadAccessAsync(visit, cancellationToken);

        if (visit.Status == VisitStatus.Cancelled)
            throw new BusinessException("VISIT_ALREADY_CANCELLED", "Lượt khám đã bị hủy, không thể thay đổi trạng thái.");

        if (newStatus == visit.Status)
            return await GetVisitByIdAsync(visit.Id, cancellationToken);

        if (newStatus == VisitStatus.Completed)
        {
            var (canComplete, incompleteReason) = await VisitCompletionCoordinator.CanCompleteVisitAsync(visit.Id, _dbContext, cancellationToken);
            if (!canComplete)
            {
                throw new BusinessException("VISIT_CANNOT_COMPLETE", $"Lượt khám chưa đủ điều kiện hoàn tất: {incompleteReason}");
            }
        }

        visit.Status = newStatus;
        visit.UpdatedAtUtc = _dateTimeProvider.UtcNow;

        if (newStatus == VisitStatus.InConsultation && !visit.ConsultationStartedAtUtc.HasValue)
        {
            visit.ConsultationStartedAtUtc = _dateTimeProvider.UtcNow;
        }
        else if (newStatus == VisitStatus.Completed)
        {
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
            if (visit.AppointmentId.HasValue)
            {
                var appointment = await _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, cancellationToken);
                if (appointment?.Status == AppointmentStatus.CheckedIn)
                {
                    appointment.Status = AppointmentStatus.Confirmed;
                    _dbContext.AppointmentHistories.Add(new AppointmentHistory
                    {
                        AppointmentId = appointment.Id,
                        Action = AppointmentHistoryAction.Confirmed,
                        OldStatus = AppointmentStatus.CheckedIn,
                        NewStatus = AppointmentStatus.Confirmed,
                        Note = $"Trả lịch hẹn về trạng thái đã xác nhận sau khi hủy lượt khám {visit.VisitCode}.",
                        PerformedByUserId = currentUserId,
                        CreatedAt = _dateTimeProvider.UtcNow
                    });
                }
            }
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

        await ValidateVisitReadAccessAsync(visit, cancellationToken);

        var rName = await GetUserNameAsync(visit.CreatedByUserId, cancellationToken);
        var docName = await GetDoctorNameAsync(visit.AssignedDoctorId, cancellationToken);
        return MapToTicket(visit, rName, docName);
    }

    private async Task ValidateDoctorFacilityAssignmentAsync(Doctor doctor, long facilityId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.StaffFacilityAssignments.AsNoTracking().AnyAsync(
            a => a.UserId == doctor.UserId && a.FacilityId == facilityId && a.IsActive && a.Role == RoleNames.Doctor,
            cancellationToken))
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Bác sĩ không được phân quyền tại cơ sở của lượt khám.");
    }

    private async Task<Doctor?> GetReadDoctorAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await _facilityAuthService.HasFullFacilityAccessAsync(userId, cancellationToken)) return null;
        var isDoctor = await (from ur in _dbContext.UserRoles
                              join role in _dbContext.Roles on ur.RoleId equals role.Id
                              where ur.UserId == userId && role.Name == RoleNames.Doctor
                              select ur).AnyAsync(cancellationToken);
        if (!isDoctor) return null;
        return await _dbContext.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.IsActive, cancellationToken)
            ?? throw new NotFoundException("Bác sĩ không tồn tại hoặc không hoạt động.");
    }

    private async Task ValidateVisitReadAccessAsync(PatientVisit visit, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return;
        await _facilityAuthService.ValidateUserFacilityAccessAsync(userId, visit.FacilityId, cancellationToken);
        var doctor = await GetReadDoctorAsync(userId, cancellationToken);
        if (doctor != null)
        {
            VisitDoctorAuthorizationGuard.ValidateAssignedDoctor(visit, doctor, allowUnassigned: true);
            await VisitDoctorAuthorizationGuard.ValidateUnassignedDepartmentAsync(_dbContext, visit, doctor);
        }
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

    private async Task<long?> ResolveUniqueLegacyAppointmentFacilityAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        var candidates = await (from assignment in _dbContext.StaffFacilityAssignments.AsNoTracking()
                                join department in _dbContext.Departments.AsNoTracking() on assignment.FacilityId equals department.FacilityId
                                join facility in _dbContext.Facilities.AsNoTracking() on assignment.FacilityId equals facility.Id
                                where assignment.UserId == appointment.Doctor.UserId &&
                                      assignment.IsActive &&
                                      assignment.Role == "Doctor" &&
                                      facility.IsActive &&
                                      department.IsActive &&
                                      department.SpecialtyId == appointment.SpecialtyId &&
                                      (!assignment.DepartmentId.HasValue || assignment.DepartmentId == department.Id)
                                select assignment.FacilityId)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken);

        return candidates.Count == 1 ? candidates[0] : null;
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

        foreach (var allergy in request.Allergies)
        {
            var severity = Enum.TryParse<AllergySeverity>(allergy.Severity, true, out var parsed) ? parsed : AllergySeverity.Moderate;
            patient.Allergies.Add(new PatientAllergy
            {
                AllergenName = allergy.Allergen.Trim(), Severity = severity,
                ReactionDescription = allergy.Reaction?.Trim(), AllergenType = AllergenType.Drug
            });
        }
        if (request.EmergencyContact is { } contact)
        {
            patient.EmergencyContacts.Add(new EmergencyContact
            {
                FullName = contact.ContactName.Trim(), Relationship = contact.Relationship.Trim(),
                PhoneNumber = contact.PhoneNumber.Trim(), IsGuardian = contact.IsGuardian, IsPrimary = true
            });
        }

        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return patient;
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
