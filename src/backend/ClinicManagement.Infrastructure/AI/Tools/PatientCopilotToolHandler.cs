using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Organization.Interfaces;
using ClinicManagement.Application.Specialties.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed class PatientCopilotToolHandler : IAiToolHandler
{
    private static readonly IReadOnlySet<AiActorRole> PatientOnly = new HashSet<AiActorRole> { AiActorRole.Patient };

    private readonly ISpecialtyService _specialties;
    private readonly IDoctorService _doctors;
    private readonly IAppointmentAvailabilityPolicy _availability;
    private readonly IAppointmentService _appointments;
    private readonly IChangeRequestService _changeRequests;
    private readonly IOrganizationService _organization;
    private readonly IDiagnosticWorkflowService _diagnostics;
    private readonly ICurrentUserService _currentUser;
    private readonly AppDbContext _db;
    private readonly IAiBookingConfirmationStore _bookingConfirmations;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PatientCopilotToolHandler(
        ISpecialtyService specialties,
        IDoctorService doctors,
        IAppointmentAvailabilityPolicy availability,
        IAppointmentService appointments,
        IChangeRequestService changeRequests,
        IOrganizationService organization,
        IDiagnosticWorkflowService diagnostics,
        ICurrentUserService currentUser,
        AppDbContext db,
        IAiBookingConfirmationStore bookingConfirmations,
        IDateTimeProvider dateTimeProvider)
    {
        _specialties = specialties;
        _doctors = doctors;
        _availability = availability;
        _appointments = appointments;
        _changeRequests = changeRequests;
        _organization = organization;
        _diagnostics = diagnostics;
        _currentUser = currentUser;
        _db = db;
        _bookingConfirmations = bookingConfirmations;
        _dateTimeProvider = dateTimeProvider;
    }

    public AiToolDefinition Definition { get; } = new() { Name = "patient.copilot.dispatch", Version = "1.0" };

    public AiToolArgumentValidationResult ValidateArguments(AiToolInvocation invocation, AiToolExecutionContext context)
    {
        var name = invocation.ToolName.Trim().ToLowerInvariant();
        var args = ParseDocument(invocation.ArgumentsJson, out var parseError);
        if (parseError != null)
            return parseError;

        var schemas = new Dictionary<string, (string[] Allowed, string[] Required)>(StringComparer.OrdinalIgnoreCase)
        {
            ["clinic.search_specialties"] = (new[] { "query" }, Array.Empty<string>()),
            ["clinic.search_doctors"] = (new[] { "query", "specialtyId" }, Array.Empty<string>()),
            ["clinic.get_available_slots"] = (new[] { "doctorId", "specialtyId", "fromDate", "toDate", "limit" }, Array.Empty<string>()),
            ["clinic.get_facilities"] = (Array.Empty<string>(), Array.Empty<string>()),
            ["clinic.get_pricing"] = (Array.Empty<string>(), Array.Empty<string>()),
            ["patient.get_my_appointments"] = (new[] { "status", "page", "pageSize" }, Array.Empty<string>()),
            ["patient.get_appointment_detail"] = (new[] { "appointmentId" }, new[] { "appointmentId" }),
            ["patient.prepare_booking"] = (new[] { "specialtyId", "doctorId", "slotId", "reason" }, new[] { "specialtyId", "doctorId", "slotId" }),
            ["patient.prepare_cancel_appointment"] = (new[] { "appointmentId", "reason" }, new[] { "appointmentId", "reason" }),
            ["patient.prepare_reschedule_appointment"] = (new[] { "appointmentId", "requestedSlotId", "reason" }, new[] { "appointmentId", "requestedSlotId", "reason" }),
            ["patient.execute_confirmed_action"] = (new[] { "actionId", "confirm", "concurrencyToken" }, new[] { "actionId", "confirm" })
        };

        if (!schemas.TryGetValue(name, out var schema))
            return AiToolArgumentValidationResult.Invalid("UNKNOWN_TOOL", "Công cụ AI không được hỗ trợ.");

        var allowed = schema.Allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in args.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                return AiToolArgumentValidationResult.Invalid("UNKNOWN_TOOL_ARGUMENT", $"Tham số '{property.Name}' không được phép.");
            if (property.Name.Equals("userId", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("actorId", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("channel", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("facilityAuthorization", StringComparison.OrdinalIgnoreCase))
                return AiToolArgumentValidationResult.Invalid("FORBIDDEN_TOOL_ARGUMENT", "Tham số quyền hạn chỉ được xác định phía server.");
        }

        foreach (var required in schema.Required)
        {
            if (!args.TryGetProperty(required, out var requiredValue) || requiredValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", $"Thiếu tham số bắt buộc '{required}'.");
        }

        var valueError = ValidateArgumentValues(name, args, context.InvocationChannel);
        return valueError ?? AiToolArgumentValidationResult.Valid();
    }

    public Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (invocation.ToolName.Equals("patient.execute_confirmed_action", StringComparison.OrdinalIgnoreCase) &&
            context.InvocationChannel != AiToolInvocationChannel.DirectHumanConfirmation)
            return Task.FromResult(AiToolExecutionResult.Failed("PLANNER_WRITE_EXECUTION_FORBIDDEN", "Planner không được thực hiện thao tác ghi đã xác nhận."));

        return invocation.ToolName.Trim().ToLowerInvariant() switch
        {
            "clinic.search_specialties" => SearchSpecialtiesAsync(invocation, cancellationToken),
            "clinic.search_doctors" => SearchDoctorsAsync(invocation, cancellationToken),
            "clinic.get_available_slots" => GetAvailableSlotsAsync(invocation, context, cancellationToken),
            "clinic.get_facilities" => GetFacilitiesAsync(cancellationToken),
            "clinic.get_pricing" => GetPricingAsync(cancellationToken),
            "patient.get_my_appointments" => GetMyAppointmentsAsync(invocation, cancellationToken),
            "patient.get_appointment_detail" => GetAppointmentDetailAsync(invocation, cancellationToken),
            "patient.prepare_booking" => PrepareBookingAsync(invocation, context, cancellationToken),
            "patient.prepare_cancel_appointment" => PrepareChangeAsync("cancel", invocation, context, cancellationToken),
            "patient.prepare_reschedule_appointment" => PrepareChangeAsync("reschedule", invocation, context, cancellationToken),
            "patient.execute_confirmed_action" => ExecuteConfirmedActionAsync(invocation, context, cancellationToken),
            _ => Task.FromResult(AiToolExecutionResult.Failed("UNKNOWN_TOOL", "Công cụ AI không được hỗ trợ."))
        };
    }

    private static JsonElement ParseDocument(string? json, out AiToolArgumentValidationResult? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số công cụ phải là JSON object.");
                return default;
            }

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            error = AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số công cụ không hợp lệ.");
            return default;
        }
    }

    private static AiToolArgumentValidationResult? ValidateArgumentValues(string name, JsonElement args, AiToolInvocationChannel channel)
    {
        foreach (var property in args.EnumerateObject())
        {
            if (property.Name.Equals("query", StringComparison.OrdinalIgnoreCase) &&
                (property.Value.ValueKind != JsonValueKind.String || (property.Value.GetString()?.Trim().Length ?? 0) > 100))
                return AiToolArgumentValidationResult.Invalid("INVALID_QUERY", "Query phải là chuỗi tối đa 100 ký tự.");
            if (property.Name.Equals("status", StringComparison.OrdinalIgnoreCase) &&
                (property.Value.ValueKind != JsonValueKind.String || (property.Value.GetString()?.Trim().Length ?? 0) > 40))
                return AiToolArgumentValidationResult.Invalid("INVALID_STATUS", "Trạng thái phải là chuỗi tối đa 40 ký tự.");
            if (property.Name.Equals("reason", StringComparison.OrdinalIgnoreCase))
            {
                var reason = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(reason) || reason.Length < 5 || reason.Length > 500)
                    return AiToolArgumentValidationResult.Invalid("INVALID_REASON", "Lý do phải dài từ 5 đến 500 ký tự.");
            }
            if (property.Name.Equals("actionId", StringComparison.OrdinalIgnoreCase) &&
                (property.Value.ValueKind != JsonValueKind.String || !Guid.TryParse(property.Value.GetString(), out _)))
                return AiToolArgumentValidationResult.Invalid("INVALID_ACTION_ID", "Mã thao tác không hợp lệ.");
            if (property.Name.Equals("concurrencyToken", StringComparison.OrdinalIgnoreCase) &&
                (property.Value.ValueKind != JsonValueKind.String || (property.Value.GetString()?.Length ?? 0) > 256))
                return AiToolArgumentValidationResult.Invalid("INVALID_CONCURRENCY_TOKEN", "Mã đồng bộ không hợp lệ.");
            if (property.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                property.Name is not "actionId" &&
                (!property.Value.TryGetInt64(out var id) || id <= 0))
                return AiToolArgumentValidationResult.Invalid("INVALID_IDENTIFIER", "Mã định danh phải là số dương.");
            if (property.Name.Equals("limit", StringComparison.OrdinalIgnoreCase) &&
                (!property.Value.TryGetInt32(out var limit) || limit is < 1 or > 100))
                return AiToolArgumentValidationResult.Invalid("INVALID_LIMIT", "Giới hạn phải từ 1 đến 100.");
            if (property.Name.Equals("page", StringComparison.OrdinalIgnoreCase) &&
                (!property.Value.TryGetInt32(out var page) || page is < 1 or > 100))
                return AiToolArgumentValidationResult.Invalid("INVALID_PAGE", "Trang phải từ 1 đến 100.");
            if (property.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase) &&
                (!property.Value.TryGetInt32(out var pageSize) || pageSize is < 1 or > 50))
                return AiToolArgumentValidationResult.Invalid("INVALID_PAGE_SIZE", "Kích thước trang phải từ 1 đến 50.");
            if (property.Name is "fromDate" or "toDate")
            {
                if (property.Value.ValueKind != JsonValueKind.String || !DateOnly.TryParse(property.Value.GetString(), out _))
                    return AiToolArgumentValidationResult.Invalid("INVALID_DATE", "Ngày phải có định dạng hợp lệ.");
            }
            if (property.Name.Equals("confirm", StringComparison.OrdinalIgnoreCase) &&
                (channel != AiToolInvocationChannel.DirectHumanConfirmation || property.Value.ValueKind != JsonValueKind.True))
                return AiToolArgumentValidationResult.Invalid("CONFIRMATION_REQUIRED", "Chỉ endpoint xác nhận trực tiếp mới được phép confirm.");
        }

        if (args.TryGetProperty("fromDate", out var fromElement) && args.TryGetProperty("toDate", out var toElement) &&
            DateOnly.TryParse(fromElement.GetString(), out var from) && DateOnly.TryParse(toElement.GetString(), out var to) &&
            (to < from || to.DayNumber - from.DayNumber > 31))
            return AiToolArgumentValidationResult.Invalid("INVALID_DATE_RANGE", "Khoảng ngày tìm slot không hợp lệ.");

        return null;
    }

    private async Task<AiToolExecutionResult> SearchSpecialtiesAsync(AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var query = GetString(args, "query");
        var items = await _specialties.GetSpecialtiesAsync();
        if (!string.IsNullOrWhiteSpace(query))
            items = items.Where(x => Contains(x.SpecialtyName, query) || Contains(x.SpecialtyCode, query) || Contains(x.Description, query)).ToList();
        var data = items.Take(20).Select(x => new { id = x.Id, code = x.SpecialtyCode, name = x.SpecialtyName, description = x.Description }).ToList();
        return Completed(data, "specialties", data.Count == 0 ? "Không tìm thấy chuyên khoa phù hợp." : $"Tìm thấy {data.Count} chuyên khoa phù hợp.");
    }

    private async Task<AiToolExecutionResult> SearchDoctorsAsync(AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var query = GetString(args, "query");
        var specialtyId = GetLong(args, "specialtyId");
        var items = await _doctors.GetAllActiveDoctorsAsync();
        if (specialtyId.HasValue) items = items.Where(x => x.SpecialtyId == specialtyId).ToList();
        if (!string.IsNullOrWhiteSpace(query)) items = items.Where(x => Contains(x.FullName, query) || Contains(x.Description, query)).ToList();
        var data = items.Take(20).Select(x => new { id = x.Id, name = x.FullName, academicTitle = x.AcademicTitle, specialtyId = x.SpecialtyId, specialtyName = x.SpecialtyName }).ToList();
        return Completed(data, "doctors", data.Count == 0 ? "Không tìm thấy bác sĩ phù hợp." : $"Tìm thấy {data.Count} bác sĩ phù hợp.");
    }

    private async Task<AiToolExecutionResult> GetAvailableSlotsAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var from = GetDate(args, "fromDate") ?? DateOnly.FromDateTime(_dateTimeProvider.UtcNow);
        var to = GetDate(args, "toDate") ?? from.AddDays(14);
        if (to < from || to.DayNumber - from.DayNumber > 31) return AiToolExecutionResult.Failed("INVALID_DATE_RANGE", "Khoảng ngày tìm slot không hợp lệ.");
        var patientId = await ResolvePatientIdAsync(context.ActorId, cancellationToken);
        var slots = await _availability.GetAvailableSlotsAsync(new BatchSlotAvailabilityRequest
        {
            DoctorId = GetLong(args, "doctorId"),
            SpecialtyId = GetLong(args, "specialtyId"),
            FromDate = from,
            ToDate = to,
            PatientId = patientId,
            CheckAiEnabledSpecialty = true,
            Limit = Math.Min(GetInt(args, "limit") ?? 50, 100)
        }, cancellationToken);
        return Completed(slots.Take(50).ToList(), "available_slots", slots.Count == 0 ? "Hiện không có khung giờ trống phù hợp." : $"Tìm thấy {slots.Count} khung giờ trống.");
    }

    private async Task<AiToolExecutionResult> GetFacilitiesAsync(CancellationToken cancellationToken)
    {
        var items = await _organization.GetFacilitiesAsync(false, cancellationToken);
        var data = items.Take(20).Select(x => new { id = x.Id, code = x.Code, name = x.Name, address = x.Address, city = x.City, phone = x.Phone, description = x.Description }).ToList();
        return Completed(data, "facilities", data.Count == 0 ? "Hiện chưa có cơ sở phù hợp." : $"Tìm thấy {data.Count} cơ sở đang hoạt động.");
    }

    private async Task<AiToolExecutionResult> GetPricingAsync(CancellationToken cancellationToken)
    {
        var specialties = await _specialties.GetSpecialtiesAsync();
        var diagnostics = await _diagnostics.GetAllDiagnosticServicesPricingAsync(cancellationToken);
        var data = new
        {
            consultation = specialties.Select(x => new { specialtyId = x.Id, specialty = x.SpecialtyName, consultationFee = x.ConsultationFee, currency = "VND" }).ToList(),
            diagnostics = diagnostics.Where(x => x.IsActive).Take(50).Select(x => new { serviceId = x.Id, code = x.Code, name = x.Name, category = x.Category, price = x.Price, currency = "VND" }).ToList()
        };
        return Completed(data, "pricing_catalog", "Bảng giá dưới đây được lấy từ dữ liệu hiện hành của ClinicCare.");
    }

    private async Task<AiToolExecutionResult> GetMyAppointmentsAsync(AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var page = Math.Clamp(GetInt(args, "page") ?? 1, 1, 100);
        var pageSize = Math.Clamp(GetInt(args, "pageSize") ?? 20, 1, 50);
        var result = await _appointments.GetPatientAppointmentsAsync(GetString(args, "status"), page, pageSize);
        return Completed(result, "appointments", result.Items.Count == 0 ? "Bạn hiện chưa có lịch hẹn phù hợp." : $"Tìm thấy {result.TotalItems} lịch hẹn của bạn.");
    }

    private async Task<AiToolExecutionResult> GetAppointmentDetailAsync(AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var appointmentId = GetLong(Parse(invocation.ArgumentsJson), "appointmentId");
        if (!appointmentId.HasValue || appointmentId <= 0) return AiToolExecutionResult.Failed("INVALID_APPOINTMENT_ID", "Mã lịch hẹn không hợp lệ.");
        var result = await _appointments.GetPatientAppointmentByIdAsync(appointmentId.Value);
        return Completed(result, "appointment_detail", "Đây là chi tiết lịch hẹn được lấy từ hệ thống ClinicCare.");
    }

    private async Task<AiToolExecutionResult> PrepareBookingAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var specialtyId = GetLong(args, "specialtyId");
        var doctorId = GetLong(args, "doctorId");
        var slotId = GetLong(args, "slotId");
        if (!specialtyId.HasValue || !doctorId.HasValue || !slotId.HasValue) return AiToolExecutionResult.Failed("MISSING_BOOKING_FIELDS", "Cần chuyên khoa, bác sĩ và khung giờ để chuẩn bị đặt lịch.");
        var patientId = await ResolvePatientIdAsync(context.ActorId, cancellationToken);
        var availability = await _availability.EvaluateSlotAvailabilityAsync(new SlotAvailabilityRequest
        {
            SlotId = slotId.Value, DoctorId = doctorId.Value, SpecialtyId = specialtyId.Value, PatientId = patientId, CheckAiEnabledSpecialty = true
        }, cancellationToken);
        if (!availability.IsAvailable) return AiToolExecutionResult.Failed(availability.ReasonCode ?? "SLOT_UNAVAILABLE", availability.FailureReason ?? "Khung giờ không còn khả dụng.");

        // Phase 1.1 remains the source of truth for booking confirmations. The gateway
        // returns a verified preview and never creates an appointment on prepare.
        return new AiToolExecutionResult
        {
            Status = "pending_confirmation",
            RequiresConfirmation = true,
            ResultType = "booking_preview",
            DisplayText = "Khung giờ còn khả dụng. Vui lòng tiếp tục xác nhận trong luồng đặt lịch.",
            Data = new
            {
                operation = "booking",
                specialtyId,
                doctorId,
                slotId,
                slotDate = availability.SlotDate,
                startTime = availability.StartTime,
                endTime = availability.EndTime,
                nextStep = "Use the existing booking confirmation flow to issue and consume the server confirmation."
            },
            DataSources = new[] { new AiToolDataSource("availability_policy", "service") }
        };
    }

    private async Task<AiToolExecutionResult> PrepareChangeAsync(string operation, AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.ActorId.HasValue) return AiToolExecutionResult.Failed("AUTHENTICATION_REQUIRED", "Bạn cần đăng nhập để thay đổi lịch hẹn.");
        if (!IsValidSessionId(context.SessionId))
            return AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần phiên hội thoại hợp lệ để chuẩn bị thao tác.", true);
        var args = Parse(invocation.ArgumentsJson);
        var appointmentId = GetLong(args, "appointmentId");
        if (!appointmentId.HasValue || appointmentId <= 0) return AiToolExecutionResult.Failed("INVALID_APPOINTMENT_ID", "Mã lịch hẹn không hợp lệ.");
        var appointment = await _appointments.GetPatientAppointmentByIdAsync(appointmentId.Value);
        long? requestedSlotId = null;
        if (operation == "reschedule")
        {
            requestedSlotId = GetLong(args, "requestedSlotId");
            if (!requestedSlotId.HasValue || requestedSlotId <= 0) return AiToolExecutionResult.Failed("MISSING_REQUESTED_SLOT", "Cần chọn khung giờ mới để đổi lịch.");
            var patientId = await ResolvePatientIdAsync(context.ActorId, cancellationToken);
            var availability = await _availability.EvaluateSlotAvailabilityAsync(new SlotAvailabilityRequest
            {
                SlotId = requestedSlotId.Value, SpecialtyId = appointment.SpecialtyId, PatientId = patientId
            }, cancellationToken);
            if (!availability.IsAvailable) return AiToolExecutionResult.Failed(availability.ReasonCode ?? "SLOT_UNAVAILABLE", availability.FailureReason ?? "Khung giờ mới không còn khả dụng.");
        }

        var normalized = JsonSerializer.Serialize(new
        {
            appointmentId = appointment.Id,
            requestedSlotId,
            reasonHash = Hash(GetString(args, "reason"))
        });
        var now = _dateTimeProvider.UtcNow;
        var requestHash = Hash($"{operation}|{normalized}");
        var idempotencyHash = Hash(invocation.IdempotencyKey);

        // Terminalize expired actions before looking for an active action. This
        // keeps the filtered uniqueness invariant true after the TTL elapses.
        await _db.AiPendingToolActions
            .Where(x => x.UserId == context.ActorId.Value &&
                        x.ExpiresAtUtc <= now &&
                        (x.State == AiPendingToolActionState.PendingConfirmation ||
                         x.State == AiPendingToolActionState.Executing ||
                         x.State == AiPendingToolActionState.FailedRetryable))
            .ExecuteUpdateAsync(x => x
                .SetProperty(a => a.State, AiPendingToolActionState.Expired)
                .SetProperty(a => a.ExecutionLeaseId, (Guid?)null)
                .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
        if (!string.IsNullOrWhiteSpace(invocation.IdempotencyKey))
        {
            var sameKey = await _db.AiPendingToolActions.FirstOrDefaultAsync(x =>
                x.UserId == context.ActorId.Value && x.IdempotencyKeyHash == idempotencyHash &&
                x.ExpiresAtUtc > now && x.State != AiPendingToolActionState.Cancelled && x.State != AiPendingToolActionState.Expired, cancellationToken);
            if (sameKey != null)
            {
                if (!string.Equals(sameKey.RequestHash, requestHash, StringComparison.Ordinal))
                    return AiToolExecutionResult.Failed("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", "Idempotency key đã được dùng cho thao tác khác.");
                return PendingResult(sameKey, appointment);
            }
        }
        var active = await _db.AiPendingToolActions.FirstOrDefaultAsync(x =>
            x.UserId == context.ActorId.Value &&
            x.ResourceId == appointment.Id.ToString() && x.ExpiresAtUtc > now &&
            (x.State == AiPendingToolActionState.PendingConfirmation || x.State == AiPendingToolActionState.Executing || x.State == AiPendingToolActionState.FailedRetryable), cancellationToken);
        if (active != null)
        {
            if (!string.Equals(active.RequestHash, requestHash, StringComparison.Ordinal)) return AiToolExecutionResult.Failed("ACTIVE_ACTION_EXISTS", "Đã có một thao tác thay đổi lịch đang chờ xác nhận.");
            return PendingResult(active, appointment);
        }

        var action = new AiPendingToolAction
        {
            ActionId = Guid.NewGuid(), UserId = context.ActorId.Value, SessionId = context.SessionId!.Trim(),
            ToolName = $"patient.prepare_{operation}_appointment", ToolVersion = "1.0",
            RequestHash = requestHash, ResourceType = "appointment", ResourceId = appointment.Id.ToString(),
            NormalizedArgumentsJson = JsonSerializer.Serialize(new { appointmentId = appointment.Id, requestedSlotId }),
            CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(12), State = AiPendingToolActionState.PendingConfirmation,
            IdempotencyKeyHash = string.IsNullOrWhiteSpace(invocation.IdempotencyKey) ? null : idempotencyHash
        };
        _db.AiPendingToolActions.Add(action);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var concurrent = await _db.AiPendingToolActions.FirstOrDefaultAsync(x =>
                x.UserId == context.ActorId.Value && x.ResourceId == appointment.Id.ToString() &&
                x.ExpiresAtUtc > now &&
                (x.State == AiPendingToolActionState.PendingConfirmation || x.State == AiPendingToolActionState.Executing || x.State == AiPendingToolActionState.FailedRetryable), cancellationToken);
            if (concurrent != null && string.Equals(concurrent.RequestHash, requestHash, StringComparison.Ordinal))
                return PendingResult(concurrent, appointment);
            return AiToolExecutionResult.Failed("ACTIVE_ACTION_EXISTS", "Đã có một thao tác thay đổi lịch đang chờ xác nhận.", true);
        }
        return PendingResult(action, appointment);
    }

    private async Task<AiToolExecutionResult> ExecuteConfirmedActionAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.InvocationChannel != AiToolInvocationChannel.DirectHumanConfirmation)
            return AiToolExecutionResult.Failed("PLANNER_WRITE_EXECUTION_FORBIDDEN", "Planner không được thực hiện thao tác ghi đã xác nhận.");
        if (!context.ActorId.HasValue) return AiToolExecutionResult.Failed("AUTHENTICATION_REQUIRED", "Bạn cần đăng nhập để xác nhận thao tác.");
        var args = Parse(invocation.ArgumentsJson);
        var actionIdText = GetString(args, "actionId");
        if (!Guid.TryParse(actionIdText, out var actionId)) return AiToolExecutionResult.Failed("INVALID_ACTION_ID", "Mã thao tác không hợp lệ.");
        if (!GetBool(args, "confirm")) return AiToolExecutionResult.Failed("CONFIRMATION_REQUIRED", "Cần xác nhận rõ ràng trước khi thực hiện.");
        if (!IsValidSessionId(context.SessionId))
            return AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần phiên hội thoại hợp lệ để xác nhận thao tác.");

        var action = await _db.AiPendingToolActions.FirstOrDefaultAsync(x => x.ActionId == actionId, cancellationToken);
        if (action == null || action.UserId != context.ActorId.Value) return AiToolExecutionResult.Failed("ACTION_NOT_FOUND", "Thao tác không tồn tại hoặc không thuộc tài khoản này.");
        if (!string.Equals(action.SessionId, context.SessionId, StringComparison.Ordinal)) return AiToolExecutionResult.Failed("SESSION_MISMATCH", "Thao tác thuộc một phiên hội thoại khác.");
        if (action.State == AiPendingToolActionState.Completed || action.ExecutedAtUtc.HasValue)
            return Completed(new { actionId, status = "already_completed", reference = action.ExecutionResultReference }, "pending_action", "Thao tác này đã được hoàn tất trước đó.");

        var now = _dateTimeProvider.UtcNow;
        if (action.CancelledAtUtc.HasValue || action.State is AiPendingToolActionState.Cancelled or AiPendingToolActionState.Expired || action.ExpiresAtUtc <= now)
        {
            await _db.AiPendingToolActions.Where(x => x.ActionId == actionId && x.UserId == context.ActorId.Value)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(a => a.State, AiPendingToolActionState.Expired)
                    .SetProperty(a => a.ExecutionLeaseId, (Guid?)null)
                    .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
            return AiToolExecutionResult.Failed("ACTION_EXPIRED", "Thao tác đã hết hạn hoặc đã bị hủy.");
        }

        var suppliedToken = GetString(args, "concurrencyToken");
        if (!string.IsNullOrWhiteSpace(suppliedToken) && action.RowVersion.Length > 0)
        {
            try
            {
                if (!action.RowVersion.SequenceEqual(Convert.FromBase64String(suppliedToken)))
                    return AiToolExecutionResult.Failed("CONCURRENCY_CONFLICT", "Thao tác đã thay đổi, vui lòng tạo lại yêu cầu xác nhận.", true);
            }
            catch (FormatException)
            {
                return AiToolExecutionResult.Failed("INVALID_CONCURRENCY_TOKEN", "Mã đồng bộ không hợp lệ.");
            }
        }

        if (action.State == AiPendingToolActionState.Executing && action.ExecutionLeaseExpiresAtUtc > now)
            return AiToolExecutionResult.Failed("ACTION_IN_PROGRESS", "Thao tác này đang được xử lý, vui lòng chờ kết quả.", true);

        var leaseId = Guid.NewGuid();
        var claimed = await _db.AiPendingToolActions
            .Where(x => x.ActionId == actionId && x.UserId == context.ActorId.Value && x.SessionId == context.SessionId &&
                        x.ExpiresAtUtc > now && x.CancelledAtUtc == null &&
                        (x.State == AiPendingToolActionState.PendingConfirmation ||
                         x.State == AiPendingToolActionState.FailedRetryable ||
                         (x.State == AiPendingToolActionState.Executing && x.ExecutionLeaseExpiresAtUtc <= now)))
            .ExecuteUpdateAsync(x => x
                .SetProperty(a => a.State, AiPendingToolActionState.Executing)
                .SetProperty(a => a.ExecutionLeaseId, leaseId)
                .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, now.AddMinutes(2))
                .SetProperty(a => a.ExecutionAttemptCount, a => a.ExecutionAttemptCount + 1)
                .SetProperty(a => a.ConfirmedAtUtc, a => a.ConfirmedAtUtc ?? now)
                .SetProperty(a => a.LastErrorCode, (string?)null), cancellationToken);
        if (claimed == 0)
            return AiToolExecutionResult.Failed("ACTION_IN_PROGRESS", "Thao tác này đang được xử lý, vui lòng thử lại sau.", true);

        action = await _db.AiPendingToolActions.AsNoTracking().FirstAsync(x => x.ActionId == actionId, cancellationToken);

        var stored = Parse(action.NormalizedArgumentsJson);
        var appointmentId = GetLong(stored, "appointmentId");
        if (!appointmentId.HasValue) return AiToolExecutionResult.Failed("INVALID_PENDING_ACTION", "Dữ liệu thao tác không hợp lệ.");
        var isReschedule = action.ToolName.Contains("reschedule", StringComparison.OrdinalIgnoreCase);
        try
        {
            // Revalidate through the canonical appointment/change-request services.
            await _appointments.GetPatientAppointmentByIdAsync(appointmentId.Value);
            var result = isReschedule
                ? await _changeRequests.CreateRescheduleRequestAsync(appointmentId.Value, new CreateRescheduleRequestDto { RequestedSlotId = GetLong(stored, "requestedSlotId") ?? 0, Reason = "Yêu cầu đổi lịch từ Patient Copilot" }, action.ActionId)
                : await _changeRequests.CreateCancellationRequestAsync(appointmentId.Value, new CreateCancellationRequestDto { Reason = "Yêu cầu hủy lịch từ Patient Copilot" }, action.ActionId);

            var completed = await _db.AiPendingToolActions
                .Where(x => x.ActionId == actionId && x.ExecutionLeaseId == leaseId && x.State == AiPendingToolActionState.Executing)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(a => a.State, AiPendingToolActionState.Completed)
                    .SetProperty(a => a.ExecutedAtUtc, _dateTimeProvider.UtcNow)
                    .SetProperty(a => a.ExecutionResultReference, result.Id.ToString())
                    .SetProperty(a => a.ExecutionLeaseId, (Guid?)null)
                    .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(a => a.LastErrorCode, (string?)null), cancellationToken);
            if (completed == 0)
                return AiToolExecutionResult.Failed("ACTION_IN_PROGRESS", "Thao tác đã được xử lý bởi một yêu cầu khác.", true);

            return Completed(new { actionId, changeRequestId = result.Id, operation = isReschedule ? "reschedule" : "cancel", appointmentId }, "change_request", isReschedule ? "Yêu cầu đổi lịch đã được tạo." : "Yêu cầu hủy lịch đã được tạo.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var errorCode = ex is ClinicManagement.Application.Common.Exceptions.ConflictException conflict
                ? conflict.ErrorCode
                : "ACTION_EXECUTION_FAILED";
            await _db.AiPendingToolActions
                .Where(x => x.ActionId == actionId && x.ExecutionLeaseId == leaseId && x.State == AiPendingToolActionState.Executing)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(a => a.State, AiPendingToolActionState.FailedRetryable)
                    .SetProperty(a => a.LastErrorCode, errorCode)
                    .SetProperty(a => a.ExecutionLeaseId, (Guid?)null)
                    .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
            return AiToolExecutionResult.Failed(errorCode, "Không thể hoàn tất thao tác lúc này; bạn có thể thử lại.", true);
        }
    }

    private async Task<long?> ResolvePatientIdAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty) return null;
        return await _db.Patients.AsNoTracking().Where(x => x.UserId == userId.Value).Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static AiToolExecutionResult PendingResult(AiPendingToolAction action, Application.Appointments.DTOs.AppointmentDto appointment) => new()
    {
        Status = "pending_confirmation", RequiresConfirmation = true, ActionId = action.ActionId.ToString(),
        ResultType = "pending_action",
        DisplayText = $"Thao tác trên lịch hẹn {appointment.AppointmentCode} đang chờ bạn xác nhận.",
        Data = new { actionId = action.ActionId, appointmentId = appointment.Id, appointmentCode = appointment.AppointmentCode, expiresAtUtc = action.ExpiresAtUtc, confirmation = "Xác nhận thao tác này", concurrencyToken = action.RowVersion.Length == 0 ? null : Convert.ToBase64String(action.RowVersion) },
        DataSources = new[] { new AiToolDataSource("appointment_service", "service"), new AiToolDataSource("pending_action_store", "database") }
    };

    private static AiToolExecutionResult Completed(object? data, string source, string? displayText = null) => new()
    {
        Status = "completed", Data = data, ResultType = source, DisplayText = displayText, DataSources = new[] { new AiToolDataSource(source, "service") }
    };

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json ?? "{}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch (JsonException) { return new(); }
    }
    private static string? GetString(Dictionary<string, JsonElement> args, string name) => args.TryGetValue(name, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString()?.Trim() : null;
    private static long? GetLong(Dictionary<string, JsonElement> args, string name) => args.TryGetValue(name, out var x) && x.TryGetInt64(out var v) ? v : null;
    private static int? GetInt(Dictionary<string, JsonElement> args, string name) => args.TryGetValue(name, out var x) && x.TryGetInt32(out var v) ? v : null;
    private static bool GetBool(Dictionary<string, JsonElement> args, string name) => args.TryGetValue(name, out var x) && x.ValueKind == JsonValueKind.True;
    private static DateOnly? GetDate(Dictionary<string, JsonElement> args, string name) => DateOnly.TryParse(GetString(args, name), out var date) ? date : null;
    private static bool Contains(string? text, string query) => !string.IsNullOrWhiteSpace(text) && text.Contains(query, StringComparison.OrdinalIgnoreCase);
    private static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    private static bool IsValidSessionId(string? sessionId) =>
        !string.IsNullOrWhiteSpace(sessionId) && sessionId.Length <= 128 && sessionId.StartsWith("sess_", StringComparison.Ordinal);

    public static IReadOnlyList<AiToolDefinition> Definitions() => new[]
    {
        Def("clinic.search_specialties", AiToolAccessMode.Public, AiToolRiskLevel.Low, AiToolConfirmationRequirement.None, "Tìm chuyên khoa đang hoạt động", new[] { AiActorCapability.ReadClinicCatalog }),
        Def("clinic.search_doctors", AiToolAccessMode.Public, AiToolRiskLevel.Low, AiToolConfirmationRequirement.None, "Tìm bác sĩ đang hoạt động", new[] { AiActorCapability.ReadClinicCatalog }),
        Def("clinic.get_available_slots", AiToolAccessMode.Public, AiToolRiskLevel.Low, AiToolConfirmationRequirement.None, "Tìm khung giờ còn trống theo policy", new[] { AiActorCapability.ReadClinicCatalog }),
        Def("clinic.get_facilities", AiToolAccessMode.Public, AiToolRiskLevel.Low, AiToolConfirmationRequirement.None, "Đọc cơ sở đang hoạt động", new[] { AiActorCapability.ReadClinicCatalog }),
        Def("clinic.get_pricing", AiToolAccessMode.Public, AiToolRiskLevel.Low, AiToolConfirmationRequirement.None, "Đọc bảng giá đã công bố", new[] { AiActorCapability.ReadClinicCatalog }),
        Def("patient.get_my_appointments", AiToolAccessMode.RoleRestricted, AiToolRiskLevel.Low, AiToolConfirmationRequirement.None, "Đọc lịch hẹn của chính bệnh nhân", new[] { AiActorCapability.ReadOwnAppointments }, PatientOnly),
        Def("patient.get_appointment_detail", AiToolAccessMode.RoleRestricted, AiToolRiskLevel.Low, AiToolConfirmationRequirement.None, "Đọc chi tiết lịch hẹn của chính bệnh nhân", new[] { AiActorCapability.ReadOwnAppointments }, PatientOnly),
        Def("patient.prepare_booking", AiToolAccessMode.RoleRestricted, AiToolRiskLevel.Medium, AiToolConfirmationRequirement.ExistingBookingConfirmation, "Kiểm tra và chuẩn bị bản nháp đặt lịch", new[] { AiActorCapability.PrepareBooking }, PatientOnly),
        Def("patient.prepare_cancel_appointment", AiToolAccessMode.RoleRestricted, AiToolRiskLevel.High, AiToolConfirmationRequirement.ExplicitUserConfirmation, "Tạo pending action yêu cầu hủy lịch", new[] { AiActorCapability.PrepareAppointmentChange }, PatientOnly),
        Def("patient.prepare_reschedule_appointment", AiToolAccessMode.RoleRestricted, AiToolRiskLevel.High, AiToolConfirmationRequirement.ExplicitUserConfirmation, "Tạo pending action yêu cầu đổi lịch", new[] { AiActorCapability.PrepareAppointmentChange }, PatientOnly),
        Def("patient.execute_confirmed_action", AiToolAccessMode.RoleRestricted, AiToolRiskLevel.High, AiToolConfirmationRequirement.ExplicitUserConfirmation, "Thực hiện pending action sau khi revalidate", new[] { AiActorCapability.ExecuteConfirmedPatientAction }, PatientOnly)
    };

    private static AiToolDefinition Def(string name, AiToolAccessMode mode, AiToolRiskLevel risk, AiToolConfirmationRequirement confirmation, string description, AiActorCapability[] capabilities, IReadOnlySet<AiActorRole>? roles = null) => new()
    {
        Name = name, Version = "1.0", AccessMode = mode, RiskLevel = risk, Confirmation = confirmation, Description = description,
        AllowedRoles = roles ?? new HashSet<AiActorRole>(), Capabilities = capabilities.ToHashSet(),
        DataSources = new[] { new AiToolDataSource("ClinicCare services", "backend") }
    };
}
