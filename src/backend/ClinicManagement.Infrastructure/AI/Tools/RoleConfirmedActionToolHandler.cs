using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Appointments.DTOs.Doctor;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Pharmacy.Interfaces;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Application.Visits.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

/// <summary>
/// Server-side implementation for human-initiated role mutations. A model can
/// describe a prepare action but cannot invoke this handler through the planner
/// channel, and no stored action is trusted at confirmation time without a
/// fresh role/facility/resource validation.
/// </summary>
public sealed class RoleConfirmedActionToolHandler : IAiToolHandler
{
    private const string ConfirmationTool = "role.execute_confirmed_action";
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IPatientVisitService _visits;
    private readonly IDiagnosticWorkflowService _diagnostics;
    private readonly IDoctorAppointmentService _doctorAppointments;
    private readonly IPharmacyService _pharmacy;

    public RoleConfirmedActionToolHandler(
        AppDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IPatientVisitService visits,
        IDiagnosticWorkflowService diagnostics,
        IDoctorAppointmentService doctorAppointments,
        IPharmacyService pharmacy)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _visits = visits;
        _diagnostics = diagnostics;
        _doctorAppointments = doctorAppointments;
        _pharmacy = pharmacy;
    }

    public AiToolDefinition Definition { get; } = new() { Name = "role.confirmed-action.dispatch", Version = "1.0" };

    public AiToolArgumentValidationResult ValidateArguments(AiToolInvocation invocation, AiToolExecutionContext context)
    {
        var name = Canonical(invocation.ToolName);
        if (!TryParseObject(invocation.ArgumentsJson, out var args, out var error)) return error!;
        using (args)
        {
            if (name == ConfirmationTool)
            {
                if (context.InvocationChannel != AiToolInvocationChannel.DirectHumanConfirmation)
                    return AiToolArgumentValidationResult.Invalid("DIRECT_CONFIRMATION_REQUIRED", "Thao tác này chỉ được thực hiện qua endpoint xác nhận trực tiếp.");
                return ValidateSchema(args.RootElement, new[] { "actionId", "confirm", "concurrencyToken" }, new[] { "actionId", "confirm", "concurrencyToken" }, validate: root =>
                {
                    if (!TryGuid(root, "actionId") || !IsTrue(root, "confirm") || !IsConfirmationToken(GetString(root, "concurrencyToken")))
                        return AiToolArgumentValidationResult.Invalid("INVALID_CONFIRMATION", "Thông tin xác nhận không hợp lệ.");
                    return null;
                });
            }

            if (context.InvocationChannel != AiToolInvocationChannel.DirectHumanPreparation)
                return AiToolArgumentValidationResult.Invalid("DIRECT_PREPARATION_REQUIRED", "Prepare-write chỉ được gọi từ luồng thao tác trực tiếp đã xác thực.");

            return name switch
            {
                "reception.prepare_check_in_appointment" => ValidateSchema(args.RootElement, new[] { "appointmentId", "departmentId", "roomId", "assignedDoctorId" }, new[] { "appointmentId", "departmentId" }, root => PositiveIds(root, "appointmentId", "departmentId", "roomId", "assignedDoctorId")),
                "reception.prepare_create_walk_in" => ValidateSchema(args.RootElement, new[] { "existingPatientId", "departmentId", "roomId", "assignedDoctorId", "chiefComplaint", "priority" }, new[] { "existingPatientId", "departmentId", "chiefComplaint" }, root =>
                    PositiveIds(root, "existingPatientId", "departmentId", "roomId", "assignedDoctorId") ?? ValidateText(root, "chiefComplaint", 5, 1000) ?? ValidatePriority(root)),
                "doctor.prepare_diagnostic_order" => ValidateSchema(args.RootElement, new[] { "appointmentId", "visitId", "departmentId", "clinicalIndication", "note", "serviceIds" }, new[] { "clinicalIndication", "serviceIds" }, ValidateDiagnosticOrder),
                "doctor.prepare_prescription_draft" => ValidateSchema(args.RootElement, new[] { "appointmentId", "visitId", "departmentId", "notes", "items" }, new[] { "items" }, ValidatePrescriptionDraft),
                "technician.prepare_start_diagnostic_order" => ValidateSchema(args.RootElement, new[] { "orderId" }, new[] { "orderId" }, root => PositiveIds(root, "orderId")),
                "technician.prepare_record_diagnostic_result" => ValidateSchema(args.RootElement, new[] { "orderId", "itemId", "resultText", "conclusion", "referenceRange", "unit" }, new[] { "orderId", "itemId", "resultText" }, root =>
                    PositiveIds(root, "orderId", "itemId") ?? ValidateText(root, "resultText", 1, 4000)),
                "technician.prepare_complete_diagnostic_order" => ValidateSchema(args.RootElement, new[] { "orderId" }, new[] { "orderId" }, root => PositiveIds(root, "orderId")),
                "pharmacist.prepare_reserve_prescription" or "pharmacist.prepare_dispense_prescription" => ValidateSchema(args.RootElement, new[] { "prescriptionId" }, new[] { "prescriptionId" }, root => PositiveIds(root, "prescriptionId")),
                _ => AiToolArgumentValidationResult.Invalid("UNKNOWN_TOOL", "Prepare action không được hỗ trợ.")
            };
        }
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var name = Canonical(invocation.ToolName);
        if (name == ConfirmationTool)
            return await ConfirmAsync(invocation, context, cancellationToken);
        return await PrepareAsync(invocation, context, cancellationToken);
    }

    private async Task<AiToolExecutionResult> PrepareAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken ct)
    {
        if (context.InvocationChannel != AiToolInvocationChannel.DirectHumanPreparation)
            return AiToolExecutionResult.Failed("DIRECT_PREPARATION_REQUIRED", "Prepare-write chỉ được gọi từ luồng thao tác trực tiếp đã xác thực.");
        if (!context.ActorId.HasValue || context.ActorId == Guid.Empty)
            return AiToolExecutionResult.Failed("AUTHENTICATION_REQUIRED", "Cần đăng nhập để chuẩn bị thao tác.");
        if (!IsValidSessionId(context.SessionId))
            return AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần session copilot hợp lệ để chuẩn bị thao tác.");

        var tool = Canonical(invocation.ToolName);
        if (!TryGetActionRole(tool, out var role) || !context.Roles.Contains(role))
            return AiToolExecutionResult.Failed("FORBIDDEN_TOOL", "Vai trò hiện tại không được phép chuẩn bị thao tác này.");
        if (!TryParseObject(invocation.ArgumentsJson, out var document, out _))
            return AiToolExecutionResult.Failed("INVALID_TOOL_ARGUMENTS", "Tham số thao tác không hợp lệ.");
        using (document)
        {
            var validation = ValidateArguments(invocation, context);
            if (!validation.IsValid) return AiToolExecutionResult.Failed(validation.Code, validation.Message);
            var preparation = await ResolvePreparationAsync(tool, role, context.ActorId.Value, document.RootElement, ct);
            if (!preparation.IsValid)
                return AiToolExecutionResult.Failed(preparation.ErrorCode!, preparation.ErrorMessage!);
            if (context.FacilityId.HasValue && context.FacilityId != preparation.FacilityId)
                return AiToolExecutionResult.Failed("FACILITY_SCOPE_DENIED", "Cơ sở của phiên đăng nhập không khớp với tài nguyên thao tác.");

            var now = _clock.UtcNow;
            await ExpireActiveActionsAsync(context.ActorId.Value, now, ct);
            var normalizedArguments = Canonicalize(document.RootElement);
            var requestHash = Hash($"{tool}|{role}|{preparation.ResourceType}|{preparation.ResourceId}|{normalizedArguments}");
            var idempotencyHash = string.IsNullOrWhiteSpace(invocation.IdempotencyKey) ? null : Hash(invocation.IdempotencyKey);

            if (!string.IsNullOrWhiteSpace(idempotencyHash))
            {
                var existingByKey = await _db.AiPendingToolActions.FirstOrDefaultAsync(x =>
                    x.UserId == context.ActorId.Value && x.SessionId == context.SessionId && x.IdempotencyKeyHash == idempotencyHash &&
                x.State != AiPendingToolActionState.Cancelled && x.State != AiPendingToolActionState.Expired, ct);
                if (existingByKey != null)
                {
                    if (!string.Equals(existingByKey.RequestHash, requestHash, StringComparison.Ordinal))
                        return AiToolExecutionResult.Failed("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", "Idempotency key đã được dùng với dữ liệu khác.");
                    return Pending(existingByKey, "Thao tác đang chờ xác nhận.");
                }
            }

            var active = await _db.AiPendingToolActions.FirstOrDefaultAsync(x =>
                x.UserId == context.ActorId.Value && x.ResourceType == preparation.ResourceType && x.ResourceId == preparation.ResourceId &&
                (x.State == AiPendingToolActionState.PendingConfirmation ||
                 x.State == AiPendingToolActionState.Executing ||
                 x.State == AiPendingToolActionState.FailedRetryable), ct);
            if (active != null)
            {
                if (!string.Equals(active.RequestHash, requestHash, StringComparison.Ordinal))
                    return AiToolExecutionResult.Failed("ACTIVE_ACTION_EXISTS", "Đã có thao tác đang chờ xác nhận trên tài nguyên này.");
                return Pending(active, "Thao tác đang chờ xác nhận.");
            }

            var actionId = Guid.NewGuid();
            var token = CreateConfirmationToken();
            var action = new AiPendingToolAction
            {
                ActionId = actionId,
                SourceAiActionId = actionId,
                UserId = context.ActorId.Value,
                ActorRole = role.ToString(),
                SessionId = context.SessionId!.Trim(),
                ConversationId = NormalizeId(invocation.ConversationId),
                FacilityId = preparation.FacilityId,
                ToolName = tool,
                ToolVersion = invocation.ToolVersion?.Trim() ?? "1.0",
                RequestHash = requestHash,
                ResourceType = preparation.ResourceType,
                ResourceId = preparation.ResourceId,
                ResourceVersion = preparation.Version,
                NormalizedArgumentsJson = normalizedArguments,
                IdempotencyKeyHash = idempotencyHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(12),
                State = AiPendingToolActionState.PendingConfirmation
            };
            action.ConfirmationTokenHash = ConfirmationBindingHash(action, token);
            _db.AiPendingToolActions.Add(action);
            try
            {
                await _db.SaveChangesAsync(ct);
                return Pending(action, "Thao tác đã được kiểm tra trước; hãy xác nhận để thực hiện.", token);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                var concurrent = await _db.AiPendingToolActions.FirstOrDefaultAsync(x =>
                    x.UserId == context.ActorId.Value && x.ResourceType == preparation.ResourceType && x.ResourceId == preparation.ResourceId &&
                    (x.State == AiPendingToolActionState.PendingConfirmation ||
                     x.State == AiPendingToolActionState.Executing ||
                     x.State == AiPendingToolActionState.FailedRetryable), ct);
                return concurrent != null && string.Equals(concurrent.RequestHash, requestHash, StringComparison.Ordinal)
                    ? Pending(concurrent, "Thao tác đang chờ xác nhận.")
                    : AiToolExecutionResult.Failed("ACTIVE_ACTION_EXISTS", "Không thể tạo thêm thao tác đồng thời trên tài nguyên này.", true);
            }
        }
    }

    private async Task<AiToolExecutionResult> ConfirmAsync(AiToolInvocation invocation, AiToolExecutionContext context, CancellationToken ct)
    {
        if (context.InvocationChannel != AiToolInvocationChannel.DirectHumanConfirmation)
            return AiToolExecutionResult.Failed("DIRECT_CONFIRMATION_REQUIRED", "Thao tác này chỉ được thực hiện qua endpoint xác nhận trực tiếp.");
        if (!context.ActorId.HasValue || context.ActorId == Guid.Empty || !IsValidSessionId(context.SessionId))
            return AiToolExecutionResult.Failed("SESSION_REQUIRED", "Cần phiên và tài khoản hợp lệ để xác nhận thao tác.");
        if (!TryParseObject(invocation.ArgumentsJson, out var document, out _))
            return AiToolExecutionResult.Failed("INVALID_CONFIRMATION", "Thông tin xác nhận không hợp lệ.");
        using (document)
        {
            if (!TryGuid(document.RootElement, "actionId", out var actionId))
                return AiToolExecutionResult.Failed("INVALID_ACTION_ID", "Mã thao tác không hợp lệ.");
            var suppliedToken = GetString(document.RootElement, "concurrencyToken");
            var action = await _db.AiPendingToolActions.FirstOrDefaultAsync(x => x.ActionId == actionId, ct);
            if (action == null || action.UserId != context.ActorId.Value)
                return AiToolExecutionResult.Failed("ACTION_NOT_FOUND", "Thao tác không tồn tại hoặc không thuộc tài khoản này.");
            if (!Enum.TryParse<AiActorRole>(action.ActorRole, true, out var role) || !context.Roles.Contains(role))
                return AiToolExecutionResult.Failed("ROLE_MISMATCH", "Vai trò hiện tại không còn được phép thực hiện thao tác này.");
            if (!string.Equals(action.SessionId, context.SessionId, StringComparison.Ordinal))
                return AiToolExecutionResult.Failed("SESSION_MISMATCH", "Thao tác thuộc phiên copilot khác.");
            if (context.FacilityId.HasValue && action.FacilityId != context.FacilityId)
                return AiToolExecutionResult.Failed("FACILITY_SCOPE_DENIED", "Thao tác không thuộc cơ sở đang được chọn.");
            if (action.State == AiPendingToolActionState.Completed || action.ExecutedAtUtc.HasValue)
            {
                if (!VerifyConfirmationToken(action, suppliedToken))
                    return AiToolExecutionResult.Failed("CONCURRENCY_CONFLICT", "Mã xác nhận không còn hợp lệ; hãy tạo lại thao tác.");
                return Completed(new { actionId, status = "already_completed", reference = action.ExecutionResultReference }, "idempotent_replay", "Thao tác đã hoàn tất trước đó.", true);
            }
            var now = _clock.UtcNow;
            if (action.CancelledAtUtc.HasValue || action.State == AiPendingToolActionState.Cancelled)
                return AiToolExecutionResult.Failed("ACTION_CANCELLED", "Thao tác đã bị hủy.");
            if (action.State == AiPendingToolActionState.Expired || action.ExpiresAtUtc <= now)
            {
                await TerminalizeAsync(action.ActionId, null, AiPendingToolActionState.Expired, "ACTION_EXPIRED", ct);
                return AiToolExecutionResult.Failed("ACTION_EXPIRED", "Thao tác đã hết hạn.");
            }
            if (action.State == AiPendingToolActionState.Executing && action.ExecutionLeaseExpiresAtUtc > now)
                return AiToolExecutionResult.Failed("ACTION_IN_PROGRESS", "Thao tác đang được xử lý.", true);
            try
            {
                ValidateStoredAction(action, context);
            }
            catch (BusinessException)
            {
                await TerminalizeAsync(action.ActionId, null, AiPendingToolActionState.FailedTerminal, "INVALID_PENDING_ACTION", ct);
                return AiToolExecutionResult.Failed("INVALID_PENDING_ACTION", "Dữ liệu pending action không còn hợp lệ.");
            }
            if (!VerifyConfirmationToken(action, suppliedToken))
                return AiToolExecutionResult.Failed("CONCURRENCY_CONFLICT", "Mã xác nhận không còn hợp lệ; hãy tạo lại thao tác.");

            var leaseId = Guid.NewGuid();
            var claimed = await _db.AiPendingToolActions
                .Where(x => x.ActionId == action.ActionId && x.UserId == context.ActorId.Value && x.SessionId == context.SessionId &&
                    x.ExpiresAtUtc > now && x.CancelledAtUtc == null &&
                    (x.State == AiPendingToolActionState.PendingConfirmation || x.State == AiPendingToolActionState.FailedRetryable ||
                     (x.State == AiPendingToolActionState.Executing && x.ExecutionLeaseExpiresAtUtc <= now)))
                .ExecuteUpdateAsync(x => x
                    .SetProperty(a => a.State, AiPendingToolActionState.Executing)
                    .SetProperty(a => a.ExecutionLeaseId, leaseId)
                    .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, now.AddMinutes(2))
                    .SetProperty(a => a.ExecutionAttemptCount, a => a.ExecutionAttemptCount + 1)
                    .SetProperty(a => a.ConfirmedAtUtc, a => a.ConfirmedAtUtc ?? now)
                    .SetProperty(a => a.LastErrorCode, (string?)null), ct);
            if (claimed == 0) return AiToolExecutionResult.Failed("ACTION_IN_PROGRESS", "Thao tác đã được claim bởi yêu cầu khác.", true);

            action = await _db.AiPendingToolActions.AsNoTracking().FirstAsync(x => x.ActionId == actionId, ct);
            try
            {
                var result = await RevalidateAndExecuteAsync(action, context, ct);
                var completed = await _db.AiPendingToolActions.Where(x => x.ActionId == actionId && x.ExecutionLeaseId == leaseId && x.State == AiPendingToolActionState.Executing)
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(a => a.State, AiPendingToolActionState.Completed)
                        .SetProperty(a => a.ExecutedAtUtc, _clock.UtcNow)
                        .SetProperty(a => a.ExecutionResultReference, result.Reference)
                        .SetProperty(a => a.ExecutionLeaseId, (Guid?)null)
                        .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null)
                        .SetProperty(a => a.LastErrorCode, (string?)null), ct);
                return completed == 0
                    ? AiToolExecutionResult.Failed("ACTION_IN_PROGRESS", "Thao tác đã được hoàn tất bởi yêu cầu khác.", true)
                    : Completed(new { actionId, operation = action.ToolName, reference = result.Reference, recovered = result.Recovered }, result.ResultType, result.Message, result.Recovered);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var errorCode = ex switch
                {
                    ConflictException conflict => conflict.ErrorCode,
                    BusinessException business => business.ErrorCode,
                    NotFoundException => "RESOURCE_NOT_FOUND",
                    JsonException => "INVALID_PENDING_ACTION",
                    _ => "ACTION_EXECUTION_FAILED"
                };
                var retryable = ex is DbUpdateException or DbException or TimeoutException;
                await TerminalizeAsync(actionId, leaseId, retryable ? AiPendingToolActionState.FailedRetryable : AiPendingToolActionState.FailedTerminal, errorCode, ct);
                return AiToolExecutionResult.Failed(errorCode, retryable ? "Không thể hoàn tất thao tác lúc này; bạn có thể thử lại." : "Dữ liệu hoặc điều kiện nghiệp vụ đã thay đổi; thao tác bị từ chối.", retryable);
            }
        }
    }

    private async Task<ExecutionResult> RevalidateAndExecuteAsync(AiPendingToolAction action, AiToolExecutionContext context, CancellationToken ct)
    {
        ValidateStoredAction(action, context);
        using var document = JsonDocument.Parse(action.NormalizedArgumentsJson);
        var args = document.RootElement;
        return action.ToolName switch
        {
            "reception.prepare_check_in_appointment" => await ExecuteReceptionCheckInAsync(action, context, args, ct),
            "reception.prepare_create_walk_in" => await ExecuteWalkInAsync(action, context, args, ct),
            "doctor.prepare_diagnostic_order" => await ExecuteDoctorDiagnosticOrderAsync(action, context, args, ct),
            "doctor.prepare_prescription_draft" => await ExecuteDoctorPrescriptionDraftAsync(action, context, args, ct),
            "technician.prepare_start_diagnostic_order" => await ExecuteTechnicianStartAsync(action, context, args, ct),
            "technician.prepare_record_diagnostic_result" => await ExecuteTechnicianResultAsync(action, context, args, ct),
            "technician.prepare_complete_diagnostic_order" => await ExecuteTechnicianCompleteAsync(action, context, args, ct),
            "pharmacist.prepare_reserve_prescription" => await ExecuteReserveAsync(action, context, args, ct),
            "pharmacist.prepare_dispense_prescription" => await ExecuteDispenseAsync(action, context, args, ct),
            _ => throw new BusinessException("INVALID_PENDING_ACTION", "Loại pending action không được hỗ trợ.")
        };
    }

    private async Task<ExecutionResult> ExecuteReceptionCheckInAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var appointmentId = GetLong(args, "appointmentId")!.Value;
        var departmentId = GetLong(args, "departmentId")!.Value;
        var resolved = await ResolveReceptionAppointmentAsync(context.ActorId!.Value, appointmentId, departmentId, GetLong(args, "roomId"), GetLong(args, "assignedDoctorId"), ct);
        EnsureFacility(action, resolved.FacilityId);
        var existing = await _db.PatientVisits.AsNoTracking().FirstOrDefaultAsync(x => x.AppointmentId == appointmentId, ct);
        if (existing != null) return new ExecutionResult(existing.Id.ToString(), "check_in_ticket", "Lịch hẹn đã được check-in trước đó.", true);
        EnsureVersion(action, resolved.Version);
        var ticket = await _visits.CheckInAppointmentAsync(new AppointmentCheckInRequest
        {
            AppointmentId = appointmentId,
            DepartmentId = departmentId,
            RoomId = GetLong(args, "roomId"),
            AssignedDoctorId = GetLong(args, "assignedDoctorId")
        }, ct);
        return new ExecutionResult(ticket.VisitId.ToString(), "check_in_ticket", "Đã check-in lịch hẹn và tạo lượt khám.");
    }

    private async Task<ExecutionResult> ExecuteWalkInAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var patientId = GetLong(args, "existingPatientId")!.Value;
        var departmentId = GetLong(args, "departmentId")!.Value;
        var resolved = await ResolveWalkInAsync(context.ActorId!.Value, patientId, departmentId, GetLong(args, "roomId"), GetLong(args, "assignedDoctorId"), ct);
        EnsureFacility(action, resolved.FacilityId);
        EnsureVersion(action, resolved.Version);
        var key = $"ai-action:{action.ActionId:N}";
        var ticket = await _visits.ReceptionIntakeAsync(new ReceptionIntakeRequest
        {
            IdempotencyKey = key,
            ExistingPatientId = patientId,
            FacilityId = resolved.FacilityId!.Value,
            DepartmentId = departmentId,
            RoomId = GetLong(args, "roomId"),
            AssignedDoctorId = GetLong(args, "assignedDoctorId"),
            ChiefComplaint = GetString(args, "chiefComplaint")!,
            Priority = GetPriority(args)
        }, ct);
        return new ExecutionResult(ticket.VisitId.ToString(), "check_in_ticket", "Đã tạo lượt khám vãng lai.");
    }

    private async Task<ExecutionResult> ExecuteDoctorDiagnosticOrderAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var source = action.SourceAiActionId ?? action.ActionId;
        var recovered = await _db.DiagnosticOrders.AsNoTracking().FirstOrDefaultAsync(x => x.SourceAiActionId == source, ct);
        if (recovered != null) return new ExecutionResult(recovered.Id.ToString(), "diagnostic_order", "Phiếu chỉ định đã được tạo trước đó.", true);
        var resource = await ResolveDoctorClinicalResourceAsync(context.ActorId!.Value, args, ct);
        EnsureFacility(action, resource.FacilityId);
        EnsureVersion(action, resource.Version);
        var request = new CreateDiagnosticOrderRequest
        {
            ClinicalIndication = GetString(args, "clinicalIndication")!,
            Note = GetString(args, "note"),
            ServiceIds = GetLongArray(args, "serviceIds")
        };
        var order = resource.VisitId.HasValue
            ? await _diagnostics.CreateOrderForVisitDoctorAsync(resource.VisitId.Value, request, source, resource.FacilityId)
            : await _diagnostics.CreateOrderForDoctorAsync(resource.AppointmentId!.Value, request, source, resource.FacilityId);
        return new ExecutionResult(order.Id.ToString(), "diagnostic_order", "Đã tạo phiếu chỉ định cận lâm sàng.");
    }

    private async Task<ExecutionResult> ExecuteDoctorPrescriptionDraftAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var resource = await ResolveDoctorClinicalResourceAsync(context.ActorId!.Value, args, ct);
        EnsureFacility(action, resource.FacilityId);
        EnsureVersion(action, resource.Version);
        var request = new SavePrescriptionDraftRequest
        {
            Notes = GetString(args, "notes"),
            Items = ParsePrescriptionItems(args)
        };
        var draft = resource.VisitId.HasValue
            ? await _doctorAppointments.SaveVisitPrescriptionDraftAsync(resource.VisitId.Value, request)
            : await _doctorAppointments.SavePrescriptionDraftAsync(resource.AppointmentId!.Value, request);
        return new ExecutionResult(draft.Id.ToString(), "prescription_draft", "Đã lưu bản nháp đơn thuốc; đơn chưa được phát hành.");
    }

    private async Task<ExecutionResult> ExecuteTechnicianStartAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var order = await ResolveTechnicianOrderAsync(context.ActorId!.Value, GetLong(args, "orderId")!.Value, ct);
        EnsureFacility(action, order.FacilityId);
        if (order.Status == DiagnosticOrderStatus.InProgress && order.StartedByUserId == context.ActorId.Value)
            return new ExecutionResult(order.Id.ToString(), "diagnostic_order", "Phiếu chỉ định đã được tiếp nhận trước đó.", true);
        EnsureVersion(action, order.Version);
        var updated = await _diagnostics.StartOrderAsync(order.Id, new TransitionDiagnosticOrderRequest { RowVersion = order.Version });
        return new ExecutionResult(updated.Id.ToString(), "diagnostic_order", "Đã tiếp nhận thực hiện phiếu chỉ định.");
    }

    private async Task<ExecutionResult> ExecuteTechnicianResultAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var order = await ResolveTechnicianResultAsync(context.ActorId!.Value, GetLong(args, "orderId")!.Value, GetLong(args, "itemId")!.Value, ct);
        var itemId = GetLong(args, "itemId")!.Value;
        var item = await _db.DiagnosticOrderItems.AsNoTracking().Include(x => x.Result).FirstOrDefaultAsync(x => x.Id == itemId && x.DiagnosticOrderId == order.Id, ct)
            ?? throw new NotFoundException("Dịch vụ chỉ định không thuộc phiếu hiện tại.");
        EnsureFacility(action, order.FacilityId);
        if (item.Result != null && item.Result.ResultedByUserId == context.ActorId.Value && item.Status == DiagnosticItemStatus.Completed)
            return new ExecutionResult(order.Id.ToString(), "diagnostic_result", "Kết quả đã được lưu trước đó.", true);
        EnsureVersion(action, order.Version);
        var updated = await _diagnostics.RecordItemResultAsync(order.Id, itemId, new RecordDiagnosticResultRequest
        {
            ResultText = GetString(args, "resultText")!,
            Conclusion = GetString(args, "conclusion"),
            ReferenceRange = GetString(args, "referenceRange"),
            Unit = GetString(args, "unit"),
            RowVersion = item.RowVersion is { Length: > 0 } ? Convert.ToBase64String(item.RowVersion) : null
        });
        return new ExecutionResult(updated.Id.ToString(), "diagnostic_result", "Đã lưu kết quả kỹ thuật; kết quả chưa được tự phát hành ngoài workflow.");
    }

    private async Task<ExecutionResult> ExecuteTechnicianCompleteAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var order = await ResolveTechnicianOrderAsync(context.ActorId!.Value, GetLong(args, "orderId")!.Value, ct);
        EnsureFacility(action, order.FacilityId);
        if (order.Status == DiagnosticOrderStatus.Completed && order.CompletedByUserId == context.ActorId.Value)
            return new ExecutionResult(order.Id.ToString(), "diagnostic_order", "Phiếu chỉ định đã hoàn tất trước đó.", true);
        EnsureVersion(action, order.Version);
        var updated = await _diagnostics.CompleteOrderAsync(order.Id, new TransitionDiagnosticOrderRequest { RowVersion = order.Version });
        return new ExecutionResult(updated.Id.ToString(), "diagnostic_order", "Đã hoàn tất phiếu chỉ định theo workflow.");
    }

    private async Task<ExecutionResult> ExecuteDispenseAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var prescription = await ResolvePharmacyPrescriptionAsync(context.ActorId!.Value, GetLong(args, "prescriptionId")!.Value, ct);
        EnsureFacility(action, prescription.FacilityId);
        if (prescription.PrescriptionStatus == PrescriptionStatus.Dispensed && prescription.DispensedByUserId == context.ActorId.Value)
            return new ExecutionResult(prescription.Id.ToString(), "dispense", "Đơn thuốc đã được cấp phát trước đó.", true);
        EnsureVersion(action, prescription.Version);
        var result = await _pharmacy.DispensePrescriptionAsync(prescription.Id);
        return new ExecutionResult(result.PrescriptionId.ToString(), "dispense", result.Message);
    }

    private async Task<ExecutionResult> ExecuteReserveAsync(AiPendingToolAction action, AiToolExecutionContext context, JsonElement args, CancellationToken ct)
    {
        var prescription = await ResolvePharmacyPrescriptionAsync(context.ActorId!.Value, GetLong(args, "prescriptionId")!.Value, ct);
        EnsureFacility(action, prescription.FacilityId);
        if (prescription.PrescriptionStatus == PrescriptionStatus.ReservedForPurchase)
            return new ExecutionResult(prescription.Id.ToString(), "prescription_reservation", "Đơn thuốc đã được giữ chỗ trước đó.", true);
        EnsureVersion(action, prescription.Version);
        var result = await _pharmacy.ConfirmPurchaseAsync(prescription.Id);
        return new ExecutionResult(result.Id.ToString(), "prescription_reservation", "Đã giữ chỗ thuốc theo đơn.");
    }

    private async Task<Preparation> ResolvePreparationAsync(string tool, AiActorRole role, Guid actorId, JsonElement args, CancellationToken ct) => tool switch
    {
        "reception.prepare_check_in_appointment" => await ResolveReceptionAppointmentAsync(actorId, GetLong(args, "appointmentId")!.Value, GetLong(args, "departmentId")!.Value, GetLong(args, "roomId"), GetLong(args, "assignedDoctorId"), ct),
        "reception.prepare_create_walk_in" => await ResolveWalkInAsync(actorId, GetLong(args, "existingPatientId")!.Value, GetLong(args, "departmentId")!.Value, GetLong(args, "roomId"), GetLong(args, "assignedDoctorId"), ct),
        "doctor.prepare_diagnostic_order" or "doctor.prepare_prescription_draft" => await ResolveDoctorClinicalResourceAsync(actorId, args, ct),
        "technician.prepare_start_diagnostic_order" or "technician.prepare_complete_diagnostic_order" => await ResolveTechnicianOrderAsync(actorId, GetLong(args, "orderId")!.Value, ct),
        "technician.prepare_record_diagnostic_result" => await ResolveTechnicianResultAsync(actorId, GetLong(args, "orderId")!.Value, GetLong(args, "itemId")!.Value, ct),
        "pharmacist.prepare_reserve_prescription" or "pharmacist.prepare_dispense_prescription" => await ResolvePharmacyPrescriptionAsync(actorId, GetLong(args, "prescriptionId")!.Value, ct),
        _ => Preparation.Invalid("UNKNOWN_TOOL", "Prepare action không được hỗ trợ.")
    };

    private async Task<Preparation> ResolveReceptionAppointmentAsync(
        Guid actorId,
        long appointmentId,
        long departmentId,
        long? roomId,
        long? assignedDoctorId,
        CancellationToken ct)
    {
        var appointment = await _db.Appointments.AsNoTracking().Where(x => x.Id == appointmentId)
            .Select(x => new
            {
                x.Id,
                DoctorUserId = x.Doctor.UserId,
                x.DoctorId,
                x.PatientId,
                x.FacilityId,
                x.AppointmentSlotId,
                x.AppointmentDate,
                x.StartTime,
                x.EndTime,
                x.Reason,
                x.Status
            }).FirstOrDefaultAsync(ct);
        if (appointment == null || !appointment.FacilityId.HasValue || appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed)
            return Preparation.Invalid("RESOURCE_SCOPE_DENIED", "Lịch hẹn không còn có thể check-in.");
        var department = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == departmentId && x.IsActive, ct);
        if (department == null || department.FacilityId != appointment.FacilityId ||
            !await HasFacilityRoleAsync(actorId, AiActorRole.Receptionist, appointment.FacilityId.Value, null, ct) ||
            !await HasDoctorAtFacilityAsync(appointment.DoctorUserId, department.FacilityId, ct))
            return Preparation.Invalid("FACILITY_SCOPE_DENIED", "Lịch hẹn không thuộc cơ sở tiếp nhận được phân quyền.");

        if (roomId.HasValue && !await _db.Rooms.AsNoTracking().AnyAsync(x => x.Id == roomId.Value && x.DepartmentId == department.Id && x.IsActive, ct))
            return Preparation.Invalid("RESOURCE_SCOPE_DENIED", "Phòng tiếp nhận không thuộc khoa đang được phân quyền.");

        var executingDoctorUserId = assignedDoctorId.HasValue
            ? await _db.Doctors.AsNoTracking().Where(x => x.Id == assignedDoctorId.Value && x.IsActive).Select(x => (Guid?)x.UserId).FirstOrDefaultAsync(ct)
            : appointment.DoctorUserId;
        if (!executingDoctorUserId.HasValue || !await HasDoctorAtFacilityAsync(executingDoctorUserId.Value, department.FacilityId, ct))
            return Preparation.Invalid("FACILITY_SCOPE_DENIED", "Bác sĩ được phân công không thuộc cơ sở tiếp nhận.");

        var version = Hash($"{AppointmentVersion(appointment.Id, appointment.DoctorId, appointment.PatientId, appointment.FacilityId.Value, appointment.AppointmentSlotId, appointment.AppointmentDate, appointment.StartTime, appointment.EndTime, appointment.Reason, appointment.Status)}|{department.Id}|{department.FacilityId}|{roomId}|{assignedDoctorId}|{executingDoctorUserId}");
        return new Preparation("appointment", appointmentId.ToString(), appointment.FacilityId.Value, version);
    }

    private async Task<Preparation> ResolveWalkInAsync(
        Guid actorId,
        long patientId,
        long departmentId,
        long? roomId,
        long? assignedDoctorId,
        CancellationToken ct)
    {
        var department = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == departmentId && x.IsActive, ct);
        var patient = await _db.Patients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == patientId, ct);
        if (department == null || patient == null || !await HasFacilityRoleAsync(actorId, AiActorRole.Receptionist, department.FacilityId, null, ct))
            return Preparation.Invalid("FACILITY_SCOPE_DENIED", "Không thể tiếp nhận hồ sơ ngoài cơ sở được phân quyền.");
        if (roomId.HasValue && !await _db.Rooms.AsNoTracking().AnyAsync(x => x.Id == roomId.Value && x.DepartmentId == department.Id && x.IsActive, ct))
            return Preparation.Invalid("RESOURCE_SCOPE_DENIED", "Phòng tiếp nhận không thuộc khoa đang được phân quyền.");
        if (assignedDoctorId.HasValue)
        {
            var doctorUserId = await _db.Doctors.AsNoTracking().Where(x => x.Id == assignedDoctorId.Value && x.IsActive).Select(x => (Guid?)x.UserId).FirstOrDefaultAsync(ct);
            if (!doctorUserId.HasValue || !await HasDoctorAtFacilityAsync(doctorUserId.Value, department.FacilityId, ct))
                return Preparation.Invalid("FACILITY_SCOPE_DENIED", "Bác sĩ được phân công không thuộc cơ sở tiếp nhận.");
        }
        return new Preparation("patient", patientId.ToString(), department.FacilityId,
            Hash($"{patient.Id}|{patient.MedicalRecordNumber}|{patient.PrimaryFacilityId}|{department.Id}|{department.FacilityId}|{roomId}|{assignedDoctorId}"));
    }

    private async Task<Preparation> ResolveDoctorClinicalResourceAsync(Guid actorId, JsonElement args, CancellationToken ct)
    {
        var appointmentId = GetLong(args, "appointmentId");
        var visitId = GetLong(args, "visitId");
        if (appointmentId.HasValue == visitId.HasValue)
            return Preparation.Invalid("INVALID_RESOURCE", "Cần chọn chính xác một appointmentId hoặc visitId.");
        if (visitId.HasValue)
        {
            var visit = await _db.PatientVisits.AsNoTracking().Include(x => x.AssignedDoctor).FirstOrDefaultAsync(x => x.Id == visitId.Value, ct);
            if (visit == null || visit.AssignedDoctor?.UserId != actorId || !await HasFacilityRoleAsync(actorId, AiActorRole.Doctor, visit.FacilityId, null, ct))
                return Preparation.Invalid("RESOURCE_SCOPE_DENIED", "Lượt khám không thuộc bác sĩ hoặc cơ sở hiện tại.");
            var visitPrescriptionVersion = await GetPrescriptionVersionAsync(visit.Id, visit.AppointmentId, ct);
            return new Preparation("visit", visit.Id.ToString(), visit.FacilityId, Hash($"{Version(visit.RowVersion)}|{visitPrescriptionVersion}"), visit.Id, null);
        }
        var appointment = await _db.Appointments.AsNoTracking().Include(x => x.Doctor).FirstOrDefaultAsync(x => x.Id == appointmentId!.Value, ct);
        if (appointment == null || !appointment.FacilityId.HasValue || appointment.Doctor.UserId != actorId)
            return Preparation.Invalid("RESOURCE_SCOPE_DENIED", "Lịch hẹn không thuộc bác sĩ hiện tại.");
        var departmentId = GetLong(args, "departmentId");
        var department = departmentId.HasValue
            ? await _db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == departmentId.Value && x.FacilityId == appointment.FacilityId.Value && x.IsActive && x.SpecialtyId == appointment.SpecialtyId, ct)
            : null;
        if (department == null || !await HasFacilityRoleAsync(actorId, AiActorRole.Doctor, department.FacilityId, department.Id, ct))
            return Preparation.Invalid("FACILITY_SCOPE_DENIED", "Lịch hẹn phải được ràng vào khoa và cơ sở bác sĩ đang được phân quyền.");
        var prescriptionVersion = await GetPrescriptionVersionAsync(null, appointment.Id, ct);
        return new Preparation("appointment", appointment.Id.ToString(), appointment.FacilityId.Value,
            Hash($"{AppointmentVersion(appointment.Id, appointment.DoctorId, appointment.PatientId, appointment.FacilityId.Value, appointment.AppointmentSlotId, appointment.AppointmentDate, appointment.StartTime, appointment.EndTime, appointment.Reason, appointment.Status)}|{department.Id}|{department.FacilityId}|{prescriptionVersion}"), null, appointment.Id);
    }

    private async Task<Preparation> ResolveTechnicianOrderAsync(Guid actorId, long orderId, CancellationToken ct)
    {
        var order = await _db.DiagnosticOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId, ct);
        var facilityId = order?.FacilityId ?? (order?.PatientVisitId.HasValue == true ? await _db.PatientVisits.AsNoTracking().Where(x => x.Id == order.PatientVisitId!.Value).Select(x => (long?)x.FacilityId).FirstOrDefaultAsync(ct) : null);
        if (order == null || !facilityId.HasValue || !await HasFacilityRoleAsync(actorId, AiActorRole.DiagnosticTechnician, facilityId.Value, order.PerformingDepartmentId, ct))
            return Preparation.Invalid("FACILITY_SCOPE_DENIED", "Phiếu chỉ định không thuộc phạm vi kỹ thuật viên hiện tại.");
        return new Preparation("diagnostic_order", order.Id.ToString(), facilityId.Value, Version(order.RowVersion), null, null, order.Status, order.StartedByUserId, order.CompletedByUserId, order.Id);
    }

    private async Task<Preparation> ResolveTechnicianResultAsync(Guid actorId, long orderId, long itemId, CancellationToken ct)
    {
        var order = await ResolveTechnicianOrderAsync(actorId, orderId, ct);
        if (!order.IsValid) return order;

        var item = await _db.DiagnosticOrderItems.AsNoTracking()
            .Include(x => x.Result)
            .FirstOrDefaultAsync(x => x.Id == itemId && x.DiagnosticOrderId == orderId, ct);
        if (item == null)
            return Preparation.Invalid("RESOURCE_SCOPE_DENIED", "Dịch vụ chỉ định không thuộc phiếu hiện tại.");

        return new Preparation(
            "diagnostic_order_item",
            $"{orderId}:{itemId}",
            order.FacilityId,
            Hash($"{order.Version}|{Version(item.RowVersion)}|{item.Status}|{Version(item.Result?.RowVersion)}"),
            null,
            null,
            order.Status,
            order.StartedByUserId,
            order.CompletedByUserId,
            order.Id);
    }

    private async Task<Preparation> ResolvePharmacyPrescriptionAsync(Guid actorId, long prescriptionId, CancellationToken ct)
    {
        var prescription = await _db.Prescriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == prescriptionId, ct);
        long? facilityId = null;
        if (prescription != null)
        {
            facilityId = await _db.PatientVisits.AsNoTracking()
                .Where(x => (prescription.PatientVisitId.HasValue && x.Id == prescription.PatientVisitId.Value) ||
                            (prescription.AppointmentId.HasValue && x.AppointmentId == prescription.AppointmentId.Value))
                .Select(x => (long?)x.FacilityId)
                .FirstOrDefaultAsync(ct);
        }
        if (prescription == null || !facilityId.HasValue || !await HasFacilityRoleAsync(actorId, AiActorRole.Pharmacist, facilityId.Value, null, ct))
            return Preparation.Invalid("FACILITY_SCOPE_DENIED", "Đơn thuốc không thuộc phạm vi dược hiện tại.");
        return new Preparation("prescription", prescription.Id.ToString(), facilityId.Value, Version(prescription.RowVersion), null, null, null, null, null, prescription.Id, prescription.Status, prescription.DispensedByUserId);
    }

    private static void EnsureVersion(AiPendingToolAction action, string current)
    {
        if (string.IsNullOrWhiteSpace(action.ResourceVersion) || !FixedEquals(action.ResourceVersion, current))
            throw new ConflictException("RESOURCE_VERSION_CHANGED", "Tài nguyên đã thay đổi sau khi chuẩn bị thao tác.");
    }

    private static void EnsureFacility(AiPendingToolAction action, long? currentFacilityId)
    {
        if (!action.FacilityId.HasValue || !currentFacilityId.HasValue || action.FacilityId != currentFacilityId)
            throw new ConflictException("FACILITY_SCOPE_CHANGED", "Tài nguyên không còn thuộc cơ sở đã được xác nhận.");
    }

    private void ValidateStoredAction(AiPendingToolAction action, AiToolExecutionContext context)
    {
        if (!TryGetActionRole(action.ToolName, out var toolRole) ||
            !string.Equals(action.ActorRole, toolRole.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(action.ToolVersion, "1.0", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("INVALID_PENDING_ACTION", "Pending action không có tool hoặc role hợp lệ.");
        }

        var preparationContext = new AiToolExecutionContext
        {
            ActorId = context.ActorId,
            IsAuthenticated = context.IsAuthenticated,
            Roles = context.Roles,
            SessionId = context.SessionId,
            FacilityId = context.FacilityId,
            InvocationChannel = AiToolInvocationChannel.DirectHumanPreparation,
            CorrelationId = context.CorrelationId
        };
        var validation = ValidateArguments(new AiToolInvocation
        {
            ToolName = action.ToolName,
            ToolVersion = action.ToolVersion,
            ArgumentsJson = action.NormalizedArgumentsJson,
            SessionId = action.SessionId,
            ConversationId = action.ConversationId
        }, preparationContext);
        if (!validation.IsValid)
            throw new BusinessException("INVALID_PENDING_ACTION", "Dữ liệu pending action không còn hợp lệ.");
    }

    private async Task<string> GetPrescriptionVersionAsync(long? visitId, long? appointmentId, CancellationToken ct)
    {
        var versions = await _db.Prescriptions.AsNoTracking()
            .Where(x => (visitId.HasValue && x.PatientVisitId == visitId.Value) ||
                        (appointmentId.HasValue && x.AppointmentId == appointmentId.Value))
            .OrderBy(x => x.Id)
            .Select(x => x.RowVersion)
            .ToListAsync(ct);
        return Hash(string.Join('|', versions.Select(Version)));
    }

    private async Task<bool> HasFacilityRoleAsync(Guid userId, AiActorRole role, long facilityId, long? departmentId, CancellationToken ct) =>
        await _db.StaffFacilityAssignments.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsActive && x.Role == role.ToString() && x.FacilityId == facilityId &&
            (!departmentId.HasValue || !x.DepartmentId.HasValue || x.DepartmentId == departmentId), ct);

    private Task<bool> HasDoctorAtFacilityAsync(Guid doctorUserId, long facilityId, CancellationToken ct) =>
        _db.StaffFacilityAssignments.AsNoTracking().AnyAsync(x => x.UserId == doctorUserId && x.IsActive && x.Role == nameof(AiActorRole.Doctor) && x.FacilityId == facilityId, ct);

    private async Task ExpireActiveActionsAsync(Guid userId, DateTime now, CancellationToken ct) =>
        await _db.AiPendingToolActions.Where(x => x.UserId == userId && x.ExpiresAtUtc <= now &&
                (x.State == AiPendingToolActionState.PendingConfirmation || x.State == AiPendingToolActionState.Executing || x.State == AiPendingToolActionState.FailedRetryable))
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.State, AiPendingToolActionState.Expired).SetProperty(a => a.ExecutionLeaseId, (Guid?)null).SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null), ct);

    private async Task TerminalizeAsync(Guid actionId, Guid? leaseId, AiPendingToolActionState state, string code, CancellationToken ct)
    {
        var query = _db.AiPendingToolActions.Where(x => x.ActionId == actionId);
        if (leaseId.HasValue) query = query.Where(x => x.ExecutionLeaseId == leaseId.Value && x.State == AiPendingToolActionState.Executing);
        await query.ExecuteUpdateAsync(x => x.SetProperty(a => a.State, state).SetProperty(a => a.LastErrorCode, code).SetProperty(a => a.ExecutionLeaseId, (Guid?)null).SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null), ct);
    }

    private static AiToolExecutionResult Pending(AiPendingToolAction action, string message, string? token = null) => new()
    {
        Status = "pending_confirmation",
        RequiresConfirmation = true,
        ActionId = action.ActionId.ToString(),
        ResultType = "pending_action",
        DisplayText = message,
        Data = new { actionId = action.ActionId, action.ToolName, action.ActorRole, action.ExpiresAtUtc, confirmationToken = token, confirmationEndpoint = $"/api/v1/ai/copilot/actions/{action.ActionId}/confirm" },
        DataSources = new[] { new AiToolDataSource("pending_action_store", "database") }
    };

    private static AiToolExecutionResult Completed(object data, string type, string message, bool isIdempotentReplay = false) => new()
    {
        Status = "completed", Data = data, ResultType = type, DisplayText = message,
        IsIdempotentReplay = isIdempotentReplay,
        DataSources = new[] { new AiToolDataSource("ClinicCare domain service", "service") }
    };

    private static AiToolArgumentValidationResult ValidateSchema(JsonElement root, IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> required, Func<JsonElement, AiToolArgumentValidationResult?> validate)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("facilityId", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("userId", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("actorId", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("resourceVersion", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("sourceAiActionId", StringComparison.OrdinalIgnoreCase) ||
                !allowed.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                return AiToolArgumentValidationResult.Invalid("FORBIDDEN_TOOL_ARGUMENT", "Phạm vi quyền và cơ sở luôn do server xác định.");
        }
        foreach (var name in required)
            if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", $"Thiếu tham số {name}.");
        return validate(root) ?? AiToolArgumentValidationResult.Valid();
    }

    private static AiToolArgumentValidationResult? PositiveIds(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var id) || id <= 0))
                return AiToolArgumentValidationResult.Invalid("INVALID_IDENTIFIER", $"{name} phải là số dương.");
        return null;
    }

    private static AiToolArgumentValidationResult? ValidateText(JsonElement root, string name, int min, int max)
    {
        var value = GetString(root, name);
        return value is null || value.Length < min || value.Length > max
            ? AiToolArgumentValidationResult.Invalid("INVALID_TEXT", $"{name} không hợp lệ.") : null;
    }

    private static AiToolArgumentValidationResult? ValidateDiagnosticOrder(JsonElement root)
    {
        var appointmentId = GetLong(root, "appointmentId");
        var visitId = GetLong(root, "visitId");
        if (appointmentId.HasValue == visitId.HasValue ||
            PositiveIds(root, "appointmentId", "visitId", "departmentId") is { } identifierError)
            return AiToolArgumentValidationResult.Invalid("INVALID_RESOURCE", "Cần đúng một appointmentId hoặc visitId.");
        if (appointmentId.HasValue && !GetLong(root, "departmentId").HasValue)
            return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Lịch hẹn cần departmentId để xác minh cơ sở.");
        if (ValidateText(root, "clinicalIndication", 1, 1000) is { } textError) return textError;
        if (!root.TryGetProperty("serviceIds", out var rawServices) || rawServices.ValueKind != JsonValueKind.Array ||
            rawServices.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number || !x.TryGetInt64(out _)))
            return AiToolArgumentValidationResult.Invalid("INVALID_SERVICE_IDS", "Danh sách dịch vụ không hợp lệ.");
        var services = GetLongArray(root, "serviceIds");
        return services.Count is < 1 or > 20 || services.Any(x => x <= 0)
            ? AiToolArgumentValidationResult.Invalid("INVALID_SERVICE_IDS", "Cần từ 1 đến 20 dịch vụ hợp lệ.") : null;
    }

    private static AiToolArgumentValidationResult? ValidatePrescriptionDraft(JsonElement root)
    {
        var appointmentId = GetLong(root, "appointmentId");
        var visitId = GetLong(root, "visitId");
        if (appointmentId.HasValue == visitId.HasValue ||
            PositiveIds(root, "appointmentId", "visitId", "departmentId") is { } identifierError)
            return AiToolArgumentValidationResult.Invalid("INVALID_RESOURCE", "Cần đúng một appointmentId hoặc visitId.");
        if (appointmentId.HasValue && !GetLong(root, "departmentId").HasValue)
            return AiToolArgumentValidationResult.Invalid("MISSING_TOOL_ARGUMENT", "Lịch hẹn cần departmentId để xác minh cơ sở.");
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 50)
            return AiToolArgumentValidationResult.Invalid("INVALID_PRESCRIPTION_ITEMS", "Cần từ 1 đến 50 dòng thuốc.");
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.EnumerateObject().Any(x => x.Name is not ("medicineId" or "quantity" or "dosage" or "frequency" or "durationDays" or "instructions")))
                return AiToolArgumentValidationResult.Invalid("FORBIDDEN_TOOL_ARGUMENT", "Dòng thuốc có trường không được hỗ trợ.");
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("medicineId", out var medicine) || !medicine.TryGetInt64(out var medId) || medId <= 0 ||
                !item.TryGetProperty("quantity", out var quantity) || !quantity.TryGetInt32(out var count) || count is < 1 or > 1000)
                return AiToolArgumentValidationResult.Invalid("INVALID_PRESCRIPTION_ITEMS", "Dòng thuốc không hợp lệ.");
        }
        return null;
    }

    private static List<SavePrescriptionItemRequest> ParsePrescriptionItems(JsonElement root) => root.GetProperty("items").EnumerateArray().Select(item => new SavePrescriptionItemRequest
    {
        MedicineId = item.GetProperty("medicineId").GetInt64(),
        Quantity = item.GetProperty("quantity").GetInt32(),
        Dosage = GetString(item, "dosage"),
        Frequency = GetString(item, "frequency"),
        DurationDays = GetInt(item, "durationDays"),
        Instructions = GetString(item, "instructions")
    }).ToList();

    private static bool TryParseObject(string? json, out JsonDocument document, out AiToolArgumentValidationResult? error)
    {
        document = null!;
        error = null;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return true;
            document.Dispose();
        }
        catch (JsonException) { }
        error = AiToolArgumentValidationResult.Invalid("INVALID_TOOL_ARGUMENTS", "Tham số phải là JSON object hợp lệ.");
        return false;
    }

    private static string Canonical(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static bool TryGetActionRole(string tool, out AiActorRole role)
    {
        if (tool.StartsWith("reception.", StringComparison.Ordinal))
        {
            role = AiActorRole.Receptionist;
            return true;
        }
        if (tool.StartsWith("doctor.", StringComparison.Ordinal))
        {
            role = AiActorRole.Doctor;
            return true;
        }
        if (tool.StartsWith("technician.", StringComparison.Ordinal))
        {
            role = AiActorRole.DiagnosticTechnician;
            return true;
        }
        if (tool.StartsWith("pharmacist.", StringComparison.Ordinal))
        {
            role = AiActorRole.Pharmacist;
            return true;
        }
        role = default;
        return false;
    }
    private static bool TryGuid(JsonElement root, string name) => TryGuid(root, name, out _);
    private static bool TryGuid(JsonElement root, string name, out Guid id)
    {
        id = Guid.Empty;
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out id);
    }
    private static bool IsTrue(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string? GetString(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
    private static long? GetLong(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : null;
    private static int? GetInt(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;
    private static List<long> GetLongArray(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out _)).Select(x => x.GetInt64()).Distinct().ToList() : new();
    private static AiToolArgumentValidationResult? ValidatePriority(JsonElement root)
    {
        if (!root.TryGetProperty("priority", out var value)) return null;
        return value.ValueKind == JsonValueKind.String && Enum.TryParse<VisitPriority>(value.GetString(), true, out _)
            ? null
            : AiToolArgumentValidationResult.Invalid("INVALID_PRIORITY", "Priority không hợp lệ.");
    }
    private static VisitPriority GetPriority(JsonElement root) =>
        root.TryGetProperty("priority", out var value) && value.ValueKind == JsonValueKind.String && Enum.TryParse<VisitPriority>(value.GetString(), true, out var priority)
            ? priority
            : VisitPriority.Normal;
    private static string Canonicalize(JsonElement root) => JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = false });
    private static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    private static string Version(byte[]? value) => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;
    private static string AppointmentVersion(long id, long doctorId, long patientId, long facilityId, long slotId, DateOnly date, TimeOnly startTime, TimeOnly endTime, string? reason, AppointmentStatus status) => Hash($"{id}|{doctorId}|{patientId}|{facilityId}|{slotId}|{date:yyyy-MM-dd}|{startTime:HH:mm:ss}|{endTime:HH:mm:ss}|{reason?.Trim()}|{status}");
    private static string CreateConfirmationToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool IsConfirmationToken(string? token) => !string.IsNullOrWhiteSpace(token) && token.Length == 43 && token.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    private static string ConfirmationBindingHash(AiPendingToolAction action, string token) => Hash(string.Join('|', new[]
    {
        token,
        action.ActionId.ToString("N"),
        action.UserId.ToString("N"),
        action.ActorRole,
        action.SessionId,
        action.ConversationId ?? string.Empty,
        action.FacilityId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        action.ToolName,
        action.ToolVersion,
        action.RequestHash,
        action.ResourceType,
        action.ResourceId,
        action.ResourceVersion ?? string.Empty,
        Hash(action.NormalizedArgumentsJson),
        action.SourceAiActionId?.ToString("N") ?? string.Empty,
        action.IdempotencyKeyHash ?? string.Empty,
        // EF providers do not consistently round-trip DateTime.Kind. The
        // persisted value is an instant stored in UTC, so preserve its ticks
        // rather than applying the local machine offset to an Unspecified
        // value during confirmation.
        new DateTimeOffset(DateTime.SpecifyKind(action.ExpiresAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
    }));
    private static bool VerifyConfirmationToken(AiPendingToolAction action, string? supplied) =>
        !string.IsNullOrWhiteSpace(action.ConfirmationTokenHash) &&
        IsConfirmationToken(supplied) &&
        FixedEquals(action.ConfirmationTokenHash, ConfirmationBindingHash(action, supplied!));
    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
    private static bool IsValidSessionId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length is >= 6 and <= 128 && value.StartsWith("sess_", StringComparison.Ordinal) && value[5..].All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    private static string? NormalizeId(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 128 ? value.Trim() : null;

    private sealed record Preparation(
        string ResourceType,
        string ResourceId,
        long? FacilityId,
        string Version,
        long? VisitId = null,
        long? AppointmentId = null,
        DiagnosticOrderStatus? Status = null,
        Guid? StartedByUserId = null,
        Guid? CompletedByUserId = null,
        long? OrderId = null,
        PrescriptionStatus? PrescriptionStatus = null,
        Guid? DispensedByUserId = null,
        string? ErrorCode = null,
        string? ErrorMessage = null)
    {
        public bool IsValid => ErrorCode is null;
        public long Id => OrderId ?? long.Parse(ResourceId, System.Globalization.CultureInfo.InvariantCulture);
        public static Preparation Invalid(string code, string message) => new("", "", null, "", ErrorCode: code, ErrorMessage: message);
    }

    private sealed record ExecutionResult(string Reference, string ResultType, string Message, bool Recovered = false);
}
