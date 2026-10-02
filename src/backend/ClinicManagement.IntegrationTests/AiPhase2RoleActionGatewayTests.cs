using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiPhase2RoleActionGatewayTests : IntegrationTestBase
{
    private static int _sequence;

    public AiPhase2RoleActionGatewayTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Role_action_catalog_is_direct_only_and_the_planner_cannot_run_a_mixed_write_plan()
    {
        var definitions = AiRoleActionCatalog.Definitions;
        Assert.Contains(definitions, x => x.Name == "reception.prepare_check_in_appointment");
        Assert.Contains(definitions, x => x.Name == "doctor.prepare_diagnostic_order");
        Assert.Contains(definitions, x => x.Name == "technician.prepare_record_diagnostic_result");
        Assert.Contains(definitions, x => x.Name == "pharmacist.prepare_reserve_prescription");
        Assert.Contains(definitions, x => x.Name == "pharmacist.prepare_dispense_prescription");
        Assert.Contains(definitions, x => x.Name == "role.execute_confirmed_action");
        Assert.All(definitions, definition =>
        {
            Assert.Equal(AiToolRiskLevel.High, definition.RiskLevel);
            Assert.Equal(AiToolConfirmationRequirement.ExplicitUserConfirmation, definition.Confirmation);
            Assert.False(AiPlannerPolicy.IsAllowed(definition.Name));
        });

        var appointment = await CreateAppointmentAsync();
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var generic = await client.PostAsJsonAsync("/api/v1/ai/tools/execute", new
        {
            toolName = "reception.prepare_check_in_appointment",
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(new { appointmentId = appointment.AppointmentId, departmentId = appointment.DepartmentId }),
            sessionId = Session("planner")
        });

        Assert.Equal(HttpStatusCode.BadRequest, generic.StatusCode);
        Assert.Equal("PLANNER_TOOL_NOT_ALLOWED", (await generic.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);
        await AssertNoVisitAsync(appointment.AppointmentId);

        await using var scope = Factory.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IAiToolExecutor>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.AiAuditLogs.CountAsync(x => x.ActionType == "Tool:clinic.get_facilities" && x.SessionId == "sess_role_planner_preflight");
        var mixedPlan = await executor.ExecutePlannerPlanAsync(new[]
        {
            new AiPlannerToolCall { Name = "clinic.get_facilities", Version = "1.0", Arguments = JsonDocument.Parse("{}").RootElement.Clone() },
            new AiPlannerToolCall { Name = "reception.prepare_check_in_appointment", Version = "1.0", Arguments = JsonDocument.Parse($"{{\"appointmentId\":{appointment.AppointmentId},\"departmentId\":{appointment.DepartmentId}}}").RootElement.Clone() },
            new AiPlannerToolCall { Name = "role.execute_confirmed_action", Version = "1.0", Arguments = JsonDocument.Parse("{}").RootElement.Clone() }
        }, "sess_role_planner_preflight");

        Assert.Equal("PLANNER_TOOL_NOT_ALLOWED", Assert.Single(mixedPlan).Error?.Code);
        var after = await db.AiAuditLogs.CountAsync(x => x.ActionType == "Tool:clinic.get_facilities" && x.SessionId == "sess_role_planner_preflight");
        Assert.Equal(before, after);
        await AssertNoVisitAsync(appointment.AppointmentId);
    }

    [Fact]
    public async Task Walk_in_and_prescription_previews_are_grounded_in_persisted_scope_and_changes()
    {
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        long departmentId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            departmentId = await db.Departments.Where(x => x.IsActive && x.SpecialtyId == SpecialtyEntityId).Select(x => x.Id).FirstAsync();
        }

        var walkIn = await PrepareAsync(receptionist, "reception.prepare_create_walk_in", new
        {
            existingPatientId = Patient2EntityId,
            departmentId,
            chiefComplaint = "Triệu chứng tổng hợp chỉ dùng trong fixture, không được đưa vào preview",
            priority = "Normal"
        }, "walk-in-preview");
        Assert.Contains("walk_in_intake", walkIn.Preview.Changes.Select(change => change.Kind));
        Assert.Contains($"Bệnh nhân mã #{Patient2EntityId}", walkIn.Preview.Resource.Subject);
        Assert.DoesNotContain("Triệu chứng tổng hợp", JsonSerializer.Serialize(walkIn.Preview), StringComparison.Ordinal);
        await MutateActionAsync(walkIn.ActionId, action => action.State = AiPendingToolActionState.Cancelled);

        var visit = await CreateVisitAsync(VisitStatus.WaitingForDoctor);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var prescription = await PrepareAsync(doctor, "doctor.prepare_prescription_draft", new
        {
            visitId = visit.VisitId,
            notes = "Bản nháp synthetic cho acceptance preview",
            items = new[] { new { medicineId = MedicineEntityId, quantity = 2, dosage = "500mg", frequency = "Ngày 2 lần" } }
        }, "prescription-preview");
        Assert.Contains("prescription_draft", prescription.Preview.Changes.Select(change => change.Kind));
        var prescriptionItem = Assert.Single(prescription.Preview.Changes.SelectMany(change => change.Items), item => item.Label.StartsWith("Thuốc ", StringComparison.Ordinal));
        Assert.Equal(2, prescriptionItem.Quantity);
        Assert.Contains("Bệnh nhân mã #", prescription.Preview.Resource.Subject);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(doctor, prescription)).StatusCode);

        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await verifyDb.Prescriptions.CountAsync(x => x.PatientVisitId == visit.VisitId));
    }

    [Fact]
    public async Task Cross_actor_schedule_reception_doctor_technician_pharmacist_admin_uses_the_real_workflow_boundary()
    {
        var appointment = await CreateAppointmentAsync();
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        var checkIn = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment", new
        {
            appointmentId = appointment.AppointmentId,
            departmentId = appointment.DepartmentId
        }, "cross-actor-checkin");
        Assert.True((await ConfirmAsync(receptionist, checkIn)).IsSuccessStatusCode);

        long visitId;
        long diagnosticServiceId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            visitId = await db.PatientVisits.Where(x => x.AppointmentId == appointment.AppointmentId).Select(x => x.Id).SingleAsync();
            diagnosticServiceId = await db.DiagnosticServices.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        }

        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var doctorOrder = await PrepareAsync(doctor, "doctor.prepare_diagnostic_order", new
        {
            visitId,
            clinicalIndication = "Chỉ định cận lâm sàng trong workflow liên actor synthetic",
            serviceIds = new[] { diagnosticServiceId }
        }, "cross-actor-doctor");
        Assert.True((await ConfirmAsync(doctor, doctorOrder)).IsSuccessStatusCode);

        long orderId;
        long itemId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await db.DiagnosticOrders.Include(x => x.Items).SingleAsync(x => x.SourceAiActionId == doctorOrder.ActionId);
            orderId = order.Id;
            itemId = Assert.Single(order.Items).Id;
        }

        var technician = await CreateAuthenticatedClientAsync("tech@test.com");
        var start = await PrepareAsync(technician, "technician.prepare_start_diagnostic_order", new { orderId }, "cross-actor-start");
        Assert.True((await ConfirmAsync(technician, start)).IsSuccessStatusCode);
        var result = await PrepareAsync(technician, "technician.prepare_record_diagnostic_result", new
        {
            orderId,
            itemId,
            resultText = "Kết quả synthetic của fixture workflow",
            conclusion = "Đã hoàn tất để bác sĩ review"
        }, "cross-actor-result");
        Assert.True((await ConfirmAsync(technician, result)).IsSuccessStatusCode);
        var complete = await PrepareAsync(technician, "technician.prepare_complete_diagnostic_order", new { orderId }, "cross-actor-complete");
        Assert.True((await ConfirmAsync(technician, complete)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{orderId}/review", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await doctor.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/start-consultation", new { })).StatusCode);
        var consultation = await doctor.PostAsJsonAsync($"/api/v1/doctor/visits/{visitId}/complete", new
        {
            clinicalFindings = "Fixture clinical findings for integration workflow",
            diagnosis = "Fixture diagnosis for integration workflow",
            summary = "Fixture consultation completed through the domain workflow",
            issuePrescription = true,
            prescriptionNotes = "Prescription issued by the real consultation completion path",
            prescriptionItems = new[] { new { medicineId = MedicineEntityId, quantity = 1, dosage = "500mg", frequency = "Ngày 1 lần" } }
        });
        Assert.Equal(HttpStatusCode.OK, consultation.StatusCode);

        long prescriptionId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            prescriptionId = await db.Prescriptions.Where(x => x.PatientVisitId == visitId).Select(x => x.Id).SingleAsync();
            Assert.Equal(PrescriptionStatus.Issued, await db.Prescriptions.Where(x => x.Id == prescriptionId).Select(x => x.Status).SingleAsync());
        }

        var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var reserve = await PrepareAsync(pharmacist, "pharmacist.prepare_reserve_prescription", new { prescriptionId }, "cross-actor-pharmacy");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(pharmacist, reserve)).StatusCode);

        var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var metrics = await ExecuteReadAsync(admin, "admin.get_dashboard_metrics", new { }, "cross-actor-admin");
        Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
        Assert.Equal("completed", (await metrics.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Status);
    }

    [Fact]
    public async Task Reception_prepare_then_confirm_creates_one_visit_and_one_cross_actor_history_notification_and_audit()
    {
        var appointment = await CreateAppointmentAsync();
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        var pending = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment", new
        {
            appointmentId = appointment.AppointmentId,
            departmentId = appointment.DepartmentId
        }, "reception-success");
        Assert.Contains(appointment.AppointmentCode, pending.Preview.Resource.Identity);
        Assert.Contains("check_in", pending.Preview.Changes.Select(change => change.Kind));

        await AssertNoVisitAsync(appointment.AppointmentId);

        var confirmed = await ConfirmAsync(receptionist, pending);
        Assert.True(confirmed.StatusCode == HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        Assert.Equal("completed", (await confirmed.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Status);

        var replay = await ConfirmAsync(receptionist, pending);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        // Simulate a process crash after the domain transaction has committed
        // but before the pending-action row was marked completed.
        await MutateActionAsync(pending.ActionId, action =>
        {
            action.State = AiPendingToolActionState.FailedRetryable;
            action.ExecutedAtUtc = null;
            action.ExecutionResultReference = null;
            action.LastErrorCode = "SIMULATED_CRASH_AFTER_DOMAIN_COMMIT";
        });
        var crashReplay = await ConfirmAsync(receptionist, pending);
        Assert.Equal(HttpStatusCode.OK, crashReplay.StatusCode);
        Assert.True((await crashReplay.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.IsIdempotentReplay);

        long visitId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = await db.PatientVisits.SingleAsync(x => x.AppointmentId == appointment.AppointmentId);
            visitId = visit.Id;
            Assert.Equal(appointment.FacilityId, visit.FacilityId);
            Assert.Equal(DoctorEntityId, visit.AssignedDoctorId);
            Assert.Equal(1, await db.AppointmentHistories.CountAsync(x => x.AppointmentId == appointment.AppointmentId && x.Action == AppointmentHistoryAction.CheckedIn));
            Assert.Equal(1, await db.Notifications.CountAsync(x => x.DedupeKey == $"checkin_appointment_{appointment.AppointmentId}"));
            Assert.Equal(1, await db.Notifications.CountAsync(x => x.DedupeKey == $"appt_checkin_doc_{appointment.AppointmentId}_{DoctorId}"));
            Assert.Equal(1, await db.AiAuditLogs.CountAsync(x => x.ActionType == "Tool:role.execute_confirmed_action" && x.SessionId == pending.SessionId && x.Outcome == "completed"));
            Assert.Equal(AiPendingToolActionState.Completed, (await db.AiPendingToolActions.SingleAsync(x => x.ActionId == pending.ActionId)).State);
        }

        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var queue = await doctor.GetAsync("/api/v1/doctor/queue");
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        Assert.Contains(visitId.ToString(), await queue.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_confirmation_rejects_wrong_identity_session_token_and_bound_action_fields_without_execution()
    {
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");

        var identityAppointment = await CreateAppointmentAsync();
        var identityAction = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment", new { appointmentId = identityAppointment.AppointmentId, departmentId = identityAppointment.DepartmentId }, "identity");
        var wrongSession = await receptionist.PostAsJsonAsync($"/api/v1/ai/copilot/actions/{identityAction.ActionId}/confirm", new { sessionId = Session("other"), concurrencyToken = identityAction.Token });
        Assert.Equal(HttpStatusCode.NotFound, wrongSession.StatusCode);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var wrongUser = await ConfirmAsync(doctor, identityAction);
        Assert.Equal(HttpStatusCode.NotFound, wrongUser.StatusCode);
        var wrongToken = await receptionist.PostAsJsonAsync($"/api/v1/ai/copilot/actions/{identityAction.ActionId}/confirm", new { sessionId = identityAction.SessionId, concurrencyToken = new string('A', 43) });
        Assert.Equal(HttpStatusCode.Conflict, wrongToken.StatusCode);
        Assert.Equal("CONCURRENCY_CONFLICT", (await wrongToken.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(AiPendingToolActionState.PendingConfirmation, (await db.AiPendingToolActions.SingleAsync(x => x.ActionId == identityAction.ActionId)).State);
        }
        await AssertNoVisitAsync(identityAppointment.AppointmentId);
        var correctToken = await ConfirmAsync(receptionist, identityAction);
        Assert.Equal(HttpStatusCode.OK, correctToken.StatusCode);
        Assert.Equal("completed", (await correctToken.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Status);

        foreach (var mutation in new Action<AiPendingToolAction>[]
        {
            action => action.ResourceId = "999999",
            action => action.ResourceVersion = "stale-version",
            action => action.RequestHash = "different-request-hash",
            action => action.ExpiresAtUtc = action.ExpiresAtUtc.AddMinutes(1)
        })
        {
            var appointment = await CreateAppointmentAsync();
            var pending = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment", new { appointmentId = appointment.AppointmentId, departmentId = appointment.DepartmentId }, "binding");
            await MutateActionAsync(pending.ActionId, mutation);
            var response = await ConfirmAsync(receptionist, pending);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("CONCURRENCY_CONFLICT", (await response.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);
            await AssertNoVisitAsync(appointment.AppointmentId);
        }
    }

    [Fact]
    public async Task Role_prepare_retry_recovers_only_the_latest_token_and_keeps_confirmation_side_effects_single()
    {
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        var appointment = await CreateAppointmentAsync();
        var sessionId = Session("lost-response");
        var idempotencyKey = $"idem-recovery-{Guid.NewGuid():N}";
        var arguments = new { appointmentId = appointment.AppointmentId, departmentId = appointment.DepartmentId };

        // The first response represents the response lost by the client.
        var first = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment", arguments, "lost-response", idempotencyKey, sessionId);
        var recovered = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment", arguments, "lost-response", idempotencyKey, sessionId);
        Assert.Equal(first.ActionId, recovered.ActionId);
        Assert.NotEqual(first.Token, recovered.Token);

        var wrongSession = await receptionist.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "reception.prepare_check_in_appointment",
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(arguments),
            sessionId = Session("wrong-recovery-session"),
            conversationId = $"conv_{Guid.NewGuid():N}",
            idempotencyKey
        });
        Assert.Equal(HttpStatusCode.NotFound, wrongSession.StatusCode);

        var wrongPayload = await receptionist.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "reception.prepare_check_in_appointment",
            toolVersion = "1.0",
            argumentsJson = $"{{\"departmentId\":{appointment.DepartmentId},\"appointmentId\":{appointment.AppointmentId}}}",
            sessionId,
            conversationId = $"conv_{Guid.NewGuid():N}",
            idempotencyKey
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPayload.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", (await wrongPayload.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);

        var oldTokenResponse = await ConfirmAsync(receptionist, first);
        Assert.Equal(HttpStatusCode.Conflict, oldTokenResponse.StatusCode);
        Assert.Equal("CONCURRENCY_CONFLICT", (await oldTokenResponse.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(receptionist, recovered)).StatusCode);
        await AssertSingleVisitAsync(appointment.AppointmentId);

        var completedRetry = await receptionist.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "reception.prepare_check_in_appointment",
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(arguments),
            sessionId,
            conversationId = $"conv_{Guid.NewGuid():N}",
            idempotencyKey
        });
        Assert.Equal(HttpStatusCode.BadRequest, completedRetry.StatusCode);
        Assert.Equal("ACTION_ALREADY_COMPLETED", (await completedRetry.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);

        var concurrentAppointment = await CreateAppointmentAsync();
        var concurrentSession = Session("concurrent-recovery");
        var concurrentKey = $"idem-concurrent-{Guid.NewGuid():N}";
        var seed = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment",
            new { appointmentId = concurrentAppointment.AppointmentId, departmentId = concurrentAppointment.DepartmentId },
            "concurrent-recovery", concurrentKey, concurrentSession);
        var clientA = await CreateAuthenticatedClientAsync("rec@test.com");
        var clientB = await CreateAuthenticatedClientAsync("rec@test.com");
        var retries = await Task.WhenAll(
            Task.Run(() => PrepareAsync(clientA, "reception.prepare_check_in_appointment",
                new { appointmentId = concurrentAppointment.AppointmentId, departmentId = concurrentAppointment.DepartmentId },
                "concurrent-recovery", concurrentKey, concurrentSession)),
            Task.Run(() => PrepareAsync(clientB, "reception.prepare_check_in_appointment",
                new { appointmentId = concurrentAppointment.AppointmentId, departmentId = concurrentAppointment.DepartmentId },
                "concurrent-recovery", concurrentKey, concurrentSession)));
        Assert.All(retries, retry => Assert.Equal(seed.ActionId, retry.ActionId));
        Assert.Equal(2, retries.Select(x => x.Token).Distinct(StringComparer.Ordinal).Count());

        var concurrentConfirmations = await Task.WhenAll(
            ConfirmAsync(clientA, retries[0]),
            ConfirmAsync(clientB, retries[1]));
        Assert.Contains(concurrentConfirmations, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(concurrentConfirmations, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertSingleVisitAsync(concurrentAppointment.AppointmentId);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.AppointmentHistories.CountAsync(x => x.AppointmentId == concurrentAppointment.AppointmentId && x.Action == AppointmentHistoryAction.CheckedIn));
            Assert.Equal(1, await db.Notifications.CountAsync(x => x.DedupeKey == $"checkin_appointment_{concurrentAppointment.AppointmentId}"));
            Assert.Equal(1, await db.Notifications.CountAsync(x => x.DedupeKey == $"appt_checkin_doc_{concurrentAppointment.AppointmentId}_{DoctorId}"));
            Assert.Equal(1, await db.AiAuditLogs.CountAsync(x => x.ActionType == "Tool:role.execute_confirmed_action" && x.SessionId == concurrentSession && x.Outcome == "completed"));
        }

        var wrongUser = await (await CreateAuthenticatedClientAsync("doc@test.com")).PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "reception.prepare_check_in_appointment",
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(arguments),
            sessionId,
            conversationId = $"conv_{Guid.NewGuid():N}",
            idempotencyKey = $"wrong-user-{Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.Forbidden, wrongUser.StatusCode);
    }

    [Fact]
    public async Task Role_prepare_retry_does_not_return_a_null_token_for_terminal_or_executing_actions()
    {
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        var cases = new[]
        {
            ("expired", AiPendingToolActionState.Expired, HttpStatusCode.Gone, "ACTION_EXPIRED"),
            ("cancelled", AiPendingToolActionState.Cancelled, HttpStatusCode.Gone, "ACTION_CANCELLED"),
            ("executing", AiPendingToolActionState.Executing, HttpStatusCode.Conflict, "ACTION_IN_PROGRESS"),
            ("failed-terminal", AiPendingToolActionState.FailedTerminal, HttpStatusCode.BadRequest, "ACTION_FAILED_TERMINAL")
        };

        foreach (var testCase in cases)
        {
            var appointment = await CreateAppointmentAsync();
            var sessionId = Session($"retry-state-{testCase.Item1}");
            var idempotencyKey = $"idem-retry-state-{testCase.Item1}-{Guid.NewGuid():N}";
            var arguments = new { appointmentId = appointment.AppointmentId, departmentId = appointment.DepartmentId };
            var pending = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment", arguments, testCase.Item1, idempotencyKey, sessionId);
            await MutateActionAsync(pending.ActionId, action =>
            {
                action.State = testCase.Item2;
                if (testCase.Item2 == AiPendingToolActionState.Cancelled)
                    action.CancelledAtUtc = DateTime.UtcNow;
                if (testCase.Item2 == AiPendingToolActionState.Executing)
                {
                    action.ExecutionLeaseId = Guid.NewGuid();
                    action.ExecutionLeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
                }
            });

            var response = await receptionist.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
            {
                toolName = "reception.prepare_check_in_appointment",
                toolVersion = "1.0",
                argumentsJson = JsonSerializer.Serialize(arguments),
                sessionId,
                conversationId = $"conv_{Guid.NewGuid():N}",
                idempotencyKey
            });
            Assert.Equal(testCase.Item3, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AiToolExecutionResult>();
            Assert.Equal(testCase.Item4, body?.Error?.Code);
            Assert.NotEqual("pending_confirmation", body?.Status);
        }
    }

    [Fact]
    public async Task Cancelled_role_action_cannot_be_confirmed_and_same_request_can_be_prepared_again()
    {
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        var (appointment, first) = await PrepareReceptionActionAsync(receptionist, "cancel-reprepare");

        var cancel = await receptionist.PostAsJsonAsync($"/api/v1/ai/copilot/actions/{first.ActionId}/cancel", new { sessionId = first.SessionId });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var confirm = await ConfirmAsync(receptionist, first);
        Assert.Equal(HttpStatusCode.Gone, confirm.StatusCode);
        Assert.Equal("ACTION_CANCELLED", (await confirm.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);
        await AssertNoVisitAsync(appointment.AppointmentId);

        var second = await PrepareAsync(receptionist, "reception.prepare_check_in_appointment",
            new { appointmentId = appointment.AppointmentId, departmentId = appointment.DepartmentId }, "cancel-reprepare", sessionId: first.SessionId);
        Assert.NotEqual(first.ActionId, second.ActionId);
        await AssertNoVisitAsync(appointment.AppointmentId);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(receptionist, second)).StatusCode);
        await AssertSingleVisitAsync(appointment.AppointmentId);
    }

    [Fact]
    public async Task Role_pending_state_machine_handles_expired_cancelled_lease_retry_and_corrupt_payload_fail_closed()
    {
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");

        var expired = await PrepareReceptionActionAsync(receptionist, "expired");
        await MutateActionAsync(expired.Pending.ActionId, action => action.State = AiPendingToolActionState.Expired);
        var expiredResponse = await ConfirmAsync(receptionist, expired.Pending);
        Assert.Equal(HttpStatusCode.Gone, expiredResponse.StatusCode);
        await AssertNoVisitAsync(expired.Appointment.AppointmentId);

        var cancelled = await PrepareReceptionActionAsync(receptionist, "cancelled");
        await MutateActionAsync(cancelled.Pending.ActionId, action =>
        {
            action.State = AiPendingToolActionState.Cancelled;
            action.CancelledAtUtc = DateTime.UtcNow;
        });
        var cancelledResponse = await ConfirmAsync(receptionist, cancelled.Pending);
        Assert.Equal(HttpStatusCode.Gone, cancelledResponse.StatusCode);
        await AssertNoVisitAsync(cancelled.Appointment.AppointmentId);

        var activeLease = await PrepareReceptionActionAsync(receptionist, "active-lease");
        await MutateActionAsync(activeLease.Pending.ActionId, action =>
        {
            action.State = AiPendingToolActionState.Executing;
            action.ExecutionLeaseId = Guid.NewGuid();
            action.ExecutionLeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
        });
        var activeResponse = await ConfirmAsync(receptionist, activeLease.Pending);
        Assert.Equal(HttpStatusCode.Conflict, activeResponse.StatusCode);
        Assert.Equal("ACTION_IN_PROGRESS", (await activeResponse.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);
        await AssertNoVisitAsync(activeLease.Appointment.AppointmentId);

        var reclaim = await PrepareReceptionActionAsync(receptionist, "reclaim");
        await MutateActionAsync(reclaim.Pending.ActionId, action =>
        {
            action.State = AiPendingToolActionState.Executing;
            action.ExecutionLeaseId = Guid.NewGuid();
            action.ExecutionLeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        });
        var clientA = await CreateAuthenticatedClientAsync("rec@test.com");
        var clientB = await CreateAuthenticatedClientAsync("rec@test.com");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationTasks = new[]
        {
            Task.Run(async () => { await gate.Task; return await ConfirmAsync(clientA, reclaim.Pending); }),
            Task.Run(async () => { await gate.Task; return await ConfirmAsync(clientB, reclaim.Pending); })
        };
        gate.TrySetResult();
        var confirmations = await Task.WhenAll(confirmationTasks);
        Assert.Contains(confirmations, x => x.StatusCode == HttpStatusCode.OK);
        Assert.DoesNotContain(confirmations, x => x.StatusCode == HttpStatusCode.InternalServerError);
        await AssertSingleVisitAsync(reclaim.Appointment.AppointmentId);
        await using (var reclaimScope = Factory.Services.CreateAsyncScope())
        {
            var reclaimDb = reclaimScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await reclaimDb.AppointmentHistories.CountAsync(x => x.AppointmentId == reclaim.Appointment.AppointmentId && x.Action == AppointmentHistoryAction.CheckedIn));
            Assert.Equal(1, await reclaimDb.Notifications.CountAsync(x => x.DedupeKey == $"checkin_appointment_{reclaim.Appointment.AppointmentId}"));
            Assert.Equal(1, await reclaimDb.Notifications.CountAsync(x => x.DedupeKey == $"appt_checkin_doc_{reclaim.Appointment.AppointmentId}_{DoctorId}"));
            Assert.Equal(1, await reclaimDb.AiAuditLogs.CountAsync(x => x.ActionType == "Tool:role.execute_confirmed_action" && x.SessionId == reclaim.Pending.SessionId && x.Outcome == "completed"));
        }

        var retry = await PrepareReceptionActionAsync(receptionist, "retry");
        await MutateActionAsync(retry.Pending.ActionId, action =>
        {
            action.State = AiPendingToolActionState.FailedRetryable;
            action.LastErrorCode = "SIMULATED_TRANSIENT_FAILURE";
        });
        var retryResponse = await ConfirmAsync(receptionist, retry.Pending);
        Assert.True(retryResponse.StatusCode == HttpStatusCode.OK, await retryResponse.Content.ReadAsStringAsync());
        await AssertSingleVisitAsync(retry.Appointment.AppointmentId);

        var corrupt = await PrepareReceptionActionAsync(receptionist, "corrupt");
        await MutateActionAsync(corrupt.Pending.ActionId, action => action.NormalizedArgumentsJson = "{\"appointmentId\":\"bad\"}");
        var corruptResponse = await ConfirmAsync(receptionist, corrupt.Pending);
        Assert.Equal(HttpStatusCode.BadRequest, corruptResponse.StatusCode);
        Assert.Equal("INVALID_PENDING_ACTION", (await corruptResponse.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);
        await AssertNoVisitAsync(corrupt.Appointment.AppointmentId);
        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(AiPendingToolActionState.FailedTerminal, (await verifyDb.AiPendingToolActions.SingleAsync(x => x.ActionId == corrupt.Pending.ActionId)).State);
    }

    [Fact]
    public async Task Reception_revalidates_resource_version_and_rejects_fake_or_cross_facility_scope()
    {
        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        var versioned = await PrepareReceptionActionAsync(receptionist, "version");
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointment = await db.Appointments.SingleAsync(x => x.Id == versioned.Appointment.AppointmentId);
            appointment.Reason = "Lý do đã thay đổi sau prepare";
            await db.SaveChangesAsync();
        }
        var stale = await ConfirmAsync(receptionist, versioned.Pending);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_VERSION_CHANGED", (await stale.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);
        await AssertNoVisitAsync(versioned.Appointment.AppointmentId);

        var baseAppointment = await CreateAppointmentAsync();
        var injectedFacility = await receptionist.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "reception.prepare_check_in_appointment",
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(new { appointmentId = baseAppointment.AppointmentId, departmentId = baseAppointment.DepartmentId, facilityId = 999999L }),
            sessionId = Session("facility-injection")
        });
        Assert.Equal(HttpStatusCode.BadRequest, injectedFacility.StatusCode);
        Assert.Equal("FORBIDDEN_TOOL_ARGUMENT", (await injectedFacility.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);

        var isolated = await CreateIsolatedFacilityAppointmentAsync();
        var crossFacility = await receptionist.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "reception.prepare_check_in_appointment",
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(new { appointmentId = isolated.AppointmentId, departmentId = isolated.DepartmentId }),
            sessionId = Session("cross-facility")
        });
        Assert.Equal(HttpStatusCode.Forbidden, crossFacility.StatusCode);
        await AssertNoVisitAsync(isolated.AppointmentId);

        // Doctor2 has assignments at both facilities. Supplying a department
        // from the reception user's facility must not re-home an appointment
        // that was explicitly bound to the isolated facility.
        var assignmentPivot = await receptionist.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName = "reception.prepare_check_in_appointment",
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(new { appointmentId = isolated.AppointmentId, departmentId = baseAppointment.DepartmentId }),
            sessionId = Session("cross-facility-assignment-pivot")
        });
        Assert.Equal(HttpStatusCode.Forbidden, assignmentPivot.StatusCode);
        await AssertNoVisitAsync(isolated.AppointmentId);
    }

    [Fact]
    public async Task Role_read_gateway_uses_persisted_facility_binding_and_does_not_leak_another_facility()
    {
        var isolated = await CreateIsolatedFacilityAppointmentAsync();
        var isolatedData = await CreateIsolatedFacilityClinicalDataAsync(isolated);

        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        var receptionLookup = await ExecuteReadAsync(receptionist, "reception.lookup_appointment", new { appointmentCode = isolated.AppointmentCode }, "read-isolated-reception");
        Assert.Equal(HttpStatusCode.OK, receptionLookup.StatusCode);
        Assert.Equal("NOT_FOUND", (await receptionLookup.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);

        var technician = await CreateAuthenticatedClientAsync("tech@test.com");
        var technicianWorklist = await ExecuteReadAsync(technician, "technician.get_worklist", new { }, "read-isolated-tech");
        Assert.Equal(HttpStatusCode.OK, technicianWorklist.StatusCode);
        var technicianResult = await technicianWorklist.Content.ReadFromJsonAsync<AiToolExecutionResult>();
        Assert.Equal("completed", technicianResult?.Status);
        Assert.DoesNotContain(isolatedData.OrderCode, JsonSerializer.Serialize(technicianResult?.Data), StringComparison.Ordinal);

        var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var pharmacyQueue = await ExecuteReadAsync(pharmacist, "pharmacist.get_prescription_queue", new { }, "read-isolated-pharmacy");
        Assert.Equal(HttpStatusCode.OK, pharmacyQueue.StatusCode);
        var pharmacyResult = await pharmacyQueue.Content.ReadFromJsonAsync<AiToolExecutionResult>();
        Assert.Equal("completed", pharmacyResult?.Status);
        var pharmacyItems = Assert.IsType<JsonElement>(pharmacyResult!.Data);
        Assert.DoesNotContain(pharmacyItems.EnumerateArray(), item => item.GetProperty("id").GetInt64() == isolatedData.PrescriptionId);

        // Inventory is explicitly global in the current domain model. The
        // gateway still requires an active pharmacist facility assignment.
        var inventory = await ExecuteReadAsync(pharmacist, "pharmacist.get_inventory_status", new { }, "read-global-inventory");
        Assert.Equal(HttpStatusCode.OK, inventory.StatusCode);
        Assert.Contains("toàn hệ thống", (await inventory.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.DisplayText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patient_booking_persists_the_selected_facility_and_fails_closed_when_a_multi_facility_choice_is_missing()
    {
        var isolated = await CreateIsolatedFacilityAppointmentAsync();
        long slotId;
        var bookingDate = GetFutureWorkingDate(9);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.DoctorWorkSchedules.AnyAsync(x => x.DoctorId == Doctor2EntityId && x.WorkDate == bookingDate && x.StartTime <= new TimeOnly(10, 0) && x.EndTime >= new TimeOnly(10, 30)))
            {
                db.DoctorWorkSchedules.Add(new DoctorWorkSchedule
                {
                    DoctorId = Doctor2EntityId,
                    WorkDate = bookingDate,
                    StartTime = new TimeOnly(8, 0),
                    EndTime = new TimeOnly(17, 0),
                    IsActive = true
                });
            }
            var slot = new AppointmentSlot
            {
                DoctorId = Doctor2EntityId,
                SlotDate = bookingDate,
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(10, 30),
                IsBooked = false
            };
            db.AppointmentSlots.Add(slot);
            await db.SaveChangesAsync();
            slotId = slot.Id;
        }

        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var missingFacility = await patient.PostAsJsonAsync("/api/v1/appointments", new
        {
            doctorId = Doctor2EntityId,
            specialtyId = SpecialtyEntityId,
            appointmentSlotId = slotId,
            reason = "Cần khám tại cơ sở đã chọn để kiểm thử ràng buộc"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingFacility.StatusCode);
        Assert.Contains("FACILITY_SELECTION_REQUIRED", await missingFacility.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var boundBooking = await patient.PostAsJsonAsync("/api/v1/appointments", new
        {
            doctorId = Doctor2EntityId,
            specialtyId = SpecialtyEntityId,
            facilityId = isolated.FacilityId,
            appointmentSlotId = slotId,
            reason = "Cần khám tại cơ sở đã chọn để kiểm thử ràng buộc"
        });
        Assert.Equal(HttpStatusCode.Created, boundBooking.StatusCode);
        var bookingBody = await boundBooking.Content.ReadFromJsonAsync<ClinicManagement.Application.Common.Models.ApiResponse<ClinicManagement.Application.Appointments.DTOs.AppointmentDto>>();
        Assert.NotNull(bookingBody?.Data);
        Assert.Equal(isolated.FacilityId, bookingBody.Data.FacilityId);
        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(isolated.FacilityId, await verifyDb.Appointments.Where(x => x.Id == bookingBody.Data.Id).Select(x => x.FacilityId).SingleAsync());
    }

    [Fact]
    public async Task Doctor_confirmed_order_is_visible_to_technician_and_crash_replay_creates_one_order_audit_and_notification_set()
    {
        var visit = await CreateVisitAsync(VisitStatus.WaitingForDoctor);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var pending = await PrepareAsync(doctor, "doctor.prepare_diagnostic_order", new
        {
            visitId = visit.VisitId,
            clinicalIndication = "Chỉ định xét nghiệm theo workflow AI có xác nhận",
            serviceIds = new[] { visit.DiagnosticServiceId }
        }, "doctor-order");
        Assert.Contains("Dịch vụ", string.Join("|", pending.Preview.Changes.SelectMany(change => change.Items).Select(item => item.Label)), StringComparison.Ordinal);
        Assert.Contains("diagnostic_order", pending.Preview.Changes.Select(change => change.Kind));
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(doctor, pending)).StatusCode);

        long orderId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await db.DiagnosticOrders.SingleAsync(x => x.SourceAiActionId == pending.ActionId);
            orderId = order.Id;
            Assert.Equal(visit.FacilityId, order.FacilityId);
            Assert.Equal(1, await db.SystemAuditLogs.CountAsync(x => x.Action == "DiagnosticOrderCreated" && x.EntityId == order.Id.ToString()));
            Assert.Equal(1, await db.Notifications.CountAsync(x => x.DedupeKey != null && x.DedupeKey.StartsWith($"diag_created_tech_{order.Id}_")));
        }

        var technician = await CreateAuthenticatedClientAsync("tech@test.com");
        var worklist = await technician.GetAsync("/api/v1/diagnostics/orders?status=Ordered");
        Assert.Equal(HttpStatusCode.OK, worklist.StatusCode);
        Assert.Contains(orderId.ToString(), await worklist.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await MutateActionAsync(pending.ActionId, action =>
        {
            action.State = AiPendingToolActionState.FailedRetryable;
            action.ExecutedAtUtc = null;
            action.ExecutionResultReference = null;
            action.LastErrorCode = "SIMULATED_CRASH_AFTER_DOMAIN_COMMIT";
        });
        var replay = await ConfirmAsync(doctor, pending);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await verifyDb.DiagnosticOrders.CountAsync(x => x.SourceAiActionId == pending.ActionId));
        Assert.Equal(1, await verifyDb.SystemAuditLogs.CountAsync(x => x.Action == "DiagnosticOrderCreated" && x.EntityId == orderId.ToString()));
        Assert.Equal(1, await verifyDb.AiAuditLogs.CountAsync(x => x.ActionType == "Tool:role.execute_confirmed_action" && x.SessionId == pending.SessionId && x.Outcome == "completed"));
    }

    [Fact]
    public async Task Technician_confirmed_workflow_keeps_result_hidden_until_doctor_review_then_patient_can_see_it_once()
    {
        var visit = await CreateVisitAsync(VisitStatus.WaitingForDoctor);
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var orderAction = await PrepareAsync(doctor, "doctor.prepare_diagnostic_order", new
        {
            visitId = visit.VisitId,
            clinicalIndication = "Cần xét nghiệm để kiểm thử đồng bộ actor",
            serviceIds = new[] { visit.DiagnosticServiceId }
        }, "tech-flow-order");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(doctor, orderAction)).StatusCode);

        long orderId;
        long itemId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await db.DiagnosticOrders.Include(x => x.Items).SingleAsync(x => x.SourceAiActionId == orderAction.ActionId);
            orderId = order.Id;
            itemId = Assert.Single(order.Items).Id;
        }

        var technician = await CreateAuthenticatedClientAsync("tech@test.com");
        var start = await PrepareAsync(technician, "technician.prepare_start_diagnostic_order", new { orderId }, "tech-start");
        Assert.Contains("diagnostic_start", start.Preview.Changes.Select(change => change.Kind));
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(technician, start)).StatusCode);
        var result = await PrepareAsync(technician, "technician.prepare_record_diagnostic_result", new
        {
            orderId,
            itemId,
            resultText = "KẾT_QUẢ_NHÁP_CHỈ_KỸ_THUẬT_VIÊN_VÀ_BÁC_SĨ_THẤY",
            conclusion = "Đang chờ bác sĩ review"
        }, "tech-result");
        Assert.Contains("diagnostic_result", result.Preview.Changes.Select(change => change.Kind));
        Assert.Contains("KẾT_QUẢ_NHÁP", string.Join("|", result.Preview.Changes.SelectMany(change => change.Items).Select(item => item.Value)), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(technician, result)).StatusCode);

        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var draftVisibility = await patient.GetAsync($"/api/v1/patients/me/diagnostic-orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, draftVisibility.StatusCode);
        Assert.DoesNotContain("KẾT_QUẢ_NHÁP_CHỈ_KỸ_THUẬT_VIÊN_VÀ_BÁC_SĨ_THẤY", await draftVisibility.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var complete = await PrepareAsync(technician, "technician.prepare_complete_diagnostic_order", new { orderId }, "tech-complete");
        Assert.Contains("diagnostic_complete", complete.Preview.Changes.Select(change => change.Kind));
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(technician, complete)).StatusCode);
        var completedButUnreviewed = await patient.GetAsync($"/api/v1/patients/me/diagnostic-orders/{orderId}");
        Assert.DoesNotContain("KẾT_QUẢ_NHÁP_CHỈ_KỸ_THUẬT_VIÊN_VÀ_BÁC_SĨ_THẤY", await completedButUnreviewed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var review = await doctor.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{orderId}/review", new { });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var published = await patient.GetAsync($"/api/v1/patients/me/diagnostic-orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Contains("KẾT_QUẢ_NHÁP_CHỈ_KỸ_THUẬT_VIÊN_VÀ_BÁC_SĨ_THẤY", await published.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await verifyDb.Notifications.CountAsync(x => x.DedupeKey == $"diag_reviewed_pat_{orderId}"));
        Assert.Equal(1, await verifyDb.SystemAuditLogs.CountAsync(x => x.Action == "DiagnosticOrderCompleted" && x.EntityId == orderId.ToString()));
    }

    [Fact]
    public async Task Pharmacy_confirmed_reservation_is_idempotent_and_unpaid_dispense_is_terminal_without_an_extra_stock_decrement()
    {
        var visit = await CreateVisitAsync(VisitStatus.InPharmacy);
        long prescriptionId;
        int stockBefore;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var medicine = await db.Medicines.SingleAsync(x => x.Id == MedicineEntityId);
            stockBefore = medicine.StockQuantity;
            var prescription = new Prescription
            {
                PatientVisitId = visit.VisitId,
                PatientId = Patient1EntityId,
                DoctorId = DoctorEntityId,
                Status = PrescriptionStatus.Issued,
                Notes = "Đơn để kiểm thử pharmacy gateway"
            };
            db.Prescriptions.Add(prescription);
            await db.SaveChangesAsync();
            db.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = prescription.Id,
                MedicineId = MedicineEntityId,
                Quantity = 2,
                Dosage = "500mg",
                Frequency = "Ngày 2 lần"
            });
            await db.SaveChangesAsync();
            prescriptionId = prescription.Id;
        }

        var pharmacist = await CreateAuthenticatedClientAsync("pharm@test.com");
        var reserve = await PrepareAsync(pharmacist, "pharmacist.prepare_reserve_prescription", new { prescriptionId }, "pharmacy-reserve");
        Assert.Contains("prescription_reservation", reserve.Preview.Changes.Select(change => change.Kind));
        Assert.Contains(2, reserve.Preview.Changes.SelectMany(change => change.Items).Select(item => item.Quantity));
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(pharmacist, reserve)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(pharmacist, reserve)).StatusCode);

        var dispense = await PrepareAsync(pharmacist, "pharmacist.prepare_dispense_prescription", new { prescriptionId }, "pharmacy-dispense");
        Assert.Contains("prescription_dispense", dispense.Preview.Changes.Select(change => change.Kind));
        var unpaid = await ConfirmAsync(pharmacist, dispense);
        Assert.Equal(HttpStatusCode.BadRequest, unpaid.StatusCode);
        Assert.Equal("PRESCRIPTION_NOT_PAID", (await unpaid.Content.ReadFromJsonAsync<AiToolExecutionResult>())?.Error?.Code);

        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicineAfter = await verifyDb.Medicines.SingleAsync(x => x.Id == MedicineEntityId);
        Assert.Equal(stockBefore - 2, medicineAfter.StockQuantity);
        Assert.Equal(PrescriptionStatus.ReservedForPurchase, (await verifyDb.Prescriptions.SingleAsync(x => x.Id == prescriptionId)).Status);
        Assert.Equal(1, await verifyDb.MedicineStockTransactions.CountAsync(x => x.PrescriptionId == prescriptionId && x.Type == MedicineStockTransactionType.Reservation));
        Assert.Equal(0, await verifyDb.MedicineStockTransactions.CountAsync(x => x.PrescriptionId == prescriptionId && x.Type == MedicineStockTransactionType.Dispense));
        Assert.Equal(AiPendingToolActionState.FailedTerminal, (await verifyDb.AiPendingToolActions.SingleAsync(x => x.ActionId == dispense.ActionId)).State);
    }

    private async Task<(RoleAppointment Appointment, PendingAction Pending)> PrepareReceptionActionAsync(HttpClient client, string suffix)
    {
        var appointment = await CreateAppointmentAsync();
        var pending = await PrepareAsync(client, "reception.prepare_check_in_appointment", new { appointmentId = appointment.AppointmentId, departmentId = appointment.DepartmentId }, suffix);
        return (appointment, pending);
    }

    private async Task<PendingAction> PrepareAsync(HttpClient client, string toolName, object arguments, string suffix, string? idempotencyKey = null, string? sessionId = null)
    {
        sessionId ??= Session(suffix);
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName,
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(arguments),
            sessionId,
            conversationId = $"conv_{Guid.NewGuid():N}",
            idempotencyKey = idempotencyKey ?? $"idem_{Guid.NewGuid():N}"
        });
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Xunit.Sdk.XunitException($"Prepare {toolName} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<AiToolExecutionResult>();
        Assert.NotNull(body);
        Assert.Equal("pending_confirmation", body!.Status);
        Assert.NotNull(body.Preview);
        Assert.Equal(toolName, body.Preview!.ToolName);
        Assert.Equal("pending_confirmation", body.Preview.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Preview.ResourceType));
        Assert.False(string.IsNullOrWhiteSpace(body.Preview.ResourceId));
        Assert.False(string.IsNullOrWhiteSpace(body.Preview.Resource.Identity));
        Assert.NotEmpty(body.Preview.Changes);
        Assert.All(body.Preview.Changes, change =>
        {
            Assert.False(string.IsNullOrWhiteSpace(change.Kind));
            Assert.False(string.IsNullOrWhiteSpace(change.Summary));
            Assert.NotEmpty(change.Items);
            Assert.All(change.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Value)));
        });
        Assert.False(string.IsNullOrWhiteSpace(body.Preview.Consequence));
        Assert.False(string.IsNullOrWhiteSpace(body.Preview.ConfirmationSummary));
        Assert.NotEqual(default, body.Preview.ValidatedAtUtc);
        Assert.True(body.Preview.ExpiresAtUtc > body.Preview.ValidatedAtUtc);
        Assert.NotEmpty(body.Preview.Sources);
        var previewJson = JsonSerializer.Serialize(body.Preview);
        Assert.DoesNotContain("confirmationToken", previewJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Patient 1", previewJson, StringComparison.Ordinal);
        var data = Assert.IsType<JsonElement>(body.Data);
        return new PendingAction(
            data.GetProperty("actionId").GetGuid(),
            data.GetProperty("confirmationToken").GetString()!,
            sessionId,
            body.Preview);
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, PendingAction action) =>
        client.PostAsJsonAsync($"/api/v1/ai/copilot/actions/{action.ActionId}/confirm", new { sessionId = action.SessionId, concurrencyToken = action.Token });

    private static Task<HttpResponseMessage> ExecuteReadAsync(HttpClient client, string toolName, object arguments, string suffix) =>
        client.PostAsJsonAsync("/api/v1/ai/tools/execute", new
        {
            toolName,
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(arguments),
            sessionId = Session(suffix)
        });

    private async Task<RoleAppointment> CreateAppointmentAsync(long? doctorId = null, long? facilityId = null, long? departmentId = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = departmentId.HasValue
            ? await db.Departments.SingleAsync(x => x.Id == departmentId.Value)
            : await db.Departments.OrderBy(x => x.Id).FirstAsync(x => x.IsActive && x.SpecialtyId == SpecialtyEntityId);
        var doctor = doctorId ?? DoctorEntityId;
        var ordinal = Interlocked.Increment(ref _sequence);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var start = new TimeOnly(7 + ordinal % 11, (ordinal * 5) % 60);
        var slot = new AppointmentSlot { DoctorId = doctor, SlotDate = date, StartTime = start, EndTime = start.AddMinutes(20), IsBooked = true };
        db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentCode = $"ROLE-AI-{Guid.NewGuid():N}"[..20],
            PatientId = Patient1EntityId,
            DoctorId = doctor,
            SpecialtyId = SpecialtyEntityId,
            FacilityId = facilityId ?? department.FacilityId,
            AppointmentSlotId = slot.Id,
            AppointmentDate = date,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Reason = "Lịch hẹn cho role gateway integration test",
            Status = AppointmentStatus.Confirmed
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return new RoleAppointment(appointment.Id, appointment.AppointmentCode, department.Id, appointment.FacilityId!.Value);
    }

    private async Task<RoleAppointment> CreateIsolatedFacilityAppointmentAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = new Facility { Code = $"FAC-ROLE-{Guid.NewGuid():N}"[..18], Name = "Cơ sở cô lập AI", Address = "Khu kiểm thử", IsActive = true };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        var department = new Department { FacilityId = facility.Id, SpecialtyId = SpecialtyEntityId, Code = $"DEP-ROLE-{Guid.NewGuid():N}"[..18], Name = "Khoa cô lập AI", DepartmentType = DepartmentType.Clinical, IsActive = true };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = Doctor2UserId, FacilityId = facility.Id, DepartmentId = department.Id, Role = "Doctor", IsActive = true, IsPrimary = true });
        await db.SaveChangesAsync();
        return await CreateAppointmentAsync(Doctor2EntityId, facility.Id, department.Id);
    }

    private async Task<IsolatedClinicalData> CreateIsolatedFacilityClinicalDataAsync(RoleAppointment appointment)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visit = new PatientVisit
        {
            VisitCode = $"VIS-ISO-{Guid.NewGuid():N}"[..20],
            PatientId = Patient1EntityId,
            FacilityId = appointment.FacilityId,
            DepartmentId = appointment.DepartmentId,
            AssignedDoctorId = Doctor2EntityId,
            VisitDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
            ArrivalType = VisitArrivalType.WalkIn,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Dữ liệu scope facility cô lập",
            QueueNumber = 20000 + Interlocked.Increment(ref _sequence),
            Status = VisitStatus.WaitingForDoctor,
            CreatedByUserId = Doctor2UserId
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();

        var orderCode = $"ORD-ISO-{Guid.NewGuid():N}"[..20];
        db.DiagnosticOrders.Add(new DiagnosticOrder
        {
            OrderCode = orderCode,
            PatientVisitId = visit.Id,
            PatientId = Patient1EntityId,
            OrderingDoctorId = Doctor2EntityId,
            FacilityId = appointment.FacilityId,
            PerformingDepartmentId = appointment.DepartmentId,
            ClinicalIndication = "Không được lộ sang cơ sở khác",
            Status = DiagnosticOrderStatus.Ordered
        });
        var prescription = new Prescription
        {
            PatientVisitId = visit.Id,
            PatientId = Patient1EntityId,
            DoctorId = Doctor2EntityId,
            Status = PrescriptionStatus.Issued,
            Notes = "Không được lộ sang cơ sở khác"
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        return new IsolatedClinicalData(orderCode, prescription.Id);
    }

    private async Task<RoleVisit> CreateVisitAsync(VisitStatus status)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await db.Departments.OrderBy(x => x.Id).FirstAsync(x => x.IsActive && x.SpecialtyId == SpecialtyEntityId);
        var serviceId = await db.DiagnosticServices.OrderBy(x => x.Id).Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        var visit = new PatientVisit
        {
            VisitCode = $"VIS-ROLE-{Guid.NewGuid():N}"[..20],
            PatientId = Patient1EntityId,
            FacilityId = department.FacilityId,
            DepartmentId = department.Id,
            AssignedDoctorId = DoctorEntityId,
            VisitDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
            ArrivalType = VisitArrivalType.WalkIn,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Khám role action gateway",
            QueueNumber = 10000 + Interlocked.Increment(ref _sequence),
            Status = status,
            CreatedByUserId = ReceptionistId
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();
        return new RoleVisit(visit.Id, department.FacilityId, serviceId);
    }

    private async Task MutateActionAsync(Guid actionId, Action<AiPendingToolAction> mutate)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var action = await db.AiPendingToolActions.SingleAsync(x => x.ActionId == actionId);
        mutate(action);
        await db.SaveChangesAsync();
    }

    private async Task AssertNoVisitAsync(long appointmentId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.PatientVisits.CountAsync(x => x.AppointmentId == appointmentId));
    }

    private async Task AssertSingleVisitAsync(long appointmentId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PatientVisits.CountAsync(x => x.AppointmentId == appointmentId));
    }

    private static string Session(string suffix) => $"sess_role_{suffix}_{Guid.NewGuid():N}";

    private sealed record PendingAction(Guid ActionId, string Token, string SessionId, AiToolActionPreview Preview);
    private sealed record RoleAppointment(long AppointmentId, string AppointmentCode, long DepartmentId, long FacilityId);
    private sealed record RoleVisit(long VisitId, long FacilityId, long DiagnosticServiceId);
    private sealed record IsolatedClinicalData(string OrderCode, long PrescriptionId);
}
