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

        var currentRoute = NormalizeRoute(request.CurrentRoute);
        if (!string.IsNullOrWhiteSpace(request.CurrentRoute) && currentRoute is null)
            return AiContextResolutionResult.Invalid("RESOURCE_CONTEXT_NOT_ALLOWED", "Route hiện tại không hợp lệ.");

        var hinted = request.ResourceContext;
        var candidate = hinted is null
            ? memory?.CurrentResource ?? new AiResolvedResourceContext { CurrentRoute = currentRoute }
            : new AiResolvedResourceContext
            {
                CurrentRoute = currentRoute,
                AppointmentId = hinted.AppointmentId,
                VisitId = hinted.VisitId,
                EncounterId = hinted.EncounterId,
                DiagnosticOrderId = hinted.DiagnosticOrderId,
                PrescriptionId = hinted.PrescriptionId,
                ResourceVersion = request.ResourceVersion
            };
        if (hinted is null && currentRoute is not null)
            candidate = new AiResolvedResourceContext
            {
                CurrentRoute = currentRoute,
                AppointmentId = candidate.AppointmentId,
                VisitId = candidate.VisitId,
                EncounterId = candidate.EncounterId,
                DiagnosticOrderId = candidate.DiagnosticOrderId,
                PrescriptionId = candidate.PrescriptionId,
                ResourceVersion = request.ResourceVersion ?? candidate.ResourceVersion
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
        if (!await IsCompositeContextConsistentAsync(candidate, cancellationToken))
            return AiContextResolutionResult.Invalid("RESOURCE_CONTEXT_MISMATCH", "Các tài nguyên được chọn không thuộc cùng một ca khám được xác minh.");

        var now = DateTime.UtcNow;
        // Executing blocks only for its existing two-minute backend lease, even
        // if confirmation TTL has elapsed. Retryable failures have no live lease
        // and remain actionable only until confirmation TTL. This does not mutate
        // actions or authorize replay; confirm still enforces its own state checks.
        var hasPending = await _db.AiPendingToolActions.AsNoTracking().AnyAsync(x =>
            x.UserId == actorId.Value &&
            (((x.State == AiPendingToolActionState.PendingConfirmation || x.State == AiPendingToolActionState.FailedRetryable) && x.ExpiresAtUtc > now) ||
             (x.State == AiPendingToolActionState.Executing && x.ExecutionLeaseExpiresAtUtc > now)), cancellationToken);

        return AiContextResolutionResult.Valid(candidate, hasPending);
    }

    private async Task<bool> CanReadAppointment(AiActorRole role, Guid actorId, long id, string? version, CancellationToken ct)
    {
        var item = await _db.Appointments.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Doctor.UserId, PatientUserId = x.Patient.UserId, x.DoctorId, x.PatientId,
                x.FacilityId, x.AppointmentSlotId, x.AppointmentDate, x.StartTime, x.Status
            }).SingleOrDefaultAsync(ct);
        if (item is null) return false;
        var scoped = role switch
        {
            AiActorRole.Doctor when item.FacilityId.HasValue => item.UserId == actorId && await HasFacility(actorId, item.FacilityId.Value, role, null, ct),
            AiActorRole.Patient => item.PatientUserId == actorId,
            AiActorRole.Receptionist when item.FacilityId.HasValue => await HasFacility(actorId, item.FacilityId.Value, role, null, ct),
            _ => false
        };
        if (!scoped) return false;
        if (string.IsNullOrWhiteSpace(version)) return true;
        var canonical = $"{item.Id}|{item.DoctorId}|{item.PatientId}|{item.FacilityId}|{item.AppointmentSlotId}|{item.AppointmentDate:yyyy-MM-dd}|{item.StartTime:HH:mm:ss}|{item.Status}";
        return FixedEquals(version, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    private async Task<bool> CanReadVisit(AiActorRole role, Guid actorId, long id, string? version, CancellationToken ct)
    {
        var item = await _db.PatientVisits.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.AssignedDoctorId, DoctorUserId = x.AssignedDoctor != null ? (Guid?)x.AssignedDoctor.UserId : null, PatientUserId = x.Patient.UserId, x.FacilityId, x.DepartmentId, x.RowVersion }).SingleOrDefaultAsync(ct);
        if (item is null) return false;
        var scoped = role switch
        {
            AiActorRole.Doctor => item.DoctorUserId == actorId && await HasFacility(actorId, item.FacilityId, role, null, ct),
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
            AiActorRole.Doctor when item.FacilityId.HasValue => item.DoctorUserId == actorId && await HasFacility(actorId, item.FacilityId.Value, role, null, ct),
            AiActorRole.Patient => item.PatientUserId == actorId,
            AiActorRole.DiagnosticTechnician when item.FacilityId.HasValue && item.PerformingDepartmentId.HasValue => await HasFacility(actorId, item.FacilityId.Value, role, item.PerformingDepartmentId.Value, ct),
            _ => false
        };
        return scoped && VersionMatches(version, item.RowVersion);
    }

    private async Task<bool> CanReadPrescription(AiActorRole role, Guid actorId, long id, string? version, CancellationToken ct)
    {
        var item = await _db.Prescriptions.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                DoctorUserId = x.Doctor!.UserId,
                PatientUserId = x.Patient!.UserId,
                FacilityId = x.PatientVisit != null ? (long?)x.PatientVisit.FacilityId : x.Appointment != null ? x.Appointment.FacilityId : null,
                x.RowVersion
            }).SingleOrDefaultAsync(ct);
        if (item is null) return false;
        var scoped = role switch
        {
            AiActorRole.Doctor when item.FacilityId.HasValue => item.DoctorUserId == actorId && await HasFacility(actorId, item.FacilityId.Value, role, null, ct),
            AiActorRole.Patient => item.PatientUserId == actorId,
            AiActorRole.Pharmacist when item.FacilityId.HasValue => await HasFacility(actorId, item.FacilityId.Value, role, null, ct),
            _ => false
        };
        return scoped && VersionMatches(version, item.RowVersion);
    }

    private Task<bool> HasFacility(Guid userId, long facilityId, AiActorRole role, long? departmentId, CancellationToken ct) =>
        _db.StaffFacilityAssignments.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsActive && x.Role == role.ToString() && x.FacilityId == facilityId && (!departmentId.HasValue || x.DepartmentId == departmentId), ct);

    private async Task<bool> IsCompositeContextConsistentAsync(AiResolvedResourceContext context, CancellationToken ct)
    {
        var links = new List<ResourceLink>();
        ResourceLink? appointment = null;
        ResourceLink? visit = null;
        ResourceLink? order = null;
        ResourceLink? prescription = null;

        if (context.AppointmentId.HasValue)
            appointment = await _db.Appointments.AsNoTracking().Where(x => x.Id == context.AppointmentId.Value)
                .Select(x => new ResourceLink { ResourceId = x.Id, PatientId = x.PatientId, FacilityId = x.FacilityId, LinkedAppointmentId = x.Id, LinkedVisitId = x.PatientVisit == null ? null : x.PatientVisit.Id }).SingleOrDefaultAsync(ct);
        if (context.VisitId.HasValue)
            visit = await _db.PatientVisits.AsNoTracking().Where(x => x.Id == context.VisitId.Value)
                .Select(x => new ResourceLink { ResourceId = x.Id, PatientId = x.PatientId, FacilityId = x.FacilityId, LinkedAppointmentId = x.AppointmentId, LinkedVisitId = x.Id }).SingleOrDefaultAsync(ct);
        if (context.DiagnosticOrderId.HasValue)
            order = await _db.DiagnosticOrders.AsNoTracking().Where(x => x.Id == context.DiagnosticOrderId.Value)
                .Select(x => new ResourceLink { ResourceId = x.Id, PatientId = x.PatientId, FacilityId = x.FacilityId, LinkedAppointmentId = x.AppointmentId, LinkedVisitId = x.PatientVisitId }).SingleOrDefaultAsync(ct);
        if (context.PrescriptionId.HasValue)
            prescription = await _db.Prescriptions.AsNoTracking().Where(x => x.Id == context.PrescriptionId.Value)
                .Select(x => new ResourceLink { ResourceId = x.Id, PatientId = x.PatientId, FacilityId = x.PatientVisit != null ? (long?)x.PatientVisit.FacilityId : x.Appointment != null ? x.Appointment.FacilityId : null, LinkedAppointmentId = x.AppointmentId, LinkedVisitId = x.PatientVisitId }).SingleOrDefaultAsync(ct);

        if (context.AppointmentId.HasValue && appointment is null || context.VisitId.HasValue && visit is null || context.DiagnosticOrderId.HasValue && order is null || context.PrescriptionId.HasValue && prescription is null)
            return false;

        links.AddRange(new[] { appointment, visit, order, prescription }.Where(x => x is not null).Select(x => x!));
        if (links.Count <= 1) return true;
        if (links.Select(x => x.PatientId).Distinct().Count() != 1 || links.Any(x => !x.FacilityId.HasValue) || links.Select(x => x.FacilityId).Distinct().Count() != 1)
            return false;
        if (appointment is not null && visit is not null && visit.LinkedAppointmentId != appointment.ResourceId)
            return false;
        if (!IsLinkedToCase(order, appointment, visit) || !IsLinkedToCase(prescription, appointment, visit))
            return false;
        if (appointment is null && visit is null && order is not null && prescription is not null && !AreDirectlyLinked(order, prescription))
            return false;
        return true;
    }

    private static bool IsLinkedToCase(ResourceLink? child, ResourceLink? appointment, ResourceLink? visit)
    {
        if (child is null || appointment is null && visit is null) return true;
        var linked = (appointment is not null && (child.LinkedAppointmentId == appointment.ResourceId || appointment.LinkedVisitId.HasValue && child.LinkedVisitId == appointment.LinkedVisitId)) ||
                     (visit is not null && child.LinkedVisitId == visit.ResourceId) ||
                     (visit is not null && visit.LinkedAppointmentId.HasValue && child.LinkedAppointmentId == visit.LinkedAppointmentId);
        return linked;
    }

    private static bool AreDirectlyLinked(ResourceLink first, ResourceLink second) =>
        first.LinkedVisitId.HasValue && first.LinkedVisitId == second.LinkedVisitId ||
        first.LinkedAppointmentId.HasValue && first.LinkedAppointmentId == second.LinkedAppointmentId;

    private static bool HasAnyResource(AiResolvedResourceContext x) => x.AppointmentId.HasValue || x.VisitId.HasValue || x.EncounterId.HasValue || x.DiagnosticOrderId.HasValue || x.PrescriptionId.HasValue;
    private sealed class ResourceLink
    {
        public long ResourceId { get; init; }
        public long PatientId { get; init; }
        public long? FacilityId { get; init; }
        public long? LinkedAppointmentId { get; init; }
        public long? LinkedVisitId { get; init; }
    }
    private static string? NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return null;
        var value = route.Trim();
        return value.StartsWith("/", StringComparison.Ordinal) && !value.StartsWith("//", StringComparison.Ordinal) &&
               value.Length <= 256 && !value.Contains("://", StringComparison.Ordinal) && !value.Contains('\\') && !value.Contains("..", StringComparison.Ordinal)
            ? value
            : null;
    }
    private static bool VersionMatches(string? requested, byte[]? actual) => string.IsNullOrWhiteSpace(requested) || actual is not null && FixedEquals(requested, Convert.ToBase64String(actual));
    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
