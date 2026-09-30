using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Api;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Suggestions;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Tools;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiSuggestionButtonTests : IntegrationTestBase
{
    private const string ChatUrl = "/api/v1/ai/copilot/chat";
    private const string SuggestionsUrl = "/api/v1/ai/copilot/suggestions";

    public AiSuggestionButtonTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public void Catalog_points_only_at_existing_read_tools_allowed_for_the_same_role()
    {
        var roleTools = AiRoleToolCatalog.Definitions
            .Concat(PatientCopilotToolHandler.Definitions())
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var writeTools = AiRoleActionCatalog.Definitions.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var scope = Factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAiToolRegistry>();

        Assert.NotEmpty(AiSuggestionCatalog.Definitions);
        Assert.Equal(AiSuggestionCatalog.Definitions.Count, AiSuggestionCatalog.Definitions.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(AiSuggestionCatalog.Definitions.Count, AiSuggestionCatalog.Definitions.Select(x => x.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.All(AiSuggestionCatalog.Definitions, suggestion =>
        {
            Assert.Matches(AiSuggestionCatalog.CodePattern, suggestion.Code);
            Assert.True(suggestion.Code.Length <= AiSuggestionCatalog.MaxCodeLength);
            Assert.False(string.IsNullOrWhiteSpace(suggestion.Label));
            Assert.Contains(suggestion.Role, new[] { AiActorRole.Patient, AiActorRole.Doctor });
            Assert.StartsWith(suggestion.Role == AiActorRole.Patient ? "patient." : "doctor.", suggestion.Code, StringComparison.Ordinal);

            if (suggestion.ActionKind == AiSuggestionActionKind.Wizard)
            {
                Assert.Equal(AiActorRole.Patient, suggestion.Role);
                Assert.Equal("start", suggestion.WizardStep);
                Assert.Equal("patient.start_booking", suggestion.Code);
                Assert.Empty(suggestion.ToolName);
                Assert.False(suggestion.RequiresResource);
                return;
            }
            Assert.True(roleTools.TryGetValue(suggestion.ToolName, out var tool), suggestion.ToolName);
            Assert.True(registry.TryGetHandler(suggestion.ToolName, out _), suggestion.ToolName);
            // Read-only: low risk, no confirmation, planner-allowlisted, never a prepare/confirm tool.
            Assert.Equal(AiToolRiskLevel.Low, tool!.RiskLevel);
            Assert.Equal(AiToolConfirmationRequirement.None, tool.Confirmation);
            Assert.True(AiPlannerPolicy.IsAllowed(tool.Name));
            Assert.DoesNotContain(tool.Name, writeTools);
            Assert.DoesNotContain(".prepare_", tool.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("execute", tool.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(suggestion.Role, tool.AllowedRoles);
            Assert.Equal(AiToolAccessMode.RoleRestricted, tool.AccessMode);
            Assert.Equal(tool.ResourceBinding.RequiresCurrentResource, suggestion.RequiresResource);
            // No user-supplied required argument: only server-bound resource ids may be required.
            Assert.DoesNotContain(tool.ArgumentSchema, argument => argument.Required && !argument.ServerBound);
        });
        Assert.All(new[] { AiActorRole.Receptionist, AiActorRole.DiagnosticTechnician, AiActorRole.Pharmacist, AiActorRole.Admin },
            role => Assert.Empty(AiSuggestionCatalog.ForRole(role, hasCaseResource: true)));
        Assert.True(AiSuggestionCatalog.ForRole(AiActorRole.Doctor, true).Count <= AiSuggestionCatalog.MaxSuggestions);
        Assert.True(AiSuggestionCatalog.ForRole(AiActorRole.Patient, true).Count <= AiSuggestionCatalog.MaxSuggestions);
    }

    [Fact]
    public async Task Catalog_tools_are_advertised_by_the_role_catalog_endpoint()
    {
        foreach (var (email, role) in new[] { ("pat1@test.com", AiActorRole.Patient), ("doc@test.com", AiActorRole.Doctor) })
        {
            var client = await CreateAuthenticatedClientAsync(email);
            var response = await client.GetAsync("/api/v1/ai/copilot/catalog");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var names = document.RootElement.GetProperty("data").GetProperty("tools").EnumerateArray()
                .Select(x => x.GetProperty("name").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(AiSuggestionCatalog.Definitions.Where(x => x.Role == role && x.ActionKind == AiSuggestionActionKind.ReadTool), x => Assert.Contains(x.ToolName, names));
        }
    }

    [Fact]
    public async Task Patient_suggestions_return_the_same_cards_as_free_text_without_calling_the_provider()
    {
        ResetProviderSpy();
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var cases = new[]
        {
            (Code: "patient.my_appointments", Text: "Xem lịch hẹn của tôi", Card: "appointments"),
            (Code: "patient.my_visits", Text: "Xem lịch sử khám của tôi", Card: "patient_visits"),
            (Code: "patient.my_diagnostic_results", Text: "Xem kết quả xét nghiệm của tôi", Card: "patient_diagnostic_results"),
            (Code: "patient.my_prescriptions", Text: "Xem đơn thuốc của tôi", Card: "patient_prescriptions"),
            (Code: "patient.my_bills", Text: "Xem hóa đơn của tôi", Card: "patient_bills")
        };

        foreach (var item in cases)
        {
            var definition = AiSuggestionCatalog.Find(item.Code, AiActorRole.Patient)!;
            var freeText = await ChatAsync(client, new { message = item.Text, sessionId = NewSession("pat-free") });
            var suggested = await ChatAsync(client, new { message = definition.Label, suggestionCode = item.Code, sessionId = NewSession("pat-code") });

            AssertLocalExecution(suggested, definition.ToolName);
            Assert.Equal(item.Card, SingleCard(suggested).GetProperty("type").GetString());
            Assert.Equal(freeText.GetProperty("intent").GetString(), suggested.GetProperty("intent").GetString());
            Assert.Equal(freeText.GetProperty("subIntent").GetString(), suggested.GetProperty("subIntent").GetString());
            Assert.Equal(freeText.GetProperty("navigationRoute").GetString(), suggested.GetProperty("navigationRoute").GetString());
            Assert.Equal(freeText.GetProperty("plannerMode").GetString(), suggested.GetProperty("plannerMode").GetString());
            Assert.Equal(freeText.GetProperty("executionMode").GetString(), suggested.GetProperty("executionMode").GetString());
            Assert.Equal(SingleCard(freeText).GetProperty("type").GetString(), SingleCard(suggested).GetProperty("type").GetString());
            Assert.Equal(SingleCard(freeText).GetProperty("data").GetRawText(), SingleCard(suggested).GetProperty("data").GetRawText());

            var chips = Codes(suggested);
            Assert.DoesNotContain(item.Code, chips);
            Assert.All(chips, code => Assert.StartsWith("patient.", code, StringComparison.Ordinal));
            Assert.True(chips.Count <= AiSuggestionCatalog.MaxSuggestions);
        }

        AssertProviderNeverCalled();
    }

    [Fact]
    public async Task Patient_cannot_run_doctor_or_unknown_codes_and_no_tool_executes()
    {
        ResetProviderSpy();
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        foreach (var code in new[] { "doctor.my_queue", "doctor.patient_summary", "patient.unknown_thing", "reception.queue", "PATIENT.MY_APPOINTMENTS" })
        {
            var session = NewSession("pat-denied");
            var data = await ChatAsync(client, new { message = "Xem lịch hẹn của tôi", suggestionCode = code, sessionId = session });

            Assert.Equal(AiPlannerErrorCodes.ToolNotAllowed, data.GetProperty("errorCode").GetString());
            Assert.Equal("Clarifying", data.GetProperty("assistantMode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("clarification").GetString()));
            Assert.Empty(data.GetProperty("cards").EnumerateArray());
            Assert.Empty(data.GetProperty("executedToolNames").EnumerateArray());
            Assert.Equal("NotCalled", data.GetProperty("providerState").GetString());
            Assert.False(data.GetProperty("providerWasCalled").GetBoolean());
            Assert.All(Codes(data), chip => Assert.StartsWith("patient.", chip, StringComparison.Ordinal));
            await AssertNoToolAuditAsync(session);
        }

        AssertProviderNeverCalled();
    }

    [Fact]
    public async Task Suggestion_code_alone_selects_the_tool_and_client_supplied_tools_or_arguments_are_ignored()
    {
        ResetProviderSpy();
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var baseline = await ChatAsync(client, new { message = "Hóa đơn của tôi", suggestionCode = "patient.my_bills", sessionId = NewSession("pat-base") });

        // Message text that would route to a different tool, plus forged
        // tool/argument/role fields, must not change what executes.
        var forged = await ChatAsync(client, new
        {
            message = "Xem lịch hẹn của tôi",
            suggestionCode = "patient.my_bills",
            sessionId = NewSession("pat-forged"),
            toolName = "admin.get_dashboard_metrics",
            toolCalls = new[] { new { name = "doctor.get_my_queue", version = "1.0", arguments = new { userId = Guid.NewGuid() } } },
            arguments = new { page = 99, pageSize = 500, patientId = Patient2EntityId },
            role = "Admin",
            allowedTools = new[] { "admin.get_ai_health" }
        });

        AssertLocalExecution(forged, "patient.get_my_bills");
        Assert.Equal(SingleCard(baseline).GetProperty("data").GetRawText(), SingleCard(forged).GetProperty("data").GetRawText());
        AssertProviderNeverCalled();
    }

    [Fact]
    public async Task Doctor_queue_suggestion_runs_and_patient_codes_are_rejected()
    {
        ResetProviderSpy();
        var client = await CreateAuthenticatedClientAsync("doc@test.com");

        var queue = await ChatAsync(client, new { message = "Hôm nay tôi khám ai?", suggestionCode = "doctor.my_queue", sessionId = NewSession("doc-queue") });
        AssertLocalExecution(queue, "doctor.get_my_queue");
        Assert.Equal("doctor_queue", SingleCard(queue).GetProperty("type").GetString());
        Assert.Equal(AiChatIntentTypes.QueueLookup, queue.GetProperty("intent").GetString());
        Assert.DoesNotContain("doctor.my_queue", Codes(queue));
        Assert.DoesNotContain(Codes(queue), code => code is "doctor.patient_summary" or "doctor.diagnostic_orders" or "doctor.prescription_status");

        var freeText = await ChatAsync(client, new { message = "Xem hàng đợi của tôi", sessionId = NewSession("doc-queue-free") });
        Assert.Equal(freeText.GetProperty("subIntent").GetString(), queue.GetProperty("subIntent").GetString());
        Assert.Equal(SingleCard(freeText).GetProperty("type").GetString(), SingleCard(queue).GetProperty("type").GetString());

        var session = NewSession("doc-patient-code");
        var denied = await ChatAsync(client, new { message = "Lịch hẹn của tôi", suggestionCode = "patient.my_appointments", sessionId = session });
        Assert.Equal(AiPlannerErrorCodes.ToolNotAllowed, denied.GetProperty("errorCode").GetString());
        Assert.Empty(denied.GetProperty("cards").EnumerateArray());
        Assert.Empty(denied.GetProperty("executedToolNames").EnumerateArray());
        await AssertNoToolAuditAsync(session);
        AssertProviderNeverCalled();
    }

    [Fact]
    public async Task Doctor_case_suggestions_require_an_explicit_verified_resource()
    {
        ResetProviderSpy();
        var own = await CreateAppointmentAsync(DoctorEntityId, "SUG-OWN");
        var other = await CreateAppointmentAsync(Doctor2EntityId, "SUG-OTHER");
        var unassignedFacility = await CreateOwnAppointmentInUnassignedFacilityAsync();
        var client = await CreateAuthenticatedClientAsync("doc@test.com");

        foreach (var code in new[] { "doctor.patient_summary", "doctor.diagnostic_orders", "doctor.prescription_status" })
        {
            var missingSession = NewSession("doc-missing");
            var missing = await ChatAsync(client, new { message = "Tóm tắt", suggestionCode = code, sessionId = missingSession, currentRoute = "/doctor/appointments" });
            Assert.Equal(AiPlannerErrorCodes.ResourceContextRequired, missing.GetProperty("errorCode").GetString());
            Assert.Equal("Clarifying", missing.GetProperty("assistantMode").GetString());
            Assert.Empty(missing.GetProperty("cards").EnumerateArray());
            Assert.Empty(missing.GetProperty("executedToolNames").EnumerateArray());
            await AssertNoToolAuditAsync(missingSession);

            foreach (var (appointmentId, label) in new[] { (other, "other-doctor"), (unassignedFacility, "unassigned-facility") })
            {
                var deniedSession = NewSession($"doc-{label}");
                var denied = await ChatAsync(client, new { message = "Tóm tắt", suggestionCode = code, sessionId = deniedSession, resourceContext = new { appointmentId } });
                Assert.Equal("RESOURCE_SCOPE_DENIED", denied.GetProperty("errorCode").GetString());
                Assert.Empty(denied.GetProperty("cards").EnumerateArray());
                Assert.Empty(denied.GetProperty("executedToolNames").EnumerateArray());
                Assert.DoesNotContain(Codes(denied), chip => chip is "doctor.patient_summary" or "doctor.diagnostic_orders" or "doctor.prescription_status");
                await AssertNoToolAuditAsync(deniedSession);
            }

            var definition = AiSuggestionCatalog.Find(code, AiActorRole.Doctor)!;
            var allowed = await ChatAsync(client, new { message = definition.Label, suggestionCode = code, sessionId = NewSession("doc-own"), resourceContext = new { appointmentId = own } });
            AssertLocalExecution(allowed, definition.ToolName);
            Assert.DoesNotContain(code, Codes(allowed));
            Assert.Contains("doctor.my_queue", Codes(allowed));
        }

        // A resource remembered from an earlier turn in the same session is
        // not enough: case suggestions need the resource on this request.
        var remembered = NewSession("doc-memory");
        var first = await ChatAsync(client, new { message = "Tóm tắt bệnh nhân hiện tại", sessionId = remembered, resourceContext = new { appointmentId = own } });
        Assert.Equal("doctor_patient_summary", SingleCard(first).GetProperty("type").GetString());
        var second = await ChatAsync(client, new { message = "Chỉ định cận lâm sàng của ca này", suggestionCode = "doctor.diagnostic_orders", sessionId = remembered });
        Assert.Equal(AiPlannerErrorCodes.ResourceContextRequired, second.GetProperty("errorCode").GetString());
        Assert.Empty(second.GetProperty("cards").EnumerateArray());

        AssertProviderNeverCalled();
    }

    [Fact]
    public async Task Suggestions_behave_identically_with_provider_disabled_or_enabled_and_never_call_it()
    {
        var own = await CreateAppointmentAsync(DoctorEntityId, "SUG-CFG");
        var results = new Dictionary<bool, List<string>>();
        foreach (var enabled in new[] { false, true })
        {
            using var factory = CreateProviderConfiguredFactory(enabled);
            ResetProviderSpy();
            var patient = await LoginAsync(factory.CreateClient(), "pat1@test.com");
            var doctor = await LoginAsync(factory.CreateClient(), "doc@test.com");
            var fingerprints = new List<string>();
            foreach (var (client, body) in new (HttpClient, object)[]
            {
                (patient, new { message = "Lịch hẹn của tôi", suggestionCode = "patient.my_appointments", sessionId = NewSession("cfg-pat") }),
                (patient, new { message = "Đơn thuốc của tôi", suggestionCode = "patient.my_prescriptions", sessionId = NewSession("cfg-rx") }),
                (patient, new { message = "Hôm nay tôi khám ai?", suggestionCode = "doctor.my_queue", sessionId = NewSession("cfg-deny") }),
                (doctor, new { message = "Hôm nay tôi khám ai?", suggestionCode = "doctor.my_queue", sessionId = NewSession("cfg-doc") }),
                (doctor, new { message = "Tóm tắt bệnh nhân đang mở", suggestionCode = "doctor.patient_summary", sessionId = NewSession("cfg-sum"), resourceContext = new { appointmentId = own } }),
                (doctor, new { message = "Tóm tắt bệnh nhân đang mở", suggestionCode = "doctor.patient_summary", sessionId = NewSession("cfg-miss") })
            })
            {
                var data = await ChatAsync(client, body);
                Assert.False(data.GetProperty("providerWasCalled").GetBoolean());
                Assert.Equal(0, data.GetProperty("providerAttemptCount").GetInt32());
                Assert.Equal("NotCalled", data.GetProperty("providerState").GetString());
                fingerprints.Add(string.Join("|",
                    data.GetProperty("assistantMode").GetString(),
                    data.GetProperty("plannerMode").GetString(),
                    data.GetProperty("executionMode").GetString(),
                    data.GetProperty("intent").GetString(),
                    data.GetProperty("subIntent").GetString(),
                    data.GetProperty("errorCode").ToString(),
                    string.Join(",", data.GetProperty("executedToolNames").EnumerateArray().Select(x => x.GetString())),
                    string.Join(",", data.GetProperty("cards").EnumerateArray().Select(x => x.GetProperty("type").GetString())),
                    string.Join(",", Codes(data))));
            }

            AssertProviderNeverCalled();
            results[enabled] = fingerprints;

            // Control: the same spy does observe provider planning for an
            // ambiguous free-text turn, so the Times.Never checks above are real.
            if (enabled)
            {
                ResetProviderSpy();
                await doctor.PostAsJsonAsync(ChatUrl, new { message = "Tình hình hôm nay thế nào?", sessionId = NewSession("cfg-control") });
                Factory.MockAiProvider.Verify(x => x.PlanRoleCopilotAsync(It.IsAny<AiRolePlannerProviderRequest>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
                ResetProviderSpy();
            }
        }

        Assert.Equal(results[false], results[true]);
    }

    [Fact]
    public async Task Suggestion_turns_are_audited_and_leave_domain_data_unchanged()
    {
        ResetProviderSpy();
        var own = await CreateAppointmentAsync(DoctorEntityId, "SUG-FP");
        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        var before = await DomainFingerprintAsync();

        var patientSession = NewSession("audit-pat");
        var doctorSession = NewSession("audit-doc");
        foreach (var definition in AiSuggestionCatalog.Definitions.Where(x => x.Role == AiActorRole.Patient && x.ActionKind == AiSuggestionActionKind.ReadTool))
            AssertLocalExecution(await ChatAsync(patient, new { message = definition.Label, suggestionCode = definition.Code, sessionId = patientSession }), definition.ToolName);
        foreach (var definition in AiSuggestionCatalog.Definitions.Where(x => x.Role == AiActorRole.Doctor))
            AssertLocalExecution(await ChatAsync(doctor, new { message = definition.Label, suggestionCode = definition.Code, sessionId = doctorSession, resourceContext = new { appointmentId = own } }), definition.ToolName);
        await ChatAsync(patient, new { message = "x", suggestionCode = "doctor.my_queue", sessionId = patientSession });

        Assert.Equal(before, await DomainFingerprintAsync());

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (session, role, userId) in new[] { (patientSession, AiActorRole.Patient, Patient1Id), (doctorSession, AiActorRole.Doctor, DoctorId) })
        {
            var logs = await db.AiAuditLogs.AsNoTracking().Where(x => x.SessionId == session).ToListAsync();
            var turns = logs.Where(x => x.ActionType == "CopilotTurn").ToList();
            Assert.All(turns, x =>
            {
                Assert.Equal(userId, x.UserId);
                Assert.Contains("\"source\":\"role-copilot-suggestion\"", x.MetadataJson, StringComparison.Ordinal);
                Assert.DoesNotContain("Patient 1", x.MetadataJson ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("Doctor 1", x.MetadataJson ?? string.Empty, StringComparison.Ordinal);
            });
            var expectedTools = AiSuggestionCatalog.Definitions.Where(x => x.Role == role && x.ActionKind == AiSuggestionActionKind.ReadTool).Select(x => $"Tool:{x.ToolName}").OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedTools, logs.Where(x => x.ActionType.StartsWith("Tool:", StringComparison.Ordinal)).Select(x => x.ActionType).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.All(logs.Where(x => x.ActionType.StartsWith("Tool:", StringComparison.Ordinal)), x => Assert.Equal("completed", x.Outcome));
        }
        Assert.Equal(AiSuggestionCatalog.Definitions.Count(x => x.Role == AiActorRole.Patient && x.ActionKind == AiSuggestionActionKind.ReadTool) + 1,
            await db.AiAuditLogs.CountAsync(x => x.SessionId == patientSession && x.ActionType == "CopilotTurn"));
        Assert.Contains(await db.AiAuditLogs.Where(x => x.SessionId == patientSession && x.ActionType == "CopilotTurn").Select(x => x.MetadataJson).ToListAsync(),
            json => json!.Contains(AiPlannerErrorCodes.ToolNotAllowed, StringComparison.Ordinal));

        AssertProviderNeverCalled();
    }

    [Fact]
    public async Task Suggestion_menu_is_role_scoped_resource_checked_and_exposes_no_ids()
    {
        var own = await CreateAppointmentAsync(DoctorEntityId, "SUG-MENU");
        var other = await CreateAppointmentAsync(Doctor2EntityId, "SUG-MENU-X");

        var anonymous = await Factory.CreateClient().PostAsJsonAsync(SuggestionsUrl, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var patientMenu = await MenuAsync(patient, new { currentRoute = "/patient" });
        Assert.Equal("Patient", patientMenu.GetProperty("role").GetString());
        Assert.Equal(AiSuggestionCatalog.Definitions.Where(x => x.Role == AiActorRole.Patient).Select(x => x.Code).ToArray(), Codes(patientMenu).ToArray());
        // A patient sending a doctor's resource gets no doctor buttons.
        Assert.All(Codes(await MenuAsync(patient, new { resourceContext = new { appointmentId = own } })), code => Assert.StartsWith("patient.", code, StringComparison.Ordinal));

        var doctor = await CreateAuthenticatedClientAsync("doc@test.com");
        Assert.Equal(new[] { "doctor.my_queue" }, Codes(await MenuAsync(doctor, new { currentRoute = "/doctor/queue" })).ToArray());
        Assert.Equal(new[] { "doctor.my_queue" }, Codes(await MenuAsync(doctor, new { resourceContext = new { appointmentId = other } })).ToArray());
        var caseMenu = await MenuAsync(doctor, new { currentRoute = $"/doctor/appointments/{own}", resourceContext = new { appointmentId = own } });
        Assert.Equal(AiSuggestionCatalog.Definitions.Where(x => x.Role == AiActorRole.Doctor).Select(x => x.Code).ToArray(), Codes(caseMenu).ToArray());

        var raw = caseMenu.GetRawText();
        Assert.Equal(new[] { "role", "suggestions" }, caseMenu.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.All(caseMenu.GetProperty("suggestions").EnumerateArray(), item =>
            Assert.Equal(new[] { "code", "label" }, item.EnumerateObject().Select(x => x.Name).ToArray()));
        Assert.DoesNotContain(own.ToString(System.Globalization.CultureInfo.InvariantCulture), raw, StringComparison.Ordinal);
        Assert.DoesNotContain(DoctorId.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("get_", raw, StringComparison.Ordinal);

        var receptionist = await CreateAuthenticatedClientAsync("rec@test.com");
        Assert.Empty(Codes(await MenuAsync(receptionist, new { })));
        Assert.Empty(Codes(await ChatAsync(receptionist, new { message = "Xem hàng đợi tiếp nhận", sessionId = NewSession("rec-chips") })));
    }

    [Fact]
    public async Task Existing_ai_endpoint_rate_limit_applies_to_suggestion_menu_and_suggestion_chat()
    {
        using var lowLimit = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"{AiRateLimitOptions.SectionName}:TestingPermitLimit", "2");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AiRateLimitOptions.SectionName}:TestingPermitLimit"] = "2"
            }));
        });
        var menuClient = await LoginAsync(lowLimit.CreateClient(), "pat1@test.com");
        Assert.Equal(HttpStatusCode.OK, (await menuClient.PostAsJsonAsync(SuggestionsUrl, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await menuClient.PostAsJsonAsync(SuggestionsUrl, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await menuClient.PostAsJsonAsync(SuggestionsUrl, new { })).StatusCode);

        var chatClient = await LoginAsync(lowLimit.CreateClient(), "doc@test.com");
        object Body() => new { message = "Hôm nay tôi khám ai?", suggestionCode = "doctor.my_queue", sessionId = NewSession("rate") };
        Assert.Equal(HttpStatusCode.OK, (await chatClient.PostAsJsonAsync(ChatUrl, Body())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chatClient.PostAsJsonAsync(ChatUrl, Body())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await chatClient.PostAsJsonAsync(ChatUrl, Body())).StatusCode);
    }

    [Fact]
    public async Task Oversized_suggestion_code_is_rejected_by_request_validation()
    {
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await client.PostAsJsonAsync(ChatUrl, new { message = "Lịch hẹn của tôi", suggestionCode = "patient." + new string('a', 64) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private void ResetProviderSpy()
    {
        Factory.MockAiProvider.Reset();
        Factory.MockAiProvider.Invocations.Clear();
    }

    private void AssertProviderNeverCalled()
    {
        Factory.MockAiProvider.Verify(x => x.PlanRoleCopilotAsync(It.IsAny<AiRolePlannerProviderRequest>(), It.IsAny<CancellationToken>()), Times.Never());
        Factory.MockAiProvider.Verify(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        Factory.MockAiProvider.Verify(x => x.GetSuggestionsFromAiAsync(It.IsAny<string>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<CancellationToken>()), Times.Never());
        Factory.MockAiProvider.VerifyNoOtherCalls();
    }

    private static void AssertLocalExecution(JsonElement data, string toolName)
    {
        Assert.Equal("NotCalled", data.GetProperty("providerState").GetString());
        Assert.Equal("NotCalled", data.GetProperty("providerStatus").GetString());
        Assert.False(data.GetProperty("providerWasCalled").GetBoolean());
        Assert.Equal(0, data.GetProperty("providerAttemptCount").GetInt32());
        Assert.Equal("Deterministic", data.GetProperty("plannerMode").GetString());
        Assert.Equal("DeterministicFallback", data.GetProperty("executionMode").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("errorCode").ValueKind);
        Assert.Equal(new[] { toolName }, data.GetProperty("executedToolNames").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal("Dữ liệu đã kiểm chứng", SingleCard(data).GetProperty("title").GetString());
    }

    private static JsonElement SingleCard(JsonElement data) => Assert.Single(data.GetProperty("cards").EnumerateArray());

    private static List<string> Codes(JsonElement data) =>
        data.GetProperty("suggestions").EnumerateArray().Select(x => x.GetProperty("code").GetString()!).ToList();

    private static string NewSession(string prefix) => $"sess_sug_{prefix}_{Guid.NewGuid():N}";

    private static async Task<JsonElement> ChatAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(ChatUrl, body);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> MenuAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(SuggestionsUrl, body);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task AssertNoToolAuditAsync(string sessionId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AiAuditLogs.AnyAsync(x => x.SessionId == sessionId && x.ActionType == "CopilotTurn"));
        Assert.False(await db.AiAuditLogs.AnyAsync(x => x.SessionId == sessionId && x.ActionType.StartsWith("Tool:")));
    }

    private WebApplicationFactory<Program> CreateProviderConfiguredFactory(bool enabled) => Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("AiProvider:IsEnabled", enabled ? "true" : "false");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiProvider:IsEnabled"] = enabled ? "true" : "false"
        }));
    });

    private static async Task<HttpClient> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password = "Pass@123" });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", payload.GetProperty("data").GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Hash of every table except the AI audit log and AI session memory, which suggestion turns write by design.</summary>
    private async Task<string> DomainFingerprintAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AiAuditLogs", "AiSessions" };
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        var builder = new StringBuilder();
        foreach (var table in tables.Where(x => !excluded.Contains(x)))
        {
            builder.Append('#').Append(table).Append('\n');
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY rowid";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    builder.Append(value is byte[] bytes ? Convert.ToBase64String(bytes) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)).Append('\u001f');
                }
                builder.Append('\n');
            }
        }

        Assert.Contains("#Appointments", builder.ToString(), StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static int _slotSequence;

    // Unique (date, time) per fixture so synthetic slots never collide with
    // each other or with the seeded half-hour grid.
    private static (DateOnly Date, TimeOnly Start) NextSlot()
    {
        var sequence = Interlocked.Increment(ref _slotSequence);
        return (GetFutureWorkingDate(30 + sequence % 40), new TimeOnly(5, 7).AddMinutes(sequence * 7 % 600));
    }

    private async Task<long> CreateAppointmentAsync(long doctorId, string prefix)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (appointmentDate, startTime) = NextSlot();
        var facilityId = await db.Departments
            .Where(department => department.IsActive && department.SpecialtyId == SpecialtyEntityId)
            .OrderBy(department => department.Id)
            .Select(department => department.FacilityId)
            .FirstAsync();
        var slot = new AppointmentSlot
        {
            DoctorId = doctorId,
            SlotDate = appointmentDate,
            StartTime = startTime,
            EndTime = startTime.AddMinutes(5),
            IsBooked = true
        };
        db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentCode = $"{prefix}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            PatientId = Patient1EntityId,
            DoctorId = doctorId,
            SpecialtyId = SpecialtyEntityId,
            FacilityId = facilityId,
            AppointmentSlotId = slot.Id,
            AppointmentDate = appointmentDate,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Status = AppointmentStatus.Confirmed,
            Reason = "Kiểm thử nút gợi ý tổng hợp"
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<long> CreateOwnAppointmentInUnassignedFacilityAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = new Facility
        {
            Code = $"SUG-F-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
            Name = "Cơ sở tổng hợp chưa phân công",
            Address = "Địa chỉ tổng hợp kiểm thử",
            City = "Hồ Chí Minh",
            Phone = "02830009999",
            IsActive = true
        };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        var (date, startTime) = NextSlot();
        var slot = new AppointmentSlot
        {
            DoctorId = DoctorEntityId,
            SlotDate = date,
            StartTime = startTime,
            EndTime = startTime.AddMinutes(5),
            IsBooked = true
        };
        db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentCode = $"SUG-NOFAC-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            PatientId = Patient1EntityId,
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            FacilityId = facility.Id,
            AppointmentSlotId = slot.Id,
            AppointmentDate = date,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Status = AppointmentStatus.Confirmed,
            Reason = "Kiểm thử phạm vi cơ sở tổng hợp"
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }
}
