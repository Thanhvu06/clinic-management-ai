using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

/// <summary>
/// Read-only dispatchers for professional workspaces. Every query derives
/// actor, role and facility scope from the authenticated server context.
/// </summary>
public sealed class RoleCopilotToolHandler : IAiToolHandler
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public RoleCopilotToolHandler(AppDbContext db, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public AiToolDefinition Definition { get; } = new() { Name = "role.copilot.dispatch", Version = "1.0" };

    public AiToolArgumentValidationResult ValidateArguments(AiToolInvocation invocation, AiToolExecutionContext context)
    {
        if (invocation.ArgumentsJson.Length > 4000)
            return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số công cụ vượt quá giới hạn.");
        try
        {
            using var document = JsonDocument.Parse(invocation.ArgumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số phải là JSON object.");
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name is "userId" or "actorId" or "role" or "facilityId" or "facilityAuthorization")
                    return AiToolArgumentValidationResult.Invalid("FORBIDDEN_TOOL_ARGUMENT", "Phạm vi quyền chỉ do server xác định.");
            }
            if (invocation.ToolName.Equals("reception.lookup_appointment", StringComparison.OrdinalIgnoreCase) &&
                (!document.RootElement.TryGetProperty("appointmentCode", out var code) || code.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(code.GetString())))
                return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Cần mã lịch hẹn để tra cứu.");
            return AiToolArgumentValidationResult.Valid();
        }
        catch (JsonException)
        {
            return AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số công cụ không hợp lệ.");
        }
    }

    public Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken = default) =>
        invocation.ToolName.Trim().ToLowerInvariant() switch
        {
            "reception.get_today_appointments" => GetReceptionAppointmentsAsync(context, cancellationToken),
            "reception.get_queue" => GetReceptionQueueAsync(context, cancellationToken),
            "reception.lookup_appointment" => LookupAppointmentAsync(context, invocation.ArgumentsJson, cancellationToken),
            "doctor.get_my_queue" => GetDoctorQueueAsync(context, cancellationToken),
            "doctor.get_patient_summary" => GetDoctorPatientSummaryAsync(context, invocation.ArgumentsJson, cancellationToken),
            "doctor.get_diagnostic_orders" => GetDoctorOrdersAsync(context, cancellationToken),
            "technician.get_worklist" => GetTechnicianWorklistAsync(context, cancellationToken),
            "pharmacist.get_prescription_queue" => GetPharmacyQueueAsync(context, cancellationToken),
            "pharmacist.get_inventory_status" => GetInventoryAsync(context, cancellationToken),
            "admin.get_dashboard_metrics" => GetAdminMetricsAsync(context, cancellationToken),
            "admin.get_ai_health" => GetAiHealthAsync(context, cancellationToken),
            _ => Task.FromResult(AiToolExecutionResult.Failed("UNKNOWN_TOOL", "Công cụ workspace không được hỗ trợ."))
        };

    private async Task<HashSet<long>> ResolveFacilityScopeAsync(AiToolExecutionContext context, string role, CancellationToken cancellationToken)
    {
        if (!context.ActorId.HasValue || context.ActorId == Guid.Empty) return new();
        var query = _db.StaffFacilityAssignments.AsNoTracking()
            .Where(x => x.UserId == context.ActorId.Value && x.IsActive && x.Role == role);
        if (context.FacilityId.HasValue)
            query = query.Where(x => x.FacilityId == context.FacilityId.Value);
        return (await query.Select(x => x.FacilityId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
    }

    private async Task<AiToolExecutionResult> GetReceptionAppointmentsAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var today = _clock.VietnamToday;
        var appointments = await _db.Appointments.AsNoTracking()
            .Where(a => a.AppointmentDate == today && _db.StaffFacilityAssignments.Any(s => s.IsActive && facilities.Contains(s.FacilityId) && s.UserId == a.Doctor.UserId))
            .OrderBy(a => a.StartTime).Take(100)
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber, doctorName = a.Doctor.UserId.ToString(), specialtyId = a.SpecialtyId })
            .ToListAsync(cancellationToken);
        return Completed(appointments, "reception_appointments", $"Có {appointments.Count} lịch hẹn trong ngày hôm nay.");
    }

    private async Task<AiToolExecutionResult> GetReceptionQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var queue = await _db.PatientVisits.AsNoTracking()
            .Where(v => facilities.Contains(v.FacilityId) && v.VisitDate == _clock.VietnamToday && v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.Completed)
            .OrderBy(v => v.QueueNumber).Take(100)
            .Select(v => new { v.Id, v.VisitCode, v.QueueNumber, v.Status, patientName = v.Patient.FullName, v.Patient.MedicalRecordNumber, v.AssignedDoctorId, v.DepartmentId })
            .ToListAsync(cancellationToken);
        return Completed(queue, "reception_queue", $"Hàng đợi hiện có {queue.Count} lượt.");
    }

    private async Task<AiToolExecutionResult> LookupAppointmentAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        using var document = JsonDocument.Parse(json);
        var code = document.RootElement.GetProperty("appointmentCode").GetString()!.Trim();
        var appointment = await _db.Appointments.AsNoTracking()
            .Where(a => a.AppointmentCode == code && _db.StaffFacilityAssignments.Any(s => s.IsActive && facilities.Contains(s.FacilityId) && s.UserId == a.Doctor.UserId))
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber })
            .SingleOrDefaultAsync(cancellationToken);
        return appointment is null ? AiToolExecutionResult.Failed("NOT_FOUND", "Không tìm thấy lịch hẹn trong phạm vi cơ sở được phân quyền.") : Completed(appointment, "appointment_lookup", "Đã tra cứu lịch hẹn từ hệ thống.");
    }

    private async Task<AiToolExecutionResult> GetDoctorQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var doctorId = await _db.Doctors.AsNoTracking().Where(d => d.UserId == context.ActorId).Select(d => (long?)d.Id).SingleOrDefaultAsync(cancellationToken);
        if (!doctorId.HasValue) return ScopeDenied();
        var items = await _db.PatientVisits.AsNoTracking()
            .Where(v => v.AssignedDoctorId == doctorId && v.VisitDate == _clock.VietnamToday && v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.Completed)
            .OrderBy(v => v.QueueNumber).Take(100)
            .Select(v => new { v.Id, v.VisitCode, v.QueueNumber, v.Status, patientName = v.Patient.FullName, v.Patient.MedicalRecordNumber, v.ChiefComplaint, v.AppointmentId })
            .ToListAsync(cancellationToken);
        return Completed(items, "doctor_queue", $"Hàng đợi của bạn có {items.Count} lượt.");
    }

    private async Task<AiToolExecutionResult> GetDoctorPatientSummaryAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        if (!TryGetLong(json, "appointmentId", out var appointmentId)) return AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần appointmentId hợp lệ.");
        var summary = await _db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId && a.Doctor.UserId == context.ActorId)
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber, a.Reason, a.SpecialtyId })
            .SingleOrDefaultAsync(cancellationToken);
        return summary is null ? AiToolExecutionResult.Failed("NOT_FOUND", "Ca khám không thuộc bác sĩ hiện tại.") : Completed(summary, "doctor_patient_summary", "Tóm tắt được giới hạn trong ca khám được phân công.");
    }

    private async Task<AiToolExecutionResult> GetDoctorOrdersAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var items = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.OrderingDoctor.UserId == context.ActorId && o.Status != DiagnosticOrderStatus.Cancelled)
            .OrderByDescending(o => o.OrderedAtUtc).Take(100)
            .Select(o => new { o.Id, o.OrderCode, o.PatientId, o.Status, o.ClinicalIndication, o.OrderedAtUtc, o.FacilityId })
            .ToListAsync(cancellationToken);
        return Completed(items, "doctor_diagnostic_orders", $"Có {items.Count} chỉ định liên quan.");
    }

    private async Task<AiToolExecutionResult> GetTechnicianWorklistAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.DiagnosticTechnician), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var items = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.FacilityId.HasValue && facilities.Contains(o.FacilityId.Value) && (o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress))
            .OrderBy(o => o.OrderedAtUtc).Take(100)
            .Select(o => new { o.Id, o.OrderCode, o.PatientId, o.Status, o.ClinicalIndication, o.OrderedAtUtc, o.PerformingDepartmentId })
            .ToListAsync(cancellationToken);
        return Completed(items, "technician_worklist", $"Có {items.Count} chỉ định đang chờ xử lý.");
    }

    private async Task<AiToolExecutionResult> GetPharmacyQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var items = await _db.Prescriptions.AsNoTracking()
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) && p.PatientVisit != null && facilities.Contains(p.PatientVisit.FacilityId))
            .OrderBy(p => p.CreatedAt).Take(100)
            .Select(p => new { p.Id, p.PatientId, patientName = p.Patient!.FullName, p.Status, p.CreatedAt, p.PatientVisitId })
            .ToListAsync(cancellationToken);
        return Completed(items, "pharmacist_prescription_queue", $"Có {items.Count} đơn thuốc trong hàng đợi.");
    }

    private async Task<AiToolExecutionResult> GetInventoryAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var medicines = await _db.Medicines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Name).Take(200)
            .Select(m => new { m.Id, m.Code, m.Name, m.Unit, m.StockQuantity, reorderLevel = m.ReorderLevel }).ToListAsync(cancellationToken);
        return Completed(medicines, "pharmacy_inventory", "Tồn kho được lấy từ danh mục thuốc hiện tại.");
    }

    private async Task<AiToolExecutionResult> GetAdminMetricsAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var today = _clock.VietnamToday;
        var metrics = new
        {
            appointmentsToday = await _db.Appointments.CountAsync(a => a.AppointmentDate == today, cancellationToken),
            activeVisits = await _db.PatientVisits.CountAsync(v => v.VisitDate == today && v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.Completed, cancellationToken),
            openDiagnosticOrders = await _db.DiagnosticOrders.CountAsync(o => o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress, cancellationToken),
            issuedPrescriptions = await _db.Prescriptions.CountAsync(p => p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase, cancellationToken)
        };
        return Completed(metrics, "admin_dashboard_metrics", "Chỉ số tổng hợp không chứa hồ sơ lâm sàng.");
    }

    private async Task<AiToolExecutionResult> GetAiHealthAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var metrics = new
        {
            pendingActions = await _db.AiPendingToolActions.CountAsync(a => a.State == AiPendingToolActionState.PendingConfirmation, cancellationToken),
            auditEvents = await _db.AiAuditLogs.CountAsync(cancellationToken)
        };
        return Completed(metrics, "admin_ai_health", "Chỉ số AI đã được tổng hợp, không trả secret hay nội dung prompt thô.");
    }

    private static bool TryGetLong(string json, string name, out long value)
    {
        value = 0;
        try { using var document = JsonDocument.Parse(json); return document.RootElement.TryGetProperty(name, out var property) && property.TryGetInt64(out value) && value > 0; }
        catch (JsonException) { return false; }
    }

    private static AiToolExecutionResult ScopeDenied() => AiToolExecutionResult.Failed("FACILITY_SCOPE_REQUIRED", "Không xác định được phạm vi cơ sở được phân quyền; dữ liệu không được trả về.");
    private static AiToolExecutionResult Completed(object data, string type, string displayText) => new()
    {
        Status = "completed", ResultType = type, Data = data, DisplayText = displayText,
        DataSources = new[] { new AiToolDataSource("ClinicCare domain database", "database") }
    };
}
