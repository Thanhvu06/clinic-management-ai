using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.DTOs.ChangeRequests;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Application.Organization.Interfaces;
using ClinicManagement.Application.Specialties.Interfaces;
using ClinicManagement.Domain.Entities;
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
        IAiBookingConfirmationStore bookingConfirmations)
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
    }

    public AiToolDefinition Definition { get; } = new() { Name = "patient.copilot.dispatch", Version = "1.0" };

    public Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken = default)
    {
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

    private async Task<AiToolExecutionResult> SearchSpecialtiesAsync(AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var query = GetString(args, "query");
        var items = await _specialties.GetSpecialtiesAsync();
        if (!string.IsNullOrWhiteSpace(query))
            items = items.Where(x => Contains(x.SpecialtyName, query) || Contains(x.SpecialtyCode, query) || Contains(x.Description, query)).ToList();
        return Completed(items.Take(20).Select(x => new { id = x.Id, code = x.SpecialtyCode, name = x.SpecialtyName, description = x.Description }).ToList(), "specialties");
    }

    private async Task<AiToolExecutionResult> SearchDoctorsAsync(AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var query = GetString(args, "query");
        var specialtyId = GetLong(args, "specialtyId");
        var items = await _doctors.GetAllActiveDoctorsAsync();
        if (specialtyId.HasValue) items = items.Where(x => x.SpecialtyId == specialtyId).ToList();
        if (!string.IsNullOrWhiteSpace(query)) items = items.Where(x => Contains(x.FullName, query) || Contains(x.Description, query)).ToList();
        return Completed(items.Take(20).Select(x => new { id = x.Id, name = x.FullName, academicTitle = x.AcademicTitle, specialtyId = x.SpecialtyId, specialtyName = x.SpecialtyName }).ToList(), "doctors");
    }

    private async Task<AiToolExecutionResult> GetAvailableSlotsAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var from = GetDate(args, "fromDate") ?? DateOnly.FromDateTime(DateTime.UtcNow);
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
        return Completed(slots, "availability_policy");
    }

    private async Task<AiToolExecutionResult> GetFacilitiesAsync(CancellationToken cancellationToken)
    {
        var items = await _organization.GetFacilitiesAsync(false, cancellationToken);
        return Completed(items.Select(x => new { id = x.Id, code = x.Code, name = x.Name, address = x.Address, city = x.City, phone = x.Phone, description = x.Description }).ToList(), "facilities");
    }

    private async Task<AiToolExecutionResult> GetPricingAsync(CancellationToken cancellationToken)
    {
        var specialties = await _specialties.GetSpecialtiesAsync();
        var diagnostics = await _diagnostics.GetAllDiagnosticServicesPricingAsync(cancellationToken);
        var data = new
        {
            consultation = specialties.Select(x => new { specialtyId = x.Id, specialty = x.SpecialtyName }).ToList(),
            diagnostics = diagnostics.Where(x => x.IsActive).Select(x => new { serviceId = x.Id, code = x.Code, name = x.Name, category = x.Category, price = x.Price }).ToList()
        };
        return Completed(data, "pricing_catalog");
    }

    private async Task<AiToolExecutionResult> GetMyAppointmentsAsync(AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var args = Parse(invocation.ArgumentsJson);
        var page = Math.Clamp(GetInt(args, "page") ?? 1, 1, 100);
        var pageSize = Math.Clamp(GetInt(args, "pageSize") ?? 20, 1, 50);
        var result = await _appointments.GetPatientAppointmentsAsync(GetString(args, "status"), page, pageSize);
        return Completed(result, "patient_appointments");
    }

    private async Task<AiToolExecutionResult> GetAppointmentDetailAsync(AiToolInvocation invocation, CancellationToken cancellationToken)
    {
        var appointmentId = GetLong(Parse(invocation.ArgumentsJson), "appointmentId");
        if (!appointmentId.HasValue || appointmentId <= 0) return AiToolExecutionResult.Failed("INVALID_APPOINTMENT_ID", "Mã lịch hẹn không hợp lệ.");
        var result = await _appointments.GetPatientAppointmentByIdAsync(appointmentId.Value);
        return Completed(result, "patient_appointments");
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
        var now = DateTime.UtcNow;
        var requestHash = Hash($"{operation}|{normalized}");
        var idempotencyHash = Hash(invocation.IdempotencyKey);
        if (!string.IsNullOrWhiteSpace(invocation.IdempotencyKey))
        {
            var sameKey = await _db.AiPendingToolActions.FirstOrDefaultAsync(x =>
                x.UserId == context.ActorId.Value && x.IdempotencyKeyHash == idempotencyHash &&
                x.ExpiresAtUtc > now && x.CancelledAtUtc == null, cancellationToken);
            if (sameKey != null)
            {
                if (!string.Equals(sameKey.RequestHash, requestHash, StringComparison.Ordinal))
                    return AiToolExecutionResult.Failed("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", "Idempotency key đã được dùng cho thao tác khác.");
                return PendingResult(sameKey, appointment);
            }
        }
        var active = await _db.AiPendingToolActions.FirstOrDefaultAsync(x =>
            x.UserId == context.ActorId.Value && x.ToolName == $"patient.prepare_{operation}_appointment" &&
            x.ResourceId == appointment.Id.ToString() && x.ExpiresAtUtc > now && x.ExecutedAtUtc == null && x.CancelledAtUtc == null, cancellationToken);
        if (active != null)
        {
            if (!string.Equals(active.RequestHash, requestHash, StringComparison.Ordinal)) return AiToolExecutionResult.Failed("ACTIVE_ACTION_EXISTS", "Đã có một thao tác thay đổi lịch đang chờ xác nhận.");
            return PendingResult(active, appointment);
        }

        var action = new AiPendingToolAction
        {
            ActionId = Guid.NewGuid(), UserId = context.ActorId.Value, SessionId = context.SessionId ?? "gateway",
            ToolName = $"patient.prepare_{operation}_appointment", ToolVersion = "1.0",
            RequestHash = requestHash, ResourceType = "appointment", ResourceId = appointment.Id.ToString(),
            NormalizedArgumentsJson = JsonSerializer.Serialize(new { appointmentId = appointment.Id, requestedSlotId }),
            CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(12), IdempotencyKeyHash = string.IsNullOrWhiteSpace(invocation.IdempotencyKey) ? null : idempotencyHash
        };
        _db.AiPendingToolActions.Add(action);
        await _db.SaveChangesAsync(cancellationToken);
        return PendingResult(action, appointment);
    }

    private async Task<AiToolExecutionResult> ExecuteConfirmedActionAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.ActorId.HasValue) return AiToolExecutionResult.Failed("AUTHENTICATION_REQUIRED", "Bạn cần đăng nhập để xác nhận thao tác.");
        var args = Parse(invocation.ArgumentsJson);
        var actionIdText = GetString(args, "actionId");
        if (!Guid.TryParse(actionIdText, out var actionId)) return AiToolExecutionResult.Failed("INVALID_ACTION_ID", "Mã thao tác không hợp lệ.");
        if (!GetBool(args, "confirm")) return AiToolExecutionResult.Failed("CONFIRMATION_REQUIRED", "Cần xác nhận rõ ràng trước khi thực hiện.");

        var action = await _db.AiPendingToolActions.FirstOrDefaultAsync(x => x.ActionId == actionId, cancellationToken);
        if (action == null || action.UserId != context.ActorId.Value) return AiToolExecutionResult.Failed("ACTION_NOT_FOUND", "Thao tác không tồn tại hoặc không thuộc tài khoản này.");
        if (!string.Equals(action.SessionId, context.SessionId ?? "", StringComparison.Ordinal) && action.SessionId != "gateway") return AiToolExecutionResult.Failed("SESSION_MISMATCH", "Thao tác thuộc một phiên hội thoại khác.");
        if (action.ExecutedAtUtc.HasValue) return Completed(new { actionId, status = "already_completed", reference = action.ExecutionResultReference }, "pending_action");
        if (action.CancelledAtUtc.HasValue || action.ExpiresAtUtc <= DateTime.UtcNow) return AiToolExecutionResult.Failed("ACTION_EXPIRED", "Thao tác đã hết hạn hoặc đã bị hủy.");
        if (action.ConfirmedAtUtc.HasValue) return AiToolExecutionResult.Failed("ACTION_IN_PROGRESS", "Thao tác này đang được xử lý, vui lòng chờ kết quả.");

        var stored = Parse(action.NormalizedArgumentsJson);
        var appointmentId = GetLong(stored, "appointmentId");
        if (!appointmentId.HasValue) return AiToolExecutionResult.Failed("INVALID_PENDING_ACTION", "Dữ liệu thao tác không hợp lệ.");
        var appointment = await _appointments.GetPatientAppointmentByIdAsync(appointmentId.Value);
        var isReschedule = action.ToolName.Contains("reschedule", StringComparison.OrdinalIgnoreCase);

        action.ConfirmedAtUtc = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AiToolExecutionResult.Failed("ACTION_IN_PROGRESS", "Thao tác này đang được xử lý, vui lòng chờ kết quả.", true);
        }
        ClinicManagement.Application.Appointments.DTOs.ChangeRequests.ChangeRequestDto result;
        try
        {
            result = isReschedule
                ? await _changeRequests.CreateRescheduleRequestAsync(appointmentId.Value, new CreateRescheduleRequestDto { RequestedSlotId = GetLong(stored, "requestedSlotId") ?? 0, Reason = "Yêu cầu đổi lịch từ Patient Copilot" })
                : await _changeRequests.CreateCancellationRequestAsync(appointmentId.Value, new CreateCancellationRequestDto { Reason = "Yêu cầu hủy lịch từ Patient Copilot" });
        }
        catch
        {
            action.CancelledAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
        action.ExecutedAtUtc = DateTime.UtcNow;
        action.ExecutionResultReference = result.Id.ToString();
        await _db.SaveChangesAsync(cancellationToken);
        return Completed(new { actionId, changeRequestId = result.Id, operation = isReschedule ? "reschedule" : "cancel", appointmentId = appointment.Id }, "change_request");
    }

    private async Task<long?> ResolvePatientIdAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty) return null;
        return await _db.Patients.AsNoTracking().Where(x => x.UserId == userId.Value).Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static AiToolExecutionResult PendingResult(AiPendingToolAction action, Application.Appointments.DTOs.AppointmentDto appointment) => new()
    {
        Status = "pending_confirmation", RequiresConfirmation = true, ActionId = action.ActionId.ToString(),
        Data = new { actionId = action.ActionId, appointmentId = appointment.Id, appointmentCode = appointment.AppointmentCode, expiresAtUtc = action.ExpiresAtUtc, confirmation = "Xác nhận thao tác này" },
        DataSources = new[] { new AiToolDataSource("appointment_service", "service"), new AiToolDataSource("pending_action_store", "database") }
    };

    private static AiToolExecutionResult Completed(object? data, string source) => new()
    {
        Status = "completed", Data = data, DataSources = new[] { new AiToolDataSource(source, "service") }
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
