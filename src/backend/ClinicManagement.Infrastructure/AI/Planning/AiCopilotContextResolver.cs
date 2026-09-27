using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Planning;

/// <summary>Resolves only server-verifiable, actor-owned resource context.</summary>
public sealed class AiCopilotContextResolver : IAiCopilotContextResolver
{
    private readonly AppDbContext _db;

    public AiCopilotContextResolver(AppDbContext db) => _db = db;

    public async Task<AiContextResolutionResult> ResolveAsync(
        AiCopilotRequestDto request,
        AiConversationMemoryState? memory,
        AiActorRole role,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        if (!actorId.HasValue || actorId == Guid.Empty)
            return AiContextResolutionResult.Invalid("AUTHENTICATION_REQUIRED", "Cần đăng nhập lại để xác minh phạm vi dữ liệu.");

        var hinted = request.ResourceContext;
        var candidate = hinted is null
            ? memory?.CurrentResource ?? new AiResolvedResourceContext()
            : new AiResolvedResourceContext
            {
                AppointmentId = hinted.AppointmentId,
                VisitId = hinted.VisitId,
                EncounterId = hinted.EncounterId,
                DiagnosticOrderId = hinted.DiagnosticOrderId,
                PrescriptionId = hinted.PrescriptionId,
                ResourceVersion = request.ResourceVersion
            };

        if (role == AiActorRole.Admin && HasAnyResource(candidate))
            return AiContextResolutionResult.Invalid("RESOURCE_CONTEXT_NOT_ALLOWED", "Admin copilot chỉ trả dữ liệu tổng hợp, không mở hồ sơ bệnh nhân.");
        if (candidate.EncounterId.HasValue)
            return AiContextResolutionResult.Invalid("RESOURCE_CONTEXT_UNSUPPORTED", "Encounter context chưa có nguồn dữ liệu xác minh độc lập.");

        if (candidate.AppointmentId.HasValue && !await CanReadAppointment(role, actorId.Value, candidate.AppointmentId.Value, request.ResourceVersion, cancellationToken))
            return AiContextResolutionResult.Invalid("RESOURCE_SCOPE_DENIED", "Lịch hẹn không còn thuộc phạm vi được phân quyền hoặc phiên bản đã thay đổi.");
        if (candidate.VisitId.HasValue && !await CanReadVisit(role, actorId.Value, candidate.VisitId.Value, request.ResourceVersion, cancellationToken))
            return AiContextResolutionResult.Invalid("RESOURCE_SCOPE_DENIED", "Lượt khám không còn thuộc phạm vi được phân quyền hoặc phiên bản đã thay đổi.");
        if (candidate.DiagnosticOrderId.HasValue && !await CanReadOrder(role, actorId.Value, candidate.DiagnosticOrderId.Value, request.ResourceVersion, cancellationToken))
            return AiContextResolutionResult.Invalid("RESOURCE_SCOPE_DENIED", "Chỉ định không còn thuộc phạm vi được phân quyền hoặc phiên bản đã thay đổi.");
        if (candidate.PrescriptionId.HasValue && !await CanReadPrescription(role, actorId.Value, candidate.PrescriptionId.Value, request.ResourceVersion, cancellationToken))
            return AiContextResolutionResult.Invalid("RESOURCE_SCOPE_DENIED", "Đơn thuốc không còn thuộc phạm vi được phân quyền hoặc phiên bản đã thay đổi.");

        var hasPending = await _db.AiPendingToolActions.AsNoTracking().AnyAsync(x =>
            x.UserId == actorId.Value &&
            (x.State == AiPendingToolActionState.PendingConfirmation || x.State == AiPendingToolActionState.Executing || x.State == AiPendingToolActionState.FailedRetryable), cancellationToken);

        return AiContextResolutionResult.Valid(candidate, hasPending);
    }

    private async Task<bool> CanReadAppointment(AiActorRole role, Guid actorId, long id, string? version, CancellationToken ct)
    {
        var item = await _db.Appointments.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Doctor.UserId, PatientUserId = x.Patient.UserId, x.DoctorId, x.PatientId,
                x.AppointmentSlotId, x.AppointmentDate, x.StartTime, x.Status
            }).SingleOrDefaultAsync(ct);
        if (item is null) return false;
        var scoped = role switch
        {
            AiActorRole.Doctor => item.UserId == actorId,
            AiActorRole.Patient => item.PatientUserId == actorId,
            AiActorRole.Receptionist => await HasSharedDoctorFacility(actorId, item.UserId, AiActorRole.Receptionist, ct),
            _ => false
        };
        if (!scoped) return false;
        if (string.IsNullOrWhiteSpace(version)) return true;
        var canonical = $"{item.Id}|{item.DoctorId}|{item.PatientId}|{item.AppointmentSlotId}|{item.AppointmentDate:yyyy-MM-dd}|{item.StartTime:HH:mm:ss}|{item.Status}";
        return FixedEquals(version, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    private async Task<bool> CanReadVisit(AiActorRole role, Guid actorId, long id, string? version, CancellationToken ct)
    {
        var item = await _db.PatientVisits.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.AssignedDoctorId, DoctorUserId = x.AssignedDoctor != null ? (Guid?)x.AssignedDoctor.UserId : null, PatientUserId = x.Patient.UserId, x.FacilityId, x.RowVersion }).SingleOrDefaultAsync(ct);
        if (item is null) return false;
        var scoped = role switch
        {
            AiActorRole.Doctor => item.DoctorUserId == actorId,
            AiActorRole.Patient => item.PatientUserId == actorId,
            AiActorRole.Receptionist => await HasFacility(actorId, item.FacilityId, role, null, ct),
            AiActorRole.Pharmacist => await HasFacility(actorId, item.FacilityId, role, null, ct),
            _ => false
        };
        return scoped && VersionMatches(version, item.RowVersion);
    }

    private async Task<bool> CanReadOrder(AiActorRole role, Guid actorId, long id, string? version, CancellationToken ct)
    {
        var item = await _db.DiagnosticOrders.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { DoctorUserId = x.OrderingDoctor.UserId, PatientUserId = x.Patient.UserId, x.FacilityId, x.PerformingDepartmentId, x.RowVersion }).SingleOrDefaultAsync(ct);
        if (item is null) return false;
        var scoped = role switch
        {
            AiActorRole.Doctor => item.DoctorUserId == actorId,
            AiActorRole.Patient => item.PatientUserId == actorId,
            AiActorRole.DiagnosticTechnician when item.FacilityId.HasValue => await HasFacility(actorId, item.FacilityId.Value, role, item.PerformingDepartmentId, ct),
            _ => false
        };
        return scoped && VersionMatches(version, item.RowVersion);
    }

    private async Task<bool> CanReadPrescription(AiActorRole role, Guid actorId, long id, string? version, CancellationToken ct)
    {
        var item = await _db.Prescriptions.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { DoctorUserId = x.Doctor!.UserId, PatientUserId = x.Patient!.UserId, FacilityId = x.PatientVisit != null ? (long?)x.PatientVisit.FacilityId : null, x.RowVersion }).SingleOrDefaultAsync(ct);
        if (item is null) return false;
        var scoped = role switch
        {
            AiActorRole.Doctor => item.DoctorUserId == actorId,
            AiActorRole.Patient => item.PatientUserId == actorId,
            AiActorRole.Pharmacist when item.FacilityId.HasValue => await HasFacility(actorId, item.FacilityId.Value, role, null, ct),
            _ => false
        };
        return scoped && VersionMatches(version, item.RowVersion);
    }

    private Task<bool> HasFacility(Guid userId, long facilityId, AiActorRole role, long? departmentId, CancellationToken ct) =>
        _db.StaffFacilityAssignments.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsActive && x.Role == role.ToString() && x.FacilityId == facilityId && (!departmentId.HasValue || !x.DepartmentId.HasValue || x.DepartmentId == departmentId), ct);

    private Task<bool> HasSharedDoctorFacility(Guid actorId, Guid doctorUserId, AiActorRole actorRole, CancellationToken ct) =>
        _db.StaffFacilityAssignments.AsNoTracking().AnyAsync(actor => actor.UserId == actorId && actor.IsActive && actor.Role == actorRole.ToString() &&
            _db.StaffFacilityAssignments.Any(doctor => doctor.UserId == doctorUserId && doctor.IsActive && doctor.Role == AiActorRole.Doctor.ToString() && doctor.FacilityId == actor.FacilityId), ct);

    private static bool HasAnyResource(AiResolvedResourceContext x) => x.AppointmentId.HasValue || x.VisitId.HasValue || x.EncounterId.HasValue || x.DiagnosticOrderId.HasValue || x.PrescriptionId.HasValue;
    private static bool VersionMatches(string? requested, byte[]? actual) => string.IsNullOrWhiteSpace(requested) || actual is not null && FixedEquals(requested, Convert.ToBase64String(actual));
    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
