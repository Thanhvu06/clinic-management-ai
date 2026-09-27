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
            if (invocation.ToolName.Equals("clinic.search_knowledge", StringComparison.OrdinalIgnoreCase) &&
                (!document.RootElement.TryGetProperty("query", out var query) || query.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(query.GetString())))
                return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Cần nội dung cần tra cứu trong kho kiến thức phòng khám.");
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
            "clinic.search_knowledge" => SearchKnowledgeAsync(invocation.ArgumentsJson, cancellationToken),
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

    private async Task<AiToolExecutionResult> SearchKnowledgeAsync(string json, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out var queryValue) || queryValue.ValueKind != JsonValueKind.String)
            return AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần nội dung cần tra cứu trong kho kiến thức phòng khám.");

        var term = queryValue.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(term))
            return AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần nội dung cần tra cứu trong kho kiến thức phòng khám.");
        term = term.Length > 160 ? term[..160] : term;
        term = term.Replace("%", string.Empty).Replace("_", string.Empty).Replace("[", string.Empty);
        if (string.IsNullOrWhiteSpace(term))
            return AiToolExecutionResult.Failed("INVALID_TOOL_ARGUMENTS", "Nội dung tra cứu không hợp lệ.");
        var pattern = $"%{term}%";
        var results = new List<KnowledgeHit>();

        results.AddRange(await _db.Specialties.AsNoTracking()
            .Where(x => x.IsActive && (EF.Functions.Like(x.Name, pattern) || EF.Functions.Like(x.SpecialtyCode, pattern) || (x.Description != null && EF.Functions.Like(x.Description, pattern))))
            .OrderBy(x => x.Name).Take(20)
            .Select(x => new KnowledgeHit("specialty", x.Id.ToString(), x.Name, x.Description, x.ConsultationFee, null, null, null))
            .ToListAsync(cancellationToken));

        results.AddRange(await _db.DiagnosticServices.AsNoTracking()
            .Where(x => x.IsActive && (EF.Functions.Like(x.Name, pattern) || EF.Functions.Like(x.Code, pattern) || (x.PreparationInstructions != null && EF.Functions.Like(x.PreparationInstructions, pattern))))
            .OrderBy(x => x.Name).Take(20)
            .Select(x => new KnowledgeHit("diagnostic_service", x.Id.ToString(), x.Name, x.PreparationInstructions, x.Price, x.Code, null, null))
            .ToListAsync(cancellationToken));

        results.AddRange(await _db.Facilities.AsNoTracking()
            .Where(x => x.IsActive && (EF.Functions.Like(x.Name, pattern) || EF.Functions.Like(x.Code, pattern) || EF.Functions.Like(x.Address, pattern) || EF.Functions.Like(x.City, pattern)))
            .OrderBy(x => x.Name).Take(20)
            .Select(x => new KnowledgeHit("facility", x.Id.ToString(), x.Name, x.Description, null, x.Code, x.Address, x.Phone))
            .ToListAsync(cancellationToken));

        results.AddRange(await _db.ClinicLocations.AsNoTracking()
            .Where(x => x.IsActive && (EF.Functions.Like(x.Name, pattern) || EF.Functions.Like(x.Code, pattern) || EF.Functions.Like(x.Address, pattern) || EF.Functions.Like(x.City, pattern) || (x.Description != null && EF.Functions.Like(x.Description, pattern))))
            .OrderBy(x => x.Name).Take(20)
            .Select(x => new KnowledgeHit("clinic_location", x.Id.ToString(), x.Name, x.Description, null, x.Code, x.Address, x.Phone, x.OpeningHours))
            .ToListAsync(cancellationToken));

        if (results.Count == 0)
        {
            // General clinic questions (opening hours, holidays and pre-exam guidance)
            // are still served from the approved clinic index when the query is not
            // a literal entity name. No patient or staff record is included here.
            results.AddRange(await _db.Specialties.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Take(5)
                .Select(x => new KnowledgeHit("specialty", x.Id.ToString(), x.Name, x.Description, x.ConsultationFee, null, null, null)).ToListAsync(cancellationToken));
            results.AddRange(await _db.DiagnosticServices.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Take(5)
                .Select(x => new KnowledgeHit("diagnostic_service", x.Id.ToString(), x.Name, x.PreparationInstructions, x.Price, x.Code, null, null)).ToListAsync(cancellationToken));
            results.AddRange(await _db.Facilities.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Take(5)
                .Select(x => new KnowledgeHit("facility", x.Id.ToString(), x.Name, x.Description, null, x.Code, x.Address, x.Phone)).ToListAsync(cancellationToken));
            results.AddRange(await _db.ClinicLocations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Take(5)
                .Select(x => new KnowledgeHit("clinic_location", x.Id.ToString(), x.Name, x.Description, null, x.Code, x.Address, x.Phone, x.OpeningHours)).ToListAsync(cancellationToken));
        }

        var limited = results.Take(20).Select(x => new
        {
            sourceType = x.SourceType,
            sourceId = x.SourceId,
            title = x.Title,
            snippet = x.Snippet,
            code = x.Code,
            address = x.Address,
            phone = x.Phone,
            openingHours = x.OpeningHours,
            publishedPrice = x.PublishedPrice,
            updatedAtUtc = DateTime.UtcNow
        }).ToArray();
        return new AiToolExecutionResult
        {
            Status = "completed",
            ResultType = "clinic_knowledge",
            DisplayText = limited.Length == 0 ? "Không tìm thấy nội dung phù hợp trong kho kiến thức đã được phê duyệt." : $"Đã tìm thấy {limited.Length} mục từ kho kiến thức phòng khám.",
            Data = limited,
            DataSources = new[] { new AiToolDataSource("clinic_knowledge_allowlist", "approved_database") }
        };
    }

    private sealed record KnowledgeHit(
        string SourceType,
        string SourceId,
        string Title,
        string? Snippet,
        decimal? PublishedPrice,
        string? Code,
        string? Address,
        string? Phone,
        string? OpeningHours = null);

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
            .Where(a => a.AppointmentDate == today && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .OrderBy(a => a.StartTime).Take(100)
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber, doctorName = _db.Users.Where(u => u.Id == a.Doctor.UserId).Select(u => u.FullName).FirstOrDefault() ?? "Bác sĩ", specialtyId = a.SpecialtyId })
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
            .Where(a => a.AppointmentCode == code && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.StartTime, a.EndTime, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber })
            .SingleOrDefaultAsync(cancellationToken);
        return appointment is null ? AiToolExecutionResult.Failed("NOT_FOUND", "Không tìm thấy lịch hẹn trong phạm vi cơ sở được phân quyền.") : Completed(appointment, "appointment_lookup", "Đã tra cứu lịch hẹn từ hệ thống.");
    }

    private async Task<AiToolExecutionResult> GetDoctorQueueAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var doctorId = await _db.Doctors.AsNoTracking().Where(d => d.UserId == context.ActorId).Select(d => (long?)d.Id).SingleOrDefaultAsync(cancellationToken);
        if (!doctorId.HasValue) return ScopeDenied();
        var items = await _db.PatientVisits.AsNoTracking()
            .Where(v => v.AssignedDoctorId == doctorId && facilities.Contains(v.FacilityId) && v.VisitDate == _clock.VietnamToday && v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.Completed)
            .OrderBy(v => v.QueueNumber).Take(100)
            .Select(v => new { v.Id, v.VisitCode, v.QueueNumber, v.Status, patientName = v.Patient.FullName, v.Patient.MedicalRecordNumber, v.ChiefComplaint, v.AppointmentId })
            .ToListAsync(cancellationToken);
        return Completed(items, "doctor_queue", $"Hàng đợi của bạn có {items.Count} lượt.");
    }

    private async Task<AiToolExecutionResult> GetDoctorPatientSummaryAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        if (!TryGetLong(json, "appointmentId", out var appointmentId)) return AiToolExecutionResult.Failed("MISSING_TOOL_ARGUMENT", "Cần appointmentId hợp lệ.");
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var summary = await _db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId && a.Doctor.UserId == context.ActorId && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .Select(a => new { a.Id, a.AppointmentCode, a.AppointmentDate, a.Status, patientName = a.Patient.FullName, a.Patient.MedicalRecordNumber, a.Reason, a.SpecialtyId })
            .SingleOrDefaultAsync(cancellationToken);
        return summary is null ? AiToolExecutionResult.Failed("NOT_FOUND", "Ca khám không thuộc bác sĩ hiện tại.") : Completed(summary, "doctor_patient_summary", "Tóm tắt được giới hạn trong ca khám được phân công.");
    }

    private async Task<AiToolExecutionResult> GetDoctorOrdersAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Doctor), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var items = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.OrderingDoctor.UserId == context.ActorId && o.FacilityId.HasValue && facilities.Contains(o.FacilityId.Value) && o.Status != DiagnosticOrderStatus.Cancelled)
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
            .Where(p => (p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.ReservedForPurchase) &&
                        ((p.PatientVisit != null && facilities.Contains(p.PatientVisit.FacilityId)) ||
                         (p.PatientVisit == null && p.Appointment != null && p.Appointment.FacilityId.HasValue && facilities.Contains(p.Appointment.FacilityId.Value))))
            .OrderBy(p => p.CreatedAt).Take(100)
            .Select(p => new { p.Id, p.PatientId, patientName = p.Patient!.FullName, p.Status, p.CreatedAt, p.PatientVisitId })
            .ToListAsync(cancellationToken);
        return Completed(items, "pharmacist_prescription_queue", $"Có {items.Count} đơn thuốc trong hàng đợi.");
    }

    private async Task<AiToolExecutionResult> GetInventoryAsync(AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Pharmacist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        var medicines = await _db.Medicines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Name).Take(200)
            .Select(m => new { m.Id, m.Code, m.Name, m.Unit, m.StockQuantity, reorderLevel = m.ReorderLevel }).ToListAsync(cancellationToken);
        return Completed(medicines, "pharmacy_inventory", "Tồn kho toàn hệ thống được lấy từ danh mục thuốc hiện tại; mô hình dữ liệu chưa phân tách tồn kho theo cơ sở.");
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
