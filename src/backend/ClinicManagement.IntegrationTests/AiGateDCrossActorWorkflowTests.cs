using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiGateDCrossActorWorkflowTests : IntegrationTestBase
{
    public AiGateDCrossActorWorkflowTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Synthetic_patient_to_admin_workflow_preserves_scope_state_and_exactly_once_effects()
    {
        var seed = await EnsureGateDSeedAsync();

        // Seed/setup ends here. Every lifecycle transition below goes through
        // an authenticated HTTP endpoint or the real action gateway. DbContext
        // is used only to read persisted state and assert side effects.
        var slotDate = GetFutureWorkingDate(61);
        var slot = await CreateAvailableSlotAsync(
            seed.DoctorId,
            slotDate,
            new TimeOnly(9, 0),
            new TimeOnly(9, 30));
        var patientA = await CreateAuthenticatedClientAsync("pat1@test.com");
        var idempotencyKey = $"gate-d-appointment-{Guid.NewGuid():N}";
        var appointmentRequest = new
        {
            doctorId = seed.DoctorId,
            specialtyId = seed.SpecialtyId,
            facilityId = seed.AlphaFacilityId,
            appointmentSlotId = slot.Id,
            reason = "Tái khám định kỳ tăng huyết áp",
            idempotencyKey
        };

        var appointmentResponse = await patientA.PostAsJsonAsync("/api/v1/appointments", appointmentRequest);
        Assert.True(appointmentResponse.StatusCode == HttpStatusCode.Created,
            $"Appointment create failed ({(int)appointmentResponse.StatusCode}): {await appointmentResponse.Content.ReadAsStringAsync()}");
        var appointmentData = await ReadDataAsync(appointmentResponse);
        var appointmentId = appointmentData.GetProperty("id").GetInt64();
        var appointmentCode = appointmentData.GetProperty("appointmentCode").GetString()!;
        Assert.Equal(seed.AlphaFacilityId, appointmentData.GetProperty("facilityId").GetInt64());
        Assert.Equal("Pending", appointmentData.GetProperty("status").GetString());

        var appointmentRetry = await patientA.PostAsJsonAsync("/api/v1/appointments", appointmentRequest);
        Assert.Equal(HttpStatusCode.Created, appointmentRetry.StatusCode);
        Assert.Equal(appointmentId, (await ReadDataAsync(appointmentRetry)).GetProperty("id").GetInt64());
        var changedPayload = new
        {
            doctorId = seed.DoctorId,
            specialtyId = seed.SpecialtyId,
            facilityId = seed.AlphaFacilityId,
            appointmentSlotId = slot.Id,
            reason = "Tái khám định kỳ tăng huyết áp và kiểm tra huyết áp",
            idempotencyKey
        };
        var changedRetry = await patientA.PostAsJsonAsync("/api/v1/appointments", changedPayload);
        Assert.Equal(HttpStatusCode.Conflict, changedRetry.StatusCode);
        Assert.Contains("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", await changedRetry.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync<Appointment>(x => x.Id == appointmentId));

        var patientB = await CreateAuthenticatedClientAsync("pat2@test.com");
        var patientBAppointment = await patientB.GetAsync($"/api/v1/appointments/{appointmentId}");
        Assert.False(patientBAppointment.IsSuccessStatusCode);
        var patientBCancel = await patientB.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/cancellation-requests", new { reason = "Synthetic wrong patient" });
        Assert.False(patientBCancel.IsSuccessStatusCode);

        var receptionA = await CreateAuthenticatedClientAsync("rec@test.com");
        var receptionList = await receptionA.GetAsync($"/api/v1/reception/appointments?facilityId={seed.AlphaFacilityId}&search={Uri.EscapeDataString(appointmentCode)}");
        Assert.Equal(HttpStatusCode.OK, receptionList.StatusCode);
        Assert.Contains(appointmentCode, await receptionList.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await receptionA.PostAsync($"/api/v1/reception/appointments/{appointmentId}/confirm", null)).StatusCode);

        var receptionB = await CreateAuthenticatedClientAsync(seed.ReceptionistBetaEmail);
        var betaList = await receptionB.GetAsync($"/api/v1/reception/appointments?facilityId={seed.AlphaFacilityId}&search={Uri.EscapeDataString(appointmentCode)}");
        Assert.Equal(HttpStatusCode.Forbidden, betaList.StatusCode);
        Assert.False((await receptionB.GetAsync($"/api/v1/reception/appointments/{appointmentId}")).IsSuccessStatusCode);

        var visitsBeforeCheckIn = await CountAsync<PatientVisit>(x => x.AppointmentId == appointmentId);
        var checkIn = await PrepareActionAsync(receptionA, "reception.prepare_check_in_appointment", new
        {
            appointmentId,
            departmentId = seed.AlphaDepartmentId
        }, "gate-d-check-in");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(receptionA, checkIn)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(receptionA, checkIn)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmActionWithAsync(receptionA, checkIn, checkIn.SessionId, "wrong-token")).StatusCode);

        var visit = await ReadSingleAsync<PatientVisit, VisitSnapshot>(
            x => x.AppointmentId == appointmentId,
            x => new VisitSnapshot(x.Id, x.QueueNumber, x.Status, x.FacilityId, x.DepartmentId, x.AssignedDoctorId));
        Assert.Equal(visitsBeforeCheckIn + 1, await CountAsync<PatientVisit>(x => x.AppointmentId == appointmentId));
        Assert.Equal(VisitStatus.WaitingForDoctor, visit.Status);
        Assert.Equal(seed.AlphaFacilityId, visit.FacilityId);
        Assert.Equal(seed.AlphaDepartmentId, visit.DepartmentId);
        Assert.Equal(seed.DoctorId, visit.AssignedDoctorId);
        Assert.Equal(1, await CountAsync<AppointmentHistory>(x => x.AppointmentId == appointmentId && x.Action == AppointmentHistoryAction.CheckedIn));
        Assert.Equal(1, await CountAsync<Notification>(x => x.DedupeKey == $"checkin_appointment_{appointmentId}"));

        var doctorA = await CreateAuthenticatedClientAsync("doc@test.com");
        var doctorQueue = await doctorA.GetAsync($"/api/v1/doctor/queue?date={slotDate:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, doctorQueue.StatusCode);
        Assert.Contains(visit.Id.ToString(), await doctorQueue.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var doctorB = await CreateAuthenticatedClientAsync("doc2@test.com");
        Assert.Equal(HttpStatusCode.NotFound, (await doctorB.GetAsync($"/api/v1/doctor/appointments/{appointmentId}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctorA.GetAsync($"/api/v1/doctor/visits/{visit.Id}/clinical-context")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctorA.PostAsync($"/api/v1/doctor/visits/{visit.Id}/start-consultation", null)).StatusCode);

        var diagnosticServiceId = await ReadFirstScalarAsync<DiagnosticService, long>(x => x.IsActive, x => x.Id);
        var diagnosticAction = await PrepareActionAsync(doctorA, "doctor.prepare_diagnostic_order", new
        {
            visitId = visit.Id,
            departmentId = seed.AlphaDepartmentId,
            clinicalIndication = "Synthetic Gate D diagnostic indication",
            serviceIds = new[] { diagnosticServiceId }
        }, "gate-d-diagnostic-order");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(doctorA, diagnosticAction)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(doctorA, diagnosticAction)).StatusCode);

        var diagnosticOrder = await ReadSingleAsync<DiagnosticOrder, DiagnosticSnapshot>(
            x => x.PatientVisitId == visit.Id,
            x => new DiagnosticSnapshot(x.Id, x.Status, x.FacilityId, x.Items.Select(i => i.Id).Single()));
        Assert.Equal(1, await CountAsync<DiagnosticOrder>(x => x.PatientVisitId == visit.Id));
        Assert.Equal(1, await CountAsync<SystemAuditLog>(x => x.Action == "DiagnosticOrderCreated" && x.EntityId == diagnosticOrder.Id.ToString()));
        Assert.Equal(1, await CountAsync<Notification>(x => x.DedupeKey == $"diag_created_pat_{diagnosticOrder.Id}_{Patient1Id}"));

        var technicianA = await CreateAuthenticatedClientAsync("tech@test.com");
        var technicianB = await CreateAuthenticatedClientAsync(seed.TechnicianBetaEmail);
        Assert.Equal(HttpStatusCode.OK, (await technicianA.GetAsync("/api/v1/diagnostics/orders?status=Ordered")).StatusCode);
        Assert.Contains(diagnosticOrder.Id.ToString(), await (await technicianA.GetAsync("/api/v1/diagnostics/orders?status=Ordered")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var betaDiagnosticOrders = await ReadDataAsync(await technicianB.GetAsync("/api/v1/diagnostics/orders?status=Ordered"));
        Assert.Empty(betaDiagnosticOrders.GetProperty("items").EnumerateArray());
        Assert.False((await PrepareActionAsyncResponse(technicianB, "technician.prepare_start_diagnostic_order", new { orderId = diagnosticOrder.Id }, "gate-d-wrong-tech")).IsSuccessStatusCode);

        var itemId = diagnosticOrder.ItemId;
        var startDiagnostic = await PrepareActionAsync(technicianA, "technician.prepare_start_diagnostic_order", new { orderId = diagnosticOrder.Id }, "gate-d-diagnostic-start");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(technicianA, startDiagnostic)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(technicianA, startDiagnostic)).StatusCode);
        var recordResult = await PrepareActionAsync(technicianA, "technician.prepare_record_diagnostic_result", new
        {
            orderId = diagnosticOrder.Id,
            itemId,
            resultText = "SYNTHETIC_RESULT_GATE_D_NOT_PUBLISHED",
            conclusion = "Synthetic result awaiting doctor review",
            referenceRange = "synthetic-range",
            unit = "synthetic-unit"
        }, "gate-d-diagnostic-result");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(technicianA, recordResult)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(technicianA, recordResult)).StatusCode);
        var completeDiagnostic = await PrepareActionAsync(technicianA, "technician.prepare_complete_diagnostic_order", new { orderId = diagnosticOrder.Id }, "gate-d-diagnostic-complete");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(technicianA, completeDiagnostic)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(technicianA, completeDiagnostic)).StatusCode);

        var patientDraftResult = await patientA.GetAsync($"/api/v1/patients/me/diagnostic-orders/{diagnosticOrder.Id}");
        Assert.Equal(HttpStatusCode.OK, patientDraftResult.StatusCode);
        Assert.DoesNotContain("SYNTHETIC_RESULT_GATE_D_NOT_PUBLISHED", await patientDraftResult.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await patientB.GetAsync($"/api/v1/patients/me/diagnostic-orders/{diagnosticOrder.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await doctorA.PostAsJsonAsync($"/api/v1/doctor/diagnostic-orders/{diagnosticOrder.Id}/review", new { })).StatusCode);
        var patientPublishedResult = await patientA.GetAsync($"/api/v1/patients/me/diagnostic-orders/{diagnosticOrder.Id}");
        Assert.Contains("SYNTHETIC_RESULT_GATE_D_NOT_PUBLISHED", await patientPublishedResult.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await patientB.GetAsync($"/api/v1/patients/me/diagnostic-orders/{diagnosticOrder.Id}")).StatusCode);

        var prescriptionDraft = await doctorA.PutAsJsonAsync($"/api/v1/doctor/visits/{visit.Id}/prescription-draft", new
        {
            notes = "Synthetic Gate D prescription",
            items = new[]
            {
                new
                {
                    medicineId = seed.MedicineId,
                    quantity = 2,
                    dosage = "synthetic-dose",
                    frequency = "synthetic-frequency",
                    durationDays = 2,
                    instructions = "Synthetic instructions"
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, prescriptionDraft.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctorA.PostAsJsonAsync($"/api/v1/doctor/visits/{visit.Id}/complete", new
        {
            diagnosis = "Synthetic Gate D diagnosis",
            treatmentPlan = "Synthetic Gate D treatment plan",
            summary = "Synthetic consultation completed after diagnostic review"
        })).StatusCode);

        var prescription = await ReadSingleAsync<Prescription, PrescriptionSnapshot>(
            x => x.PatientVisitId == visit.Id,
            x => new PrescriptionSnapshot(x.Id, x.Status, x.Items.Select(i => i.Quantity).Single()));
        Assert.Equal(PrescriptionStatus.Issued, prescription.Status);
        Assert.Equal(1, await CountAsync<Prescription>(x => x.PatientVisitId == visit.Id));

        var receptionInvoice = await receptionA.PostAsJsonAsync("/api/v1/reception/billing/invoices/visit", new { patientVisitId = visit.Id });
        Assert.Equal(HttpStatusCode.Created, receptionInvoice.StatusCode);
        var invoiceData = await ReadDataAsync(receptionInvoice);
        var invoiceId = invoiceData.GetProperty("id").GetInt64();
        var totalAmount = invoiceData.GetProperty("totalAmount").GetDecimal();
        Assert.True(totalAmount > 0);
        Assert.Equal(InvoiceStatus.Unpaid, await ReadScalarAsync<Invoice, InvoiceStatus>(x => x.Id == invoiceId, x => x.Status));

        var pharmacistA = await CreateAuthenticatedClientAsync("pharm@test.com");
        var pharmacistB = await CreateAuthenticatedClientAsync(seed.PharmacistBetaEmail);
        var stockBeforeReserve = await ReadScalarAsync<Medicine, int>(x => x.Id == seed.MedicineId, x => x.StockQuantity);
        Assert.False((await PrepareActionAsyncResponse(pharmacistB, "pharmacist.prepare_reserve_prescription", new { prescriptionId = prescription.Id }, "gate-d-wrong-pharmacy")).IsSuccessStatusCode);

        var reserve = await PrepareActionAsync(pharmacistA, "pharmacist.prepare_reserve_prescription", new { prescriptionId = prescription.Id }, "gate-d-reserve");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(pharmacistA, reserve)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(pharmacistA, reserve)).StatusCode);
        Assert.Equal(PrescriptionStatus.ReservedForPurchase, await ReadScalarAsync<Prescription, PrescriptionStatus>(x => x.Id == prescription.Id, x => x.Status));
        Assert.Equal(stockBeforeReserve - prescription.Quantity, await ReadScalarAsync<Medicine, int>(x => x.Id == seed.MedicineId, x => x.StockQuantity));
        Assert.Equal(1, await CountAsync<MedicineStockTransaction>(x => x.PrescriptionId == prescription.Id && x.Type == MedicineStockTransactionType.Reservation));

        var unpaidDispense = await PrepareActionAsync(pharmacistA, "pharmacist.prepare_dispense_prescription", new { prescriptionId = prescription.Id }, "gate-d-unpaid-dispense");
        var unpaidConfirmation = await ConfirmActionAsync(pharmacistA, unpaidDispense);
        Assert.False(unpaidConfirmation.IsSuccessStatusCode);
        Assert.Contains("PRESCRIPTION_NOT_PAID", await unpaidConfirmation.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync<MedicineStockTransaction>(x => x.PrescriptionId == prescription.Id && x.Type == MedicineStockTransactionType.Dispense));

        Assert.Equal(HttpStatusCode.OK, (await receptionA.PostAsJsonAsync($"/api/v1/reception/billing/invoices/{invoiceId}/pay", new
        {
            amount = totalAmount,
            method = PaymentMethod.Cash,
            referenceCode = "SYNTHETIC-GATE-D-PAYMENT"
        })).StatusCode);
        Assert.Equal(InvoiceStatus.Paid, await ReadScalarAsync<Invoice, InvoiceStatus>(x => x.Id == invoiceId, x => x.Status));

        var dispense = await PrepareActionAsync(pharmacistA, "pharmacist.prepare_dispense_prescription", new { prescriptionId = prescription.Id }, "gate-d-dispense");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(pharmacistA, dispense)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmActionAsync(pharmacistA, dispense)).StatusCode);
        Assert.Equal(PrescriptionStatus.Dispensed, await ReadScalarAsync<Prescription, PrescriptionStatus>(x => x.Id == prescription.Id, x => x.Status));
        Assert.Equal(stockBeforeReserve - prescription.Quantity, await ReadScalarAsync<Medicine, int>(x => x.Id == seed.MedicineId, x => x.StockQuantity));
        Assert.Equal(1, await CountAsync<MedicineStockTransaction>(x => x.PrescriptionId == prescription.Id && x.Type == MedicineStockTransactionType.Dispense));
        Assert.Equal(VisitStatus.Completed, await ReadScalarAsync<PatientVisit, VisitStatus>(x => x.Id == visit.Id, x => x.Status));
        Assert.Equal(1, await CountAsync<Notification>(x => x.DedupeKey == $"rx_dispensed_{prescription.Id}"));

        var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var dashboard = await ExecuteReadAsync(admin, "admin.get_dashboard_metrics", new { }, "gate-d-admin-dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("\"status\":\"completed\"", await dashboard.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var health = await ExecuteReadAsync(admin, "admin.get_ai_health", new { }, "gate-d-admin-health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var healthJson = await health.Content.ReadAsStringAsync();
        Assert.DoesNotContain("x-goog-api-key", healthJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", healthJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pass@123", healthJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SYNTHETIC_RESULT_GATE_D_NOT_PUBLISHED", healthJson, StringComparison.Ordinal);

        var patientAppointments = await patientA.GetAsync("/api/v1/appointments/my");
        Assert.Contains(appointmentCode, await patientAppointments.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var patientPrescriptions = await patientA.GetAsync("/api/v1/patients/me/prescriptions");
        Assert.Contains(prescription.Id.ToString(), await patientPrescriptions.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(appointmentCode, await (await patientB.GetAsync("/api/v1/appointments/my")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(prescription.Id.ToString(), await (await patientB.GetAsync("/api/v1/patients/me/prescriptions")).Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(1, await CountAsync<DiagnosticResult>(x => x.DiagnosticOrderItemId == itemId));
        Assert.Equal(1, await CountAsync<SystemAuditLog>(x => x.Action == "DiagnosticOrderCompleted" && x.EntityId == diagnosticOrder.Id.ToString()));
        Assert.Equal(1, await CountAsync<Notification>(x => x.DedupeKey == $"diag_reviewed_pat_{diagnosticOrder.Id}"));
        Assert.Equal(1, await CountAsync<Payment>(x => x.InvoiceId == invoiceId && x.Status == PaymentStatus.Succeeded));
    }

    private async Task<GateDSeed> EnsureGateDSeedAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var alphaDepartment = await db.Departments.Where(x => x.IsActive).OrderBy(x => x.Id).FirstAsync();
        var alphaFacility = await db.Facilities.SingleAsync(x => x.Id == alphaDepartment.FacilityId);
        var specialty = await db.Specialties.Where(x => x.IsActive).OrderBy(x => x.Id).FirstAsync();
        var betaFacility = await db.Facilities.SingleOrDefaultAsync(x => x.Code == "GATED-BETA");
        if (betaFacility == null)
        {
            betaFacility = new Facility
            {
                Code = "GATED-BETA",
                Name = "Synthetic Gate D Facility Beta",
                Address = "Synthetic only",
                City = "Synthetic",
                Phone = "0000000000",
                IsActive = true
            };
            db.Facilities.Add(betaFacility);
            await db.SaveChangesAsync();
        }

        var betaDepartment = await db.Departments.SingleOrDefaultAsync(x => x.Code == "GATED-BETA-DEPT");
        if (betaDepartment == null)
        {
            betaDepartment = new Department
            {
                FacilityId = betaFacility.Id,
                SpecialtyId = specialty.Id,
                Code = "GATED-BETA-DEPT",
                Name = "Synthetic Gate D Department Beta",
                DepartmentType = DepartmentType.Clinical,
                IsActive = true
            };
            db.Departments.Add(betaDepartment);
            await db.SaveChangesAsync();
        }

        var receptionistBetaEmail = "gate-d-reception-beta@test.com";
        var technicianBetaEmail = "gate-d-technician-beta@test.com";
        var pharmacistBetaEmail = "gate-d-pharmacist-beta@test.com";
        foreach (var account in new[]
        {
            (Email: receptionistBetaEmail, FullName: "Synthetic Reception Beta", Role: RoleNames.Receptionist, Phone: "0000000001"),
            (Email: technicianBetaEmail, FullName: "Synthetic Technician Beta", Role: RoleNames.DiagnosticTechnician, Phone: "0000000002"),
            (Email: pharmacistBetaEmail, FullName: "Synthetic Pharmacist Beta", Role: RoleNames.Pharmacist, Phone: "0000000003")
        })
        {
            var user = await userManager.FindByNameAsync(account.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = account.Email,
                    Email = account.Email,
                    FullName = account.FullName,
                    PhoneNumber = account.Phone,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                var result = await userManager.CreateAsync(user, "Pass@123");
                Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
                await userManager.AddToRoleAsync(user, account.Role);
            }

            if (!await db.StaffFacilityAssignments.AnyAsync(x => x.UserId == user.Id && x.FacilityId == betaFacility.Id && x.IsActive))
            {
                db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
                {
                    UserId = user.Id,
                    FacilityId = betaFacility.Id,
                    DepartmentId = betaDepartment.Id,
                    Role = account.Role,
                    IsPrimary = true,
                    IsActive = true
                });
            }
        }
        await db.SaveChangesAsync();

        return new GateDSeed(
            alphaFacility.Id,
            alphaDepartment.Id,
            await db.Doctors.Where(x => x.UserId == DoctorId).Select(x => x.Id).SingleAsync(),
            specialty.Id,
            await db.Medicines.Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(),
            receptionistBetaEmail,
            technicianBetaEmail,
            pharmacistBetaEmail);
    }

    private async Task<PendingAction> PrepareActionAsync(HttpClient client, string toolName, object arguments, string suffix)
    {
        var response = await PrepareActionAsyncResponse(client, toolName, arguments, suffix);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Prepare {toolName} failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        return new PendingAction(
            data.GetProperty("actionId").GetGuid(),
            data.GetProperty("confirmationToken").GetString()!,
            $"sess_gate_d_{suffix}");
    }

    private static Task<HttpResponseMessage> PrepareActionAsyncResponse(HttpClient client, string toolName, object arguments, string suffix) =>
        client.PostAsJsonAsync("/api/v1/ai/copilot/actions/prepare", new
        {
            toolName,
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(arguments),
            sessionId = $"sess_gate_d_{suffix}",
            conversationId = $"gate-d-conversation-{suffix}",
            idempotencyKey = $"gate-d-idempotency-{suffix}-{Guid.NewGuid():N}"
        });

    private static Task<HttpResponseMessage> ConfirmActionAsync(HttpClient client, PendingAction action) =>
        ConfirmActionWithAsync(client, action, action.SessionId, action.ConfirmationToken);

    private static Task<HttpResponseMessage> ConfirmActionWithAsync(HttpClient client, PendingAction action, string sessionId, string concurrencyToken) =>
        client.PostAsJsonAsync($"/api/v1/ai/copilot/actions/{action.ActionId}/confirm", new { sessionId, concurrencyToken });

    private static Task<HttpResponseMessage> ExecuteReadAsync(HttpClient client, string toolName, object arguments, string suffix) =>
        client.PostAsJsonAsync("/api/v1/ai/tools/execute", new
        {
            toolName,
            toolVersion = "1.0",
            argumentsJson = JsonSerializer.Serialize(arguments),
            sessionId = $"gate-d-read-{suffix}"
        });

    private async Task<TProjection> ReadSingleAsync<TEntity, TProjection>(
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        System.Linq.Expressions.Expression<Func<TEntity, TProjection>> projection)
        where TEntity : class
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<TEntity>().AsNoTracking().Where(predicate).Select(projection).SingleAsync();
    }

    private async Task<TValue> ReadScalarAsync<TEntity, TValue>(
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        System.Linq.Expressions.Expression<Func<TEntity, TValue>> projection)
        where TEntity : class
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<TEntity>().AsNoTracking().Where(predicate).Select(projection).SingleAsync();
    }

    private async Task<TValue> ReadFirstScalarAsync<TEntity, TValue>(
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        System.Linq.Expressions.Expression<Func<TEntity, TValue>> projection)
        where TEntity : class
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<TEntity>().AsNoTracking().Where(predicate).Select(projection).FirstAsync();
    }

    private async Task<int> CountAsync<TEntity>(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<TEntity>().AsNoTracking().CountAsync(predicate);
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").Clone();
    }

    private sealed record GateDSeed(
        long AlphaFacilityId,
        long AlphaDepartmentId,
        long DoctorId,
        long SpecialtyId,
        long MedicineId,
        string ReceptionistBetaEmail,
        string TechnicianBetaEmail,
        string PharmacistBetaEmail);

    private sealed record PendingAction(Guid ActionId, string ConfirmationToken, string SessionId);
    private sealed record VisitSnapshot(long Id, int QueueNumber, VisitStatus Status, long FacilityId, long DepartmentId, long? AssignedDoctorId);
    private sealed record DiagnosticSnapshot(long Id, DiagnosticOrderStatus Status, long? FacilityId, long ItemId);
    private sealed record PrescriptionSnapshot(long Id, PrescriptionStatus Status, int Quantity);
}
